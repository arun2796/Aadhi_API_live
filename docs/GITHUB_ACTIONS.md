# GitHub Actions — `Aadhi_API_live`

This repository has **two** workflows. They deliberately do not overlap.

| Workflow | Runs on | Does |
|---|---|---|
| [`ci.yml`](../.github/workflows/ci.yml) | every branch **except** `ak-dev`, and every PR into `ak-dev` or `main` | restore → build → test → check the EF model matches the migrations |
| [`deploy-api.yml`](../.github/workflows/deploy-api.yml) | push to `ak-dev`, or **Run workflow** | preflight → build + test → publish → deploy to Lightsail |

`ak-dev` is covered by the deploy workflow, which runs the same tests — so CI skips it rather
than running everything twice.

> **`ak-dev` is the branch that ships.** Every push to it that passes the tests goes to
> production automatically. Do experimental work on another branch — CI checks it there, and
> nothing deploys until it lands on `ak-dev`.

---

## Secrets

**Settings → Secrets and variables → Actions → Secrets tab → New repository secret.**

Secrets are write-only: GitHub will never show them to you again, and Actions masks them in logs.

| Name | Required | Value | Where to get it |
|---|:--:|---|---|
| `LIGHTSAIL_HOST` | yes | Your instance's **static IP**, or `api.aadhicracker.in` once DNS resolves | Lightsail console → Networking |
| `LIGHTSAIL_USER` | yes | `ubuntu` | The default user on the Ubuntu blueprint |
| `LIGHTSAIL_SSH_KEY` | yes | The **private** key, complete with its `BEGIN`/`END` lines | Generated below |

### Creating the deploy key

Use a dedicated key, not your personal one or the Lightsail default — this one lives in GitHub
and should be revocable on its own.

```bash
ssh-keygen -t ed25519 -f deploy_key -C "github-actions-aadhi-api" -N ""
ssh-copy-id -i deploy_key.pub ubuntu@<static-ip>

# Confirm it works before pasting it anywhere
ssh -i deploy_key ubuntu@<static-ip> 'echo ok'
```

Paste the contents of **`deploy_key`** (no `.pub`) into `LIGHTSAIL_SSH_KEY` — the whole file,
including `-----BEGIN OPENSSH PRIVATE KEY-----` and `-----END OPENSSH PRIVATE KEY-----`. Then
delete the local copies:

```bash
rm deploy_key deploy_key.pub
```

The preflight job checks the shape of this value and tells you specifically if you have pasted a
`.pub` file or a PuTTY `.ppk` — both of which otherwise fail with an unhelpful
`libcrypto: error` deep inside ssh.

To revoke it later, delete the matching line from `~ubuntu/.ssh/authorized_keys` on the instance.

## Variables

**Settings → Secrets and variables → Actions → Variables tab.**

Variables are plain text and *are* visible in logs — never put a credential here.

| Name | Required | Value | Effect if unset |
|---|:--:|---|---|
| `API_PUBLIC_URL` | no | `https://api.aadhicracker.in` — no trailing slash, no `/api/v1` | The post-deploy public smoke test is skipped and the job prints a warning. The deploy still verifies health on the server itself. |

Set it. The on-server health check proves the process is up; only this one proves the API is
reachable through DNS, the Lightsail firewall, nginx and TLS — which is what a customer needs.

---

## What is NOT a GitHub secret

Everything the running API needs — the database password, the JWT signing key, the R2
credentials, the allowed CORS origins — lives in **`/etc/aadhi-api/aadhi-api.env`** on the
instance, owned by root and mode `0600`.

That file is not part of a release, so deploys never touch or overwrite it. Changing a value
there needs no deploy at all:

```bash
sudo nano /etc/aadhi-api/aadhi-api.env
sudo systemctl restart aadhi-api
```

Keeping runtime configuration off GitHub means a compromised GitHub account cannot read your
database password, and rotating a credential does not require a code change.
See [`deploy/aadhi-api.env.example`](../deploy/aadhi-api.env.example).

---

## How the deploy works

```
preflight   secrets present, SSH key well-formed          ~5s, fails fast
    ↓
build       restore → build → TEST → publish self-contained → upload artifact
    ↓
deploy      verify SSH + write access
            rsync → /var/www/aadhi-api/releases/<sha>
            deploy-release.sh: flip `current` → restart → poll /health/ready
            public smoke test
```

**The preflight job exists so a missing secret costs you five seconds, not five minutes** — it
used to be checked after the build had already run.

**Tests are the only gate between a push and production.** A failing test stops the deploy.

**Rollback is automatic.** If the new release does not answer `/health/ready` within 90 seconds,
`deploy-release.sh` puts the `current` symlink back, restarts the previous release, verifies
*that* is healthy, and fails the job. The last 5 releases stay on disk.

Manual rollback:

```bash
ls -1t /var/www/aadhi-api/releases     # newest first
bash /tmp/deploy-release.sh <older-sha>
```

## Troubleshooting

| Job fails at | Meaning |
|---|---|
| preflight → *Missing repository secret* | Add it in the Secrets tab. The message names it exactly. |
| preflight → *does not look like an OpenSSH private key* | You pasted the `.pub` file, or a PuTTY `.ppk`. See above. |
| deploy → *ssh-keyscan got no host key* | `LIGHTSAIL_HOST` is wrong or unreachable; port 22 closed in the Lightsail firewall; instance stopped. |
| deploy → *Cannot SSH* | The public half of the key is not in `~ubuntu/.ssh/authorized_keys`. |
| deploy → *cannot write to …/releases* | Run `provision-lightsail.sh`, then log out and back in once so the `aadhi` group membership takes effect. |
| deploy → *did not become healthy* | The release started and failed. It has already been rolled back. `journalctl -u aadhi-api -n 80` names the missing setting — the API refuses to start rather than run misconfigured. |
| deploy → *not reachable publicly* | It is healthy on the server; the problem is nginx, TLS, DNS or the Lightsail firewall. |
| ci → *model has changed but no migration was added* | Add the migration the error message spells out. |
