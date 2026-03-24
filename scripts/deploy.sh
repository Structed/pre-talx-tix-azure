#!/bin/bash
# First-time deployment of Pretix + Pretalx.
# Generates secrets, validates config, and starts all services.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
cd "$PROJECT_DIR"

echo "=== Deploying Pretix + Pretalx ==="

# Create .env from template if it doesn't exist
if [ ! -f .env ]; then
    echo "Creating .env from .env.example..."
    cp .env.example .env
    echo "Please edit .env with your domain and SMTP settings, then re-run this script."
    exit 1
fi

# Source .env to check values
set -a
source .env
set +a

# Validate required config
if [ -z "${DOMAIN:-}" ] || [ "$DOMAIN" = "yourdomain.com" ]; then
    echo "ERROR: Set DOMAIN in .env to your actual domain."
    exit 1
fi

# Generate secrets if empty
generate_secret() {
    openssl rand -base64 48 | tr -d '/+=' | head -c "$1"
}

CHANGED=false
if [ -z "${DB_PASSWORD:-}" ]; then
    sed -i "s/^DB_PASSWORD=$/DB_PASSWORD=$(generate_secret 32)/" .env
    CHANGED=true
    echo "Generated DB_PASSWORD"
fi
if [ -z "${PRETIX_SECRET_KEY:-}" ]; then
    sed -i "s/^PRETIX_SECRET_KEY=$/PRETIX_SECRET_KEY=$(generate_secret 50)/" .env
    CHANGED=true
    echo "Generated PRETIX_SECRET_KEY"
fi
if [ -z "${PRETALX_SECRET_KEY:-}" ]; then
    sed -i "s/^PRETALX_SECRET_KEY=$/PRETALX_SECRET_KEY=$(generate_secret 50)/" .env
    CHANGED=true
    echo "Generated PRETALX_SECRET_KEY"
fi

if [ "$CHANGED" = true ]; then
    echo "Secrets written to .env — keep this file safe!"
    echo ""
fi

# Pull images and start
echo "Pulling container images..."
docker compose pull --quiet

echo "Starting services..."
docker compose up -d

echo ""
echo "=== Deployment complete ==="
echo ""
echo "  Pretix:  https://tickets.${DOMAIN}"
echo "  Pretalx: https://talks.${DOMAIN}"
echo ""
echo "First-time setup:"
echo "  1. Wait ~30s for services to initialize"
echo "  2. Run migrations:  docker compose exec pretix pretix migrate"
echo "  3. Rebuild assets:  docker compose exec pretix pretix rebuild"
echo "  4. Run migrations:  docker compose exec pretalx pretalx migrate"
echo "  5. Rebuild assets:  docker compose exec pretalx pretalx rebuild"
echo ""
echo "Set up daily backups:"
echo "  ./scripts/backup.sh --install-cron"
