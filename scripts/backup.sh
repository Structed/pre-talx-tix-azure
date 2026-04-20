#!/bin/bash
# Back up both PostgreSQL databases.
# Usage:
#   ./scripts/backup.sh                 # Run backup now
#   ./scripts/backup.sh --install-cron  # Install daily 3 AM cron job
set -euo pipefail

# Ensure PATH includes Docker (cron runs with minimal PATH)
export PATH="/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:$PATH"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$SCRIPT_DIR/lib/common.sh"

init_project_dir
cd "$PROJECT_DIR"

# Install cron job if requested
if [ "${1:-}" = "--install-cron" ]; then
    LOG_DIR="$PROJECT_DIR/logs"
    mkdir -p "$LOG_DIR"
    CRON_CMD="0 3 * * * cd $PROJECT_DIR && ./scripts/backup.sh >> $LOG_DIR/backup.log 2>&1"
    # Get existing crontab (ignore error if none exists), filter out old backup entries, add new one
    # Note: grep -v returns 1 if no lines match, so we use || true to handle empty crontab
    ( crontab -l 2>/dev/null || true ) | ( grep -v "tixtalk-backup" || true ) | ( grep -v "scripts/backup.sh" || true ) | { cat; echo "$CRON_CMD"; } | crontab -
    log "Installed daily backup cron job (3:00 AM)."
    echo "Logs: $LOG_DIR/backup.log"
    exit 0
fi

# Load .env for DB credentials
load_env

BACKUP_DIR="$PROJECT_DIR/backups"
mkdir -p "$BACKUP_DIR"

# Ensure the backup directory owner/group matches the project directory
# (e.g., if it was previously created by root via cron).
project_owner="$(stat -c '%U:%G' "$PROJECT_DIR")"
backup_owner="$(stat -c '%U:%G' "$BACKUP_DIR")"
if [ "$backup_owner" != "$project_owner" ]; then
    if [ "$(id -u)" -eq 0 ]; then
        chown -R "$project_owner" "$BACKUP_DIR"
        log "Fixed backup directory ownership recursively to $project_owner"
    else
        log_error "Cannot use $BACKUP_DIR (owned by $backup_owner, expected $project_owner)"
        log_error "Fix with: sudo chown -R $project_owner $BACKUP_DIR"
        exit 1
    fi
fi

if [ ! -w "$BACKUP_DIR" ]; then
    log_error "Cannot write to $BACKUP_DIR (owned by $(stat -c '%U:%G' "$BACKUP_DIR"))"
    log_error "Fix with: sudo chown -R $project_owner $BACKUP_DIR"
    exit 1
fi
TIMESTAMP=$(date +%Y%m%d-%H%M%S)

log "Starting backup..."

# Dump both databases
for DB in pretix pretalx; do
    BACKUP_FILE="$BACKUP_DIR/${DB}_${TIMESTAMP}.sql.gz"
    docker compose exec -T postgres pg_dump -U "$DB_USER" "$DB" | gzip > "$BACKUP_FILE"
    SIZE=$(du -h "$BACKUP_FILE" | cut -f1)
    echo "  $DB → $BACKUP_FILE ($SIZE)"
done

# Clean up backups older than 30 days
find "$BACKUP_DIR" -name "*.sql.gz" -mtime +30 -delete 2>/dev/null || true

log "Backup complete."
