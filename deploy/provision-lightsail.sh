#!/usr/bin/env bash
#
# One-time provisioning for the Aadhi Crackers API on an AWS Lightsail Linux instance.
#
# Target: Ubuntu 22.04 or 24.04 LTS ("OS Only" blueprint). Run ONCE, as a user with sudo:
#
#   ssh ubuntu@<lightsail-static-ip>
#   git clone https://github.com/arun2796/Aadhi_API_live.git /tmp/aadhi
#   sudo bash /tmp/aadhi/deploy/provision-lightsail.sh
#
# It is idempotent — re-running is safe and will not clobber an existing database
# or the environment file. If you have ALREADY installed PostgreSQL yourself, that is fine:
# apt-get skips what is present, and the database/role step is skipped if they already exist,
# so the script effectively just fills in whatever is missing.
#
# What it does NOT do: install the .NET runtime. Releases are published
# self-contained, so the server needs no .NET at all.
#
set -euo pipefail

APP_USER="aadhi"
APP_ROOT="/var/www/aadhi-api"
ENV_DIR="/etc/aadhi-api"
ENV_FILE="${ENV_DIR}/aadhi-api.env"
DB_NAME="aadhicrackers"
DB_USER="aadhi"
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

log() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m[warn] %s\033[0m\n' "$*"; }

if [[ $EUID -ne 0 ]]; then
  echo "Run this with sudo: sudo bash $0" >&2
  exit 1
fi

# ---------------------------------------------------------------------------
log "Installing packages"
# ---------------------------------------------------------------------------
export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get install -y --no-install-recommends \
  postgresql postgresql-contrib \
  nginx \
  certbot python3-certbot-nginx \
  rsync curl ca-certificates unzip \
  ufw fail2ban

# ---------------------------------------------------------------------------
log "Creating the service account and release directories"
# ---------------------------------------------------------------------------
if ! id -u "$APP_USER" >/dev/null 2>&1; then
  # System account, no shell, no home: it exists only to own and run the process.
  adduser --system --group --no-create-home --shell /usr/sbin/nologin "$APP_USER"
  echo "created user $APP_USER"
else
  echo "user $APP_USER already exists"
fi

install -d -o "$APP_USER" -g "$APP_USER" -m 0755 "$APP_ROOT"
install -d -o "$APP_USER" -g "$APP_USER" -m 0755 "$APP_ROOT/releases"

# The deploying SSH user needs to write into the release directory over rsync.
DEPLOY_USER="${SUDO_USER:-ubuntu}"
if id -u "$DEPLOY_USER" >/dev/null 2>&1; then
  usermod -aG "$APP_USER" "$DEPLOY_USER"
  chmod 0775 "$APP_ROOT" "$APP_ROOT/releases"
  echo "added $DEPLOY_USER to the $APP_USER group (log out and back in for it to take effect)"
fi

# ---------------------------------------------------------------------------
log "Configuring PostgreSQL"
# ---------------------------------------------------------------------------
systemctl enable --now postgresql

# Fail here with a clear message rather than letting every psql call below fail one by one.
if ! sudo -u postgres psql -tAc 'SELECT 1' >/dev/null 2>&1; then
  echo "Cannot connect to PostgreSQL as the 'postgres' system user." >&2
  echo "Check it is running ('systemctl status postgresql') and that this is a standard" >&2
  echo "Debian/Ubuntu package install with peer authentication for the postgres user." >&2
  exit 1
fi

echo "PostgreSQL $(sudo -u postgres psql -tAc 'SHOW server_version') is running"

DB_EXISTS="$(sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='${DB_NAME}'" || true)"
if [[ "$DB_EXISTS" == "1" ]]; then
  warn "database '${DB_NAME}' already exists — leaving it and its password untouched"
  DB_PASSWORD=""
else
  DB_PASSWORD="$(openssl rand -base64 30 | tr -d '/+=' | cut -c1-28)"
  sudo -u postgres psql -v ON_ERROR_STOP=1 <<SQL
CREATE ROLE ${DB_USER} LOGIN PASSWORD '${DB_PASSWORD}';
CREATE DATABASE ${DB_NAME} OWNER ${DB_USER};
SQL
  # EF migrations create everything inside the public schema of this database.
  sudo -u postgres psql -v ON_ERROR_STOP=1 -d "${DB_NAME}" <<SQL
GRANT ALL ON SCHEMA public TO ${DB_USER};
ALTER SCHEMA public OWNER TO ${DB_USER};
SQL
  echo "created database '${DB_NAME}' owned by '${DB_USER}'"
fi

# PostgreSQL stays bound to localhost. Nothing outside this instance connects to it;
# for admin access use an SSH tunnel:  ssh -L 5432:localhost:5432 ubuntu@<ip>
PG_CONF="$(sudo -u postgres psql -tAc 'SHOW config_file')"
if grep -qE "^\s*listen_addresses\s*=\s*'\*'" "$PG_CONF"; then
  warn "listen_addresses is '*' in $PG_CONF — set it back to 'localhost' unless you really need remote access"
fi

