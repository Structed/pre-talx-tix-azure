#!/bin/bash
# Run periodic tasks for pretix and pretalx.
# Both apps require `runperiodic` to process background tasks like sending
# emails, expiring orders, cleaning up data, etc. The standalone Docker images
# do NOT run this automatically — it must be scheduled externally.
#
# Usage:
#   ./scripts/cron.sh                 # Run periodic tasks now
#   ./scripts/cron.sh --install       # Install cron job (every 5 minutes)
#   ./scripts/cron.sh --remove        # Remove the cron job
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
source "$SCRIPT_DIR/lib/common.sh"

init_project_dir
cd "$PROJECT_DIR"

# Install cron job if requested
if [ "${1:-}" = "--install" ]; then
    CRON_CMD="*/5 * * * * cd $PROJECT_DIR && ./scripts/cron.sh >> /var/log/tixtalk-cron.log 2>&1"
    ( crontab -l 2>/dev/null || true ) | ( grep -v "tixtalk-cron" || true ) | { cat; echo "$CRON_CMD"; } | crontab -
    log "Installed periodic task cron job (every 5 minutes)."
    echo "Logs: /var/log/tixtalk-cron.log"
    exit 0
fi

# Remove cron job if requested
if [ "${1:-}" = "--remove" ]; then
    ( crontab -l 2>/dev/null || true ) | ( grep -v "tixtalk-cron" || true ) | crontab -
    log "Removed periodic task cron job."
    exit 0
fi

log "Running periodic tasks..."

# Run pretix periodic tasks (sends emails, expires orders, processes payments, etc.)
if docker compose exec -T pretix pretix cron 2>&1; then
    echo "  pretix runperiodic OK"
else
    log_warn "pretix runperiodic failed"
fi

# Run pretalx periodic tasks (sends emails, processes notifications, etc.)
if docker compose exec -T pretalx pretalx cron 2>&1; then
    echo "  pretalx runperiodic OK"
else
    log_warn "pretalx runperiodic failed"
fi

log "Periodic tasks complete."
