# Database backup & restore

Nightly backups of the Neon PostgreSQL database are taken by the GitHub Actions workflow
[`.github/workflows/db-backup.yml`](.github/workflows/db-backup.yml). It runs every day at
**21:00 UTC** (and on demand via *Run workflow*), dumps the database with `pg_dump`, gzips it to
`backup-YYYY-MM-DD.sql.gz`, and uploads it as a workflow artifact kept for **30 days**.

## 1. Add the GitHub secret (one-time setup)

The workflow reads the connection string from the repository secret `NEON_DATABASE_URL` and fails
with a clear error until it is set.

1. On GitHub, open the repository → **Settings** → **Secrets and variables** → **Actions**.
2. Click **New repository secret**.
3. Name: `NEON_DATABASE_URL`
4. Value: the full Neon connection string (Neon console → your project → **Connect**), e.g.

   ```
   postgresql://USER:PASSWORD@YOUR-ENDPOINT-pooler.REGION.aws.neon.tech/aadhicrackers?sslmode=require&channel_binding=require
   ```

5. Save. Trigger a manual run (**Actions** → *Nightly database backup* → **Run workflow**) to
   verify a backup artifact appears.

## 2. Download a backup artifact

- **Web UI**: **Actions** → *Nightly database backup* → pick a run → **Artifacts** →
  download `db-backup-<run id>` (a zip containing `backup-YYYY-MM-DD.sql.gz`). Unzip it.
- **CLI**:

  ```bash
  gh run list --workflow db-backup.yml            # find the run id
  gh run download <RUN_ID>                        # downloads backup-YYYY-MM-DD.sql.gz
  ```

## 3. Restore

Restore into an **empty** database (create a fresh one in the Neon console, or drop/recreate the
schema first) with a single command:

```bash
gunzip -c backup-YYYY-MM-DD.sql.gz | psql "postgresql://USER:PASSWORD@HOST/DBNAME?sslmode=require&channel_binding=require"
```

Notes:

- `psql` comes from the PostgreSQL client tools (`sudo apt-get install postgresql-client` on
  Debian/Ubuntu, `winget install PostgreSQL.PostgreSQL` on Windows — or use the Neon SQL editor
  for small dumps).
- The dump is taken with `--no-owner --no-privileges`, so it restores cleanly under the Neon role
  in the connection string.
- To restore into the live database in place (data loss — you are replacing everything), recreate
  the `public` schema first:

  ```bash
  psql "$NEON_DATABASE_URL" -c 'DROP SCHEMA public CASCADE; CREATE SCHEMA public;'
  gunzip -c backup-YYYY-MM-DD.sql.gz | psql "$NEON_DATABASE_URL"
  ```

- After a restore, boot the API once with `DatabaseProvider=PostgreSql` pointing at the restored
  database; `DatabaseInitializer` runs `MigrateAsync` and applies any migrations newer than the
  backup.
