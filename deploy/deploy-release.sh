#!/usr/bin/env bash
#
# Activates a release that has already been rsynced to /var/www/aadhi-api/releases/<id>.
# Run on the Lightsail instance; the GitHub Actions workflow calls it over SSH.
#
#   bash deploy-release.sh <release-id>
#
# Flow: flip the "current" symlink -> restart the service -> poll /health/ready.
# If the new release fails to come up, the symlink is put back and the previous
# release is restarted, so a bad deploy self-heals instead of leaving the site down.
#
set -euo pipefail

APP_ROOT="/var/www/aadhi-api"
RELEASES_DIR="${APP_ROOT}/releases"
CURRENT_LINK="${APP_ROOT}/current"
SERVICE="aadhi-api"
HEALTH_URL="http://127.0.0.1:5000/health/ready"
HEALTH_TIMEOUT_SECONDS=90
KEEP_RELEASES=5

RELEASE_ID="${1:-}"
if [[ -z "$RELEASE_ID" ]]; then
  echo "usage: $0 <release-id>" >&2
  exit 2
fi

RELEASE_DIR="${RELEASES_DIR}/${RELEASE_ID}"
log() { printf '\033[1;36m==> %s\033[0m\n' "$*"; }
fail() { printf '\033[1;31m[error] %s\033[0m\n' "$*" >&2; exit 1; }

[[ -d "$RELEASE_DIR" ]] || fail "release directory $RELEASE_DIR does not exist"
[[ -f "${RELEASE_DIR}/AadhiCrackers.Api" ]] || fail "no AadhiCrackers.Api executable in $RELEASE_DIR"

# Remember where we were, so a failed deploy can go back.
PREVIOUS_RELEASE=""
if [[ -L "$CURRENT_LINK" ]]; then
  PREVIOUS_RELEASE="$(basename "$(readlink -f "$CURRENT_LINK")")"
  log "current release is ${PREVIOUS_RELEASE}"
else
  log "no current release yet — this is the first deploy"
fi

chmod +x "${RELEASE_DIR}/AadhiCrackers.Api"
sudo chown -R aadhi:aadhi "$RELEASE_DIR"

activate() {
  local id="$1"
  # ln -T avoids the classic trap where, because "current" is already a symlink to a
  # directory, a plain "ln -sfn" would create the link *inside* the old target.
  sudo ln -sfnT "${RELEASES_DIR}/${id}" "$CURRENT_LINK"
  sudo systemctl restart "$SERVICE"
}

wait_for_health() {
  local deadline=$(( SECONDS + HEALTH_TIMEOUT_SECONDS ))
  while (( SECONDS < deadline )); do
    if ! systemctl is-active --quiet "$SERVICE"; then
      return 1   # the process died; no point waiting out the timeout
    fi
    if curl --fail --silent --max-time 5 "$HEALTH_URL" >/dev/null 2>&1; then
      return 0
    fi
    sleep 3
  done
  return 1
}

log "activating release ${RELEASE_ID}"
activate "$RELEASE_ID"

log "waiting for ${HEALTH_URL} (up to ${HEALTH_TIMEOUT_SECONDS}s)"
if wait_for_health; then
  log "healthy"
else
  printf '\033[1;31m[error] release %s did not become healthy\033[0m\n' "$RELEASE_ID" >&2
  echo "--- last 60 log lines ---" >&2
  sudo journalctl -u "$SERVICE" -n 60 --no-pager >&2 || true

  if [[ -n "$PREVIOUS_RELEASE" && -d "${RELEASES_DIR}/${PREVIOUS_RELEASE}" ]]; then
    log "rolling back to ${PREVIOUS_RELEASE}"
    activate "$PREVIOUS_RELEASE"
    if wait_for_health; then
      fail "deploy failed; rolled back to ${PREVIOUS_RELEASE}, which is healthy"
    fi
    fail "deploy failed AND the rollback to ${PREVIOUS_RELEASE} is also unhealthy — the site is down, investigate now"
  fi
  fail "deploy failed and there is no previous release to roll back to"
fi

# Keep the last few releases so a rollback is always one symlink away.
log "pruning old releases (keeping ${KEEP_RELEASES})"
cd "$RELEASES_DIR"
CURRENT_TARGET="$(basename "$(readlink -f "$CURRENT_LINK")")"
ls -1td */ 2>/dev/null | tail -n "+$((KEEP_RELEASES + 1))" | while read -r old; do
  old="${old%/}"
  [[ "$old" == "$CURRENT_TARGET" ]] && continue
  echo "  removing $old"
  sudo rm -rf -- "$old"
done

log "deployed ${RELEASE_ID}"
systemctl --no-pager status "$SERVICE" | head -n 5
