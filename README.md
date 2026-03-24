# Pretix + Pretalx on a VPS

Self-hosted [pretix](https://pretix.eu/) (ticketing) and [pretalx](https://pretalx.com/) (call for papers & scheduling) via docker-compose. Optimized for a single cheap VPS (e.g., Hetzner CX33 at ~€5.49/mo).

## What You Get

| Service | Image | Purpose |
|---------|-------|---------|
| **Caddy** | `caddy:2-alpine` | Reverse proxy + automatic Let's Encrypt TLS |
| **PostgreSQL** | `postgres:16-alpine` | Shared database (pretix + pretalx DBs) |
| **Redis** | `redis:7-alpine` | Shared cache + Celery task queue |
| **Pretix** | `pretix/standalone` | Ticketing at `tickets.yourdomain.com` |
| **Pretalx** | `pretalx/standalone` | CfP/scheduling at `talks.yourdomain.com` |

Estimated cost: **~€5-7/month** (Hetzner CX33: 4 vCPU, 8 GB RAM, 80 GB SSD).

## Prerequisites

- A VPS running Ubuntu 22.04+ or Debian 12+ (2+ GB RAM minimum, 4+ GB recommended)
- A domain with DNS access
- SSH access to the server

## Quick Start

### 1. Set up DNS

Point two A records to your server's IP address:

```
tickets.yourdomain.com → <server-ip>
talks.yourdomain.com   → <server-ip>
```

### 2. Clone and configure

SSH into your server, then:

```bash
git clone <this-repo>
cd pre-talx-tix-azure

# Install Docker if needed
sudo ./scripts/setup.sh

# Create your configuration
cp .env.example .env
nano .env  # Set DOMAIN, SMTP settings
```

### 3. Deploy

```bash
./scripts/deploy.sh
```

This will:
- Generate secure random passwords for the database and app secret keys
- Pull all container images
- Start everything

### 4. Initialize (first time only)

Wait ~30 seconds for services to start, then:

```bash
docker compose exec pretix pretix migrate
docker compose exec pretix pretix rebuild
docker compose exec pretalx pretalx migrate
docker compose exec pretalx pretalx rebuild
```

### 5. Access your apps

- **Pretix**: `https://tickets.yourdomain.com`
- **Pretalx**: `https://talks.yourdomain.com`

Both apps have web-based setup wizards on first visit.

## Updating Containers

```bash
# Pull latest images and restart
./scripts/update.sh

# Pin a specific version
./scripts/update.sh --pretix 2025.1.0

# Update both
./scripts/update.sh --pretix 2025.1.0 --pretalx 2025.1.0
```

## Backups

### Manual backup

```bash
./scripts/backup.sh
```

Saves gzipped SQL dumps to `backups/` with timestamps.

### Automatic daily backups

```bash
./scripts/backup.sh --install-cron
```

Runs at 3:00 AM daily. Backups older than 30 days are auto-deleted.

### Restore from backup

```bash
./scripts/restore.sh backups/pretix_20260324-030000.sql.gz pretix
```

## Yearly Events

Both apps are multi-tenant — create new events in the web UI each year. No infrastructure changes needed:

- **Pretix**: `https://tickets.yourdomain.com/<organizer>/<year>/`
- **Pretalx**: `https://talks.yourdomain.com/<event-slug>/`

## Configuration Reference

All configuration is in `.env`:

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `DOMAIN` | Yes | — | Your domain (e.g., `yourdomain.com`) |
| `DB_USER` | No | `pretalxtix` | PostgreSQL username |
| `DB_PASSWORD` | No | auto-generated | PostgreSQL password |
| `PRETIX_SECRET_KEY` | No | auto-generated | Pretix secret key |
| `PRETALX_SECRET_KEY` | No | auto-generated | Pretalx secret key |
| `PRETIX_IMAGE_TAG` | No | `stable` | Pretix Docker image tag |
| `PRETALX_IMAGE_TAG` | No | `latest` | Pretalx Docker image tag |
| `MAIL_FROM` | Yes | — | Email sender address |
| `SMTP_HOST` | Yes | — | SMTP server hostname |
| `SMTP_PORT` | No | `587` | SMTP server port |
| `SMTP_USER` | Yes | — | SMTP username |
| `SMTP_PASSWORD` | Yes | — | SMTP password |

## Troubleshooting

### Check service status
```bash
docker compose ps
docker compose logs pretix --tail 50
docker compose logs pretalx --tail 50
```

### TLS certificate not working
Caddy auto-provisions Let's Encrypt certs. Ensure:
- DNS A records are pointing to the server
- Ports 80 and 443 are open (`sudo ufw status`)
- Check Caddy logs: `docker compose logs caddy`

### Database connection issues
```bash
docker compose exec postgres psql -U pretalxtix -l
```

### Restart everything
```bash
docker compose down && docker compose up -d
```

## Destroying Everything

```bash
docker compose down -v  # -v removes all data volumes
```
