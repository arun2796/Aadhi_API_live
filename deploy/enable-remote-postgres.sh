#!/usr/bin/env bash
#
# Opens the Lightsail PostgreSQL to a SPECIFIC remote address, over TLS.
#
#   sudo bash deploy/enable-remote-postgres.sh 203.0.113.45/32
#   sudo bash deploy/enable-remote-postgres.sh --disable
#
# READ THIS FIRST. You almost certainly want an SSH tunnel instead:
#
#     ssh -L 5432:localhost:5432 ubuntu@<static-ip>
#     # then point psql / pgAdmin / DBeaver at localhost:5432
#
# The tunnel needs no server change, exposes nothing, is already encrypted and authenticated by
# your SSH key, and keeps working when your home IP changes. This script exists for the cases a
# tunnel genuinely cannot cover — a BI tool or a managed service that must dial the database
# directly — and it deliberately refuses to open the database to the whole internet.
#
# WHAT AN OPEN 5432 COSTS YOU. A PostgreSQL port reachable from 0.0.0.0/0 starts receiving
# automated login attempts within hours. Every authentication attempt is a chance for a weak
# password, and every PostgreSQL authentication CVE becomes remotely reachable the day it is
# published. Restricting the source address is what keeps that surface at zero.
#
set -euo pipefail

DB_NAME="${DB_NAME:-aadhicrackers}"
DB_USER="${DB_USER:-aadhi}"
MARKER="# --- managed by enable-remote-postgres.sh ---"
MARKER_END="# --- end enable-remote-postgres.sh ---"

