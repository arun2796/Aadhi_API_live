# Database backup & restore

PostgreSQL runs **on the Lightsail instance itself** and listens on localhost only. Nothing
outside the instance can reach it — that includes a GitHub Actions runner, which is why backups
are taken by a cron job on the server rather than by a workflow.

## How backups are taken

[`deploy/backup-database.sh`](deploy/backup-database.sh), installed by
[`deploy/provision-lightsail.sh`](deploy/provision-lightsail.sh) as
`/usr/local/bin/aadhi-backup-database` and run nightly at **21:00 UTC** (02:30 IST).

Each run:

1. `pg_dump` → gzip → `/var/backups/aadhi/aadhicrackers-<timestamp>.sql.gz` (mode 0600).
2. Fails loudly if the dump is under 1 KB — a silent empty backup is worse than none.
3. Uploads a copy to the Cloudflare R2 bucket under `db-backups/`, using the R2 credentials
   already in `/etc/aadhi-api/aadhi-api.env`. **A backup that only lives on the same disk as the
   database is not a backup** — it dies with the instance.
4. Prunes local dumps older than 14 days. Copies in R2 are left alone.

Check it is working:

```bash
ls -lh /var/backups/aadhi/
tail -n 40 /var/log/aadhi-backup.log
sudo /usr/local/bin/aadhi-backup-database    # run one on demand
```

If the log says *"this backup exists ONLY on this instance"*, the R2 keys are missing from the
environment file — fix that, because the off-site copy is the half that matters.

Also turn on **Lightsail automatic snapshots** in the console. They capture the whole instance,
not just the database, and they are the fastest way back from a broken machine.

## Restoring

### Onto the same instance

```bash
sudo systemctl stop aadhi-api          # nothing must be writing during a restore

# Take a safety dump of what is there now, in case the restore is the mistake.
sudo -u postgres pg_dump aadhicrackers | gzip > /var/backups/aadhi/pre-restore-$(date -u +%FT%H%M%SZ).sql.gz

sudo -u postgres psql -v ON_ERROR_STOP=1 -d aadhicrackers <<'SQL'
DROP SCHEMA IF EXISTS public CASCADE;
CREATE SCHEMA public AUTHORIZATION aadhi;
GRANT ALL ON SCHEMA public TO aadhi;
SQL

gunzip -c /var/backups/aadhi/aadhicrackers-<timestamp>.sql.gz \
  | sudo -u postgres psql -v ON_ERROR_STOP=1 --single-transaction -d aadhicrackers

sudo systemctl start aadhi-api
```

`--single-transaction` means a restore that fails part-way leaves the database untouched rather
than half-populated.

### Fetching a backup out of R2

```bash
set -a; source /etc/aadhi-api/aadhi-api.env; set +a

# List what is there
curl --aws-sigv4 "aws:amz:auto:s3" \
  --user "${Storage__R2__AccessKeyId}:${Storage__R2__SecretAccessKey}" \
  "https://${Storage__R2__AccountId}.r2.cloudflarestorage.com/${Storage__R2__BucketName}?list-type=2&prefix=db-backups/"

# Download one
curl --aws-sigv4 "aws:amz:auto:s3" \
  --user "${Storage__R2__AccessKeyId}:${Storage__R2__SecretAccessKey}" \
  -o restore.sql.gz \
  "https://${Storage__R2__AccountId}.r2.cloudflarestorage.com/${Storage__R2__BucketName}/db-backups/<filename>"
```

### Onto a brand-new instance

Run [`deploy/provision-lightsail.sh`](deploy/provision-lightsail.sh) first, then restore as above
before the first deploy.

## After any restore

Boot the API once and watch the log:

```bash
sudo systemctl restart aadhi-api
journalctl -u aadhi-api -n 40 --no-pager
```

`DatabaseInitializer` applies any migrations newer than the backup and names each one it runs.
PostgreSQL is the only supported provider — there is no `DatabaseProvider` switch, just
`ConnectionStrings__DefaultConnection`.

## Verifying a backup is actually restorable

A backup nobody has restored is a guess. Once, on a throwaway database:

```bash
sudo -u postgres createdb restore_test
gunzip -c /var/backups/aadhi/<latest>.sql.gz | sudo -u postgres psql -v ON_ERROR_STOP=1 -d restore_test
sudo -u postgres psql -d restore_test -c 'SELECT count(*) FROM "Orders";'
sudo -u postgres dropdb restore_test
```
