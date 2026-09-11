#!/usr/bin/env bash
#
# Nightly backup of the PostgreSQL database running on this Lightsail instance.
#
# Installed by provision-lightsail.sh as a root cron entry. Run it by hand any time:
#
#   sudo bash /var/www/aadhi-api/current/deploy/backup-database.sh
#
# WHY THIS RUNS ON THE SERVER. PostgreSQL listens on localhost only, so nothing outside the
# instance — including a GitHub Actions runner — can reach it. The backup has to be taken here.
#
# WHY IT UPLOADS. A backup sitting on the same disk as the database is not a backup: it dies with
# the instance. When R2 credentials are present in the environment file the dump is copied into
# the same Cloudflare R2 bucket the site already uses, under db-backups/. Without them the dump
# is still written locally and the script says loudly that it never left the machine.
#
set -euo pipefail

DB_NAME="${DB_NAME:-aadhicrackers}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/aadhi}"
ENV_FILE="${ENV_FILE:-/etc/aadhi-api/aadhi-api.env}"
KEEP_DAYS="${KEEP_DAYS:-14}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
OUT="${BACKUP_DIR}/${DB_NAME}-${STAMP}.sql.gz"

log()  { printf '[backup %s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }
fail() { printf '[backup] ERROR: %s\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || fail "run as root (sudo), so it can read the environment file and write ${BACKUP_DIR}"
command -v pg_dump >/dev/null || fail "pg_dump not found — sudo apt-get install -y postgresql-client"

install -d -m 0700 "$BACKUP_DIR"

# ---------------------------------------------------------------------------
log "dumping ${DB_NAME}"
# ---------------------------------------------------------------------------
# --no-owner / --no-privileges keeps the dump restorable into a database owned by a
# different role, which is what you want when rebuilding onto a fresh instance.
sudo -u postgres pg_dump "$DB_NAME" --no-owner --no-privileges \
  | gzip -9 > "$OUT.partial"
mv "$OUT.partial" "$OUT"        # only name it .sql.gz once it is complete
chmod 0600 "$OUT"
log "wrote $OUT ($(du -h "$OUT" | cut -f1))"

# Refuse to call a suspiciously tiny dump a success — an empty or failed dump that
# silently "succeeds" is worse than no backup, because it hides the problem.
SIZE=$(stat -c %s "$OUT")
(( SIZE > 1024 )) || fail "dump is only ${SIZE} bytes — treating this as a failed backup"

# ---------------------------------------------------------------------------
# Off-site copy into the existing R2 bucket.
# ---------------------------------------------------------------------------
if [[ -f "$ENV_FILE" ]]; then
  # shellcheck disable=SC1090
  set -a; source "$ENV_FILE"; set +a
fi

if [[ -n "${Storage__R2__AccountId:-}" && -n "${Storage__R2__AccessKeyId:-}" \
   && -n "${Storage__R2__SecretAccessKey:-}" && -n "${Storage__R2__BucketName:-}" ]]; then

  if ! command -v curl >/dev/null; then
    log "curl missing — skipping the off-site copy"
  else
    KEY="db-backups/$(basename "$OUT")"
    log "uploading to R2 bucket ${Storage__R2__BucketName}/${KEY}"

    # curl signs the request itself (--aws-sigv4), so no AWS CLI or rclone is needed.
    HTTP=$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' \
      --request PUT \
      --aws-sigv4 "aws:amz:auto:s3" \
      --user "${Storage__R2__AccessKeyId}:${Storage__R2__SecretAccessKey}" \
      --header "Content-Type: application/gzip" \
      --upload-file "$OUT" \
      --max-time 900 \
      "https://${Storage__R2__AccountId}.r2.cloudflarestorage.com/${Storage__R2__BucketName}/${KEY}")

    if [[ "$HTTP" == "200" ]]; then
      log "uploaded to R2"
    else
      # Not fatal: the local copy exists. But it must be noticed.
      printf '[backup] WARNING: R2 upload failed with HTTP %s — this backup exists ONLY on this instance.\n' "$HTTP" >&2
    fi
  fi
else
  printf '[backup] WARNING: no R2 credentials in %s — this backup exists ONLY on this instance, and dies with it.\n' "$ENV_FILE" >&2
fi

# ---------------------------------------------------------------------------
log "pruning local dumps older than ${KEEP_DAYS} days"
# ---------------------------------------------------------------------------
# Local only. Copies already in R2 are kept until you remove them there, so a mistake
# discovered weeks later is still recoverable.
find "$BACKUP_DIR" -name "${DB_NAME}-*.sql.gz" -type f -mtime "+${KEEP_DAYS}" -print -delete

log "done"