log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m[warn] %s\033[0m\n' "$*"; }
fail() { printf '\033[1;31m[error] %s\033[0m\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || fail "run with sudo"

CONF="$(sudo -u postgres psql -tAc 'SHOW config_file')"
HBA="$(sudo -u postgres psql -tAc 'SHOW hba_file')"
[[ -f "$CONF" && -f "$HBA" ]] || fail "could not locate postgresql.conf / pg_hba.conf"

strip_managed_block() {
  # Remove any previous block this script added, so re-running replaces rather than stacks.
  sed -i "/^${MARKER}$/,/^${MARKER_END}$/d" "$1"
}

# ---------------------------------------------------------------------------
# --disable: put it back to localhost-only.
# ---------------------------------------------------------------------------
if [[ "${1:-}" == "--disable" ]]; then
  log "Reverting to localhost-only"
  strip_managed_block "$HBA"
  sed -i "s/^listen_addresses *=.*/listen_addresses = 'localhost'/" "$CONF"
  ufw delete allow 5432/tcp 2>/dev/null || true
  while ufw status numbered | grep -q '5432'; do
    RULE=$(ufw status numbered | grep -m1 '5432' | sed 's/^\[ *\([0-9]*\).*/\1/')
    yes | ufw delete "$RULE" >/dev/null
  done
  systemctl reload postgresql
  log "Done — PostgreSQL listens on localhost only again"
  echo "Close port 5432 in the Lightsail console firewall too, if you opened it there."
  exit 0
fi

# ---------------------------------------------------------------------------
# Validate the source address.
# ---------------------------------------------------------------------------
CIDR="${1:-}"
[[ -n "$CIDR" ]] || fail "usage: sudo bash $0 <your-ip>/32   (find yours with: curl -s ifconfig.me)
       or: sudo bash $0 --disable"

if [[ ! "$CIDR" =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}/[0-9]{1,2}$ ]]; then
  fail "'$CIDR' is not a CIDR. Use something like 203.0.113.45/32 — the /32 means 'this one address'."
fi

if [[ "$CIDR" == "0.0.0.0/0" ]]; then
  fail "Refusing to open the database to every address on the internet.
Pass your own address instead:  curl -s ifconfig.me   ->   sudo bash $0 <that>/32
If a service really needs access from anywhere, put it behind a tunnel or a VPN, not this."
fi

PREFIX="${CIDR##*/}"
if (( PREFIX < 24 )); then
  warn "/$PREFIX covers $(( 2 ** (32 - PREFIX) )) addresses. A single machine needs /32."
fi

# ---------------------------------------------------------------------------
log "Backing up the current configuration"
# ---------------------------------------------------------------------------
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
cp -a "$CONF" "${CONF}.bak-${STAMP}"
cp -a "$HBA"  "${HBA}.bak-${STAMP}"
echo "saved ${CONF}.bak-${STAMP}"
echo "saved ${HBA}.bak-${STAMP}"

# ---------------------------------------------------------------------------
log "Enabling TLS"
# ---------------------------------------------------------------------------
# Without this the password crosses the internet under nothing but the connection itself.
# Debian/Ubuntu ships a self-signed certificate via the ssl-cert package and enables ssl by
# default; this only asserts it, rather than assuming.
if ! grep -qE "^\s*ssl\s*=\s*on" "$CONF"; then
  if grep -qE "^\s*#?\s*ssl\s*=" "$CONF"; then
    sed -i "s/^\s*#\?\s*ssl\s*=.*/ssl = on/" "$CONF"
  else
    printf '\nssl = on\n' >> "$CONF"
  fi
  echo "set ssl = on"
else
  echo "ssl already on"
fi

# ---------------------------------------------------------------------------
log "Listening on all interfaces (pg_hba + firewall are what restrict access)"
# ---------------------------------------------------------------------------
if grep -qE "^\s*#?\s*listen_addresses\s*=" "$CONF"; then
  sed -i "s/^\s*#\?\s*listen_addresses\s*=.*/listen_addresses = '*'/" "$CONF"
else
  printf "\nlisten_addresses = '*'\n" >> "$CONF"
fi
echo "listen_addresses = '*'"

# ---------------------------------------------------------------------------
log "Allowing ${CIDR} to reach ${DB_NAME} as ${DB_USER}"
# ---------------------------------------------------------------------------
strip_managed_block "$HBA"
cat >> "$HBA" <<EOF
${MARKER}
# hostssl, not host: a plain 'host' line would let a client negotiate an unencrypted
# session and send the password in the clear. scram-sha-256 never sends the password itself.
# Scoped to one database and one role on purpose — not 'all all'.
hostssl ${DB_NAME}    ${DB_USER}    ${CIDR}    scram-sha-256
${MARKER_END}
EOF
echo "added a hostssl rule for ${CIDR}"

# ---------------------------------------------------------------------------
log "Firewall"
# ---------------------------------------------------------------------------
ufw allow from "${CIDR}" to any port 5432 proto tcp >/dev/null
echo "ufw: 5432/tcp allowed from ${CIDR} only"

# ---------------------------------------------------------------------------
log "Reloading PostgreSQL"
# ---------------------------------------------------------------------------
# listen_addresses needs a full restart; everything else would take a reload.
systemctl restart postgresql
sleep 2
systemctl is-active --quiet postgresql || fail "PostgreSQL did not come back up. Restore with:
  cp ${CONF}.bak-${STAMP} ${CONF} && cp ${HBA}.bak-${STAMP} ${HBA} && systemctl restart postgresql"

sudo -u postgres psql -tAc "SELECT 'listening on: ' || setting FROM pg_settings WHERE name='listen_addresses'"

cat <<EOF

Done.

Connect from ${CIDR%%/*}:

  psql "host=<static-ip> port=5432 dbname=${DB_NAME} user=${DB_USER} sslmode=require"

In pgAdmin / DBeaver: host = the static IP, port 5432, database ${DB_NAME},
user ${DB_USER}, and SSL mode "require".

TWO MORE THINGS, or it will not work / will not be safe:

1. Open port 5432 in the LIGHTSAIL CONSOLE firewall as well
   (Networking -> IPv4 Firewall -> Add rule -> Custom, TCP, 5432),
   and set "Restricted to IP address" -> ${CIDR%%/*}.
   ufw on the instance and the Lightsail firewall are two separate gates; both must allow it.

2. Your home broadband address almost certainly changes. When it does, this stops working and
   you re-run this script with the new address. That recurring annoyance is the main practical
   reason an SSH tunnel is the better answer:

       ssh -L 5432:localhost:5432 ubuntu@<static-ip>

To undo everything:  sudo bash $0 --disable
EOF