# ---------------------------------------------------------------------------
log "Installing the systemd unit"
# ---------------------------------------------------------------------------
install -m 0644 "${REPO_DIR}/deploy/aadhi-api.service" /etc/systemd/system/aadhi-api.service
systemctl daemon-reload
systemctl enable aadhi-api >/dev/null
echo "installed /etc/systemd/system/aadhi-api.service (not started — no release deployed yet)"

# ---------------------------------------------------------------------------
log "Preparing the environment file"
# ---------------------------------------------------------------------------
install -d -m 0750 "$ENV_DIR"
if [[ -f "$ENV_FILE" ]]; then
  warn "$ENV_FILE already exists — leaving it alone"
else
  install -m 0600 "${REPO_DIR}/deploy/aadhi-api.env.example" "$ENV_FILE"
  if [[ -n "$DB_PASSWORD" ]]; then
    sed -i "s|Password=REPLACE_WITH_DB_PASSWORD|Password=${DB_PASSWORD}|" "$ENV_FILE"
  fi
  # A JWT key can be generated here; the rest are real secrets only you have.
  sed -i "s|JwtSettings__SecretKey=REPLACE_WITH_A_LONG_RANDOM_SECRET|JwtSettings__SecretKey=$(openssl rand -base64 48 | tr -d '\n')|" "$ENV_FILE"
  echo "created $ENV_FILE from the template"
fi
chown root:root "$ENV_FILE"
chmod 0600 "$ENV_FILE"

# ---------------------------------------------------------------------------
log "Configuring nginx"
# ---------------------------------------------------------------------------
if [[ ! -f /etc/nginx/sites-available/aadhi-api ]]; then
  install -m 0644 "${REPO_DIR}/deploy/nginx-aadhi-api.conf" /etc/nginx/sites-available/aadhi-api
  echo "installed /etc/nginx/sites-available/aadhi-api"
  warn "edit it and replace api.aadhicracker.in with your hostname, then enable it:"
  warn "  sudo ln -sf /etc/nginx/sites-available/aadhi-api /etc/nginx/sites-enabled/aadhi-api"
  warn "  sudo rm -f /etc/nginx/sites-enabled/default"
  warn "  sudo certbot --nginx -d <your-api-hostname>"
  warn "  sudo nginx -t && sudo systemctl reload nginx"
else
  warn "/etc/nginx/sites-available/aadhi-api already exists — leaving it alone"
fi
systemctl enable --now nginx

# ---------------------------------------------------------------------------
log "Scheduling nightly database backups"
# ---------------------------------------------------------------------------
install -m 0755 "${REPO_DIR}/deploy/backup-database.sh" /usr/local/bin/aadhi-backup-database
install -d -m 0700 /var/backups/aadhi

# 21:00 UTC = 02:30 IST, comfortably outside shopping hours. Output goes to a log
# rather than root's mail spool, which nobody reads.
CRON_LINE='0 21 * * * /usr/local/bin/aadhi-backup-database >> /var/log/aadhi-backup.log 2>&1'
if crontab -l 2>/dev/null | grep -Fq "aadhi-backup-database"; then
  warn "backup cron entry already present — leaving it alone"
else
  (crontab -l 2>/dev/null; echo "$CRON_LINE") | crontab -
  echo "installed nightly backup cron (21:00 UTC)"
fi

# ---------------------------------------------------------------------------
log "Firewall"
# ---------------------------------------------------------------------------
# ufw on the instance. The Lightsail console has its OWN firewall in front of this —
# open 22, 80 and 443 there too, and nothing else. Port 5432 must stay closed in both.
ufw allow OpenSSH >/dev/null
ufw allow 'Nginx Full' >/dev/null
ufw --force enable >/dev/null
ufw status verbose
systemctl enable --now fail2ban

# ---------------------------------------------------------------------------
log "Done"
# ---------------------------------------------------------------------------
cat <<EOF

Next steps
----------
1. Fill in the remaining secrets:
     sudo nano ${ENV_FILE}
   (R2 credentials, Cors__AllowedOrigins, Seeding__AdminPassword)

EOF

if [[ -n "$DB_PASSWORD" ]]; then
  cat <<EOF
   The database password was generated and already written into that file.
   It is shown here ONCE — store it in your password manager now:

       ${DB_PASSWORD}

EOF
fi

cat <<EOF
2. Point DNS at this instance's Lightsail STATIC IP, then run certbot
   (see the warnings above) so nginx serves HTTPS.

3. Nothing to import: the database starts empty and the API creates the whole
   schema from its EF migrations on first boot.

4. Add these GitHub repository secrets so Actions can deploy:
     LIGHTSAIL_HOST        this instance's static IP or hostname
     LIGHTSAIL_USER        ${DEPLOY_USER}
     LIGHTSAIL_SSH_KEY     the PRIVATE key matching an authorized_keys entry here

5. Push to the deploy branch, or run the "Deploy API to Lightsail" workflow manually.

Useful afterwards:
     sudo systemctl status aadhi-api
     journalctl -u aadhi-api -f
EOF
