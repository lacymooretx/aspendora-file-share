# Required Secrets

This app reads its secrets at runtime from environment variables. **No real values live in this repo.** Values come from `~/.secrets/.env` on a developer machine, or from `/opt/services/file-share-blazor/.env` on the production VM (10.10.30.101).

The container reads them via `docker-compose.yml`, which maps `${FILESHARE_*}` env names into the `.NET` configuration keys (`AzureAd__*`, `S3__*`, `Smtp2Go__*`, `ConnectionStrings__*`).

## Required values

| Env var | Purpose | Owner / where to obtain | Rotation |
|---|---|---|---|
| `FILESHARE_DB_PASSWORD` | Postgres password for the `fileshare_user` role used by the app container and the `postgres` sidecar. | Generated locally; stored in `/opt/services/file-share-blazor/.env`. | Generate new value, update `.env`, `docker compose up -d`, run `ALTER ROLE` on the running DB if rotating without a fresh volume. |
| `FILESHARE_AZURE_AD_CLIENT_ID` | App ID of the "Aspendora File Share" Entra ID app registration. Not a secret, but required. | Entra ID portal → App registrations → "Aspendora File Share". Currently `e407b8b3-ab87-4240-9723-31fa3c767453`. | Only changes if the app is re-registered. |
| `FILESHARE_AZURE_AD_CLIENT_SECRET` | Client secret for the "Aspendora File Share" Entra ID app. Used for OIDC sign-in. | Entra ID portal → that app → Certificates & secrets → New client secret. Or `az ad app credential reset --id <appId> --append`. | Rotate by adding a new secret, deploying it, then deleting the old one. The app registration is dedicated to this app, so rotation only impacts file-share. |
| `FILESHARE_S3_ACCESS_KEY` | Backblaze B2 application key id, scoped to the `aspendora-file-share` bucket only. | Backblaze console → Application Keys, or via the B2 API (`b2_create_key` with `bucketId` set). Current key name: `aspendora-file-share-app`. | Rotate by creating a new scoped key, deploying, then deleting the old one. **Do not use the master account key here.** |
| `FILESHARE_S3_SECRET_KEY` | Secret half of the above scoped B2 key. | Returned once when the key is created — must be captured at creation time. | See above. |
| `FILESHARE_SMTP2GO_API_KEY` | Dedicated SMTP2GO API key for the file-share app. Sends share-link and weekly report emails from `notifications@aspendora.com`. | SMTP2GO dashboard → Settings → API Keys → create one labeled for file-share. | Rotate via dashboard. Update `.env` and restart container. |

## What is NOT a secret

- `S3.ServiceUrl` (`https://s3.us-west-004.backblazeb2.com`)
- `S3.BucketName` (`aspendora-file-share`)
- `S3.Region` (`us-west-004`)
- `Smtp2Go.ApiUrl`, `Smtp2Go.FromEmail`, `Smtp2Go.FromName`
- `AllowedTenants`, `AspendoraTenantId`, `AdminGroupName`

These are public configuration and can stay in `appsettings.json`.

## If you discover a secret in the repo

1. **Stop.** Do not commit further.
2. Remove the value from the file and replace with a `${VAR_NAME}` placeholder.
3. Add an entry above documenting the env var.
4. Rotate the underlying credential (delete the old one in the issuing system, create a new one).
5. Force-push the redacted history (`git-filter-repo --replace-text`) — see `docs/claude-runlog.md` 2026-05-10 entry for the exact procedure.
