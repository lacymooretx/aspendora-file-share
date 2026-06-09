# Aspendora File Share — Blazor / .NET 9

A secure file-sharing application for Aspendora. Users authenticate with Azure AD,
upload files to Backblaze B2 (S3-compatible) storage, and share them via expiring
links or email. Also supports **file requests** — inviting external people to upload
files back to you without an account.

This is a C#/Blazor rewrite of an earlier Next.js app, built to eliminate Prisma,
Next.js middleware, and deployment complexity.

## 🎯 Status: Complete & Deployed in Production

- **Live URL**: https://share.aspendora.com
- **Hosting**: Docker container `file-share-blazor` on the production VM,
  fronted by `nginx-proxy` (SSL via existing certs).
- **Last deploy**: 2026-06-07 — added the File Requests feature.

> Deployment host/path/SSH details are intentionally kept out of this public repo.
> They live in the internal ops notes, not here.

> The frontend, background jobs, and deployment are all done. Earlier versions of
> this README described a half-finished frontend; that is no longer accurate.

## ✨ Features

- **Azure AD login** (multi-tenant) via Microsoft Identity + Graph.
- **Chunked / multipart uploads** to Backblaze B2 with drag-drop + progress.
- **Share links** — single file or auto-zipped multi-file downloads, with expiry.
- **Email delivery** of share links via SMTP2GO (with embedded logo).
- **File Requests** — generate a public `/r/{id}` link inviting others to upload
  files to you. No account required for the submitter. Includes an invite email
  and "Send Invite" flow.
- **Admin panel** (for `lacy@aspendora.com`) — audit logs, all shares, export to JSON,
  authorized-domains management.
- **Background jobs** — automatic cleanup of expired shares + weekly activity reports.

## 🏗️ Architecture

```
AspendoraFileShare/
├── Components/
│   ├── Pages/            Dashboard, Login, Share, Admin, FileRequest, Error
│   ├── FileUpload.razor / FileRequestUpload.razor
│   ├── ShareModal.razor  / RequestModal.razor
│   └── App / Routes / _Imports
├── Controllers/          Account, Upload, Download, Share, Admin, FileRequest
├── Services/             Auth, S3, Email, Cleanup, Report
├── Data/
│   ├── Models/           User, ShareLink, FileModel, FileRequest, AuditLog
│   ├── ApplicationDbContext.cs
│   └── Migrations/       InitialCreate → MakeAuditLogUserOptional → AddFileRequests
└── wwwroot/              app.css, js/ (filerequest.js, interop)
```

- **Framework**: .NET 9 / Blazor Server (SignalR for interactivity)
- **Database**: PostgreSQL via Entity Framework Core
- **Storage**: Backblaze B2 (S3 API, `us-west-004`)
- **Email**: SMTP2GO v3 API

## 🔑 Configuration & Secrets

Secret **values** are never stored in this repo. See **[docs/secrets-required.md](docs/secrets-required.md)**
for the full list of required secrets, their purpose, and where to source them.

Required secrets (values live in `~/.secrets/.env` locally, or the server `.env`):

| Secret | Purpose |
|---|---|
| `AZURE_AD_CLIENT_SECRET` | Azure AD app authentication |
| `B2_ACCESS_KEY` / `B2_SECRET_KEY` | Backblaze B2 storage |
| `SMTP2GO_API_KEY` | Outbound email |
| `POSTGRES_PASSWORD` | Database |

Non-secret config (Azure App ID, B2 endpoint/bucket, SMTP URL) lives in
`appsettings.json`. Copy `.env.example` → `.env` and populate from `~/.secrets/.env`.

## 🛠️ Local Development

```bash
cd AspendoraFileShare

# one-time: EF tools
dotnet tool install --global dotnet-ef --version 9.*
export PATH="$PATH:$HOME/.dotnet/tools"

# apply migrations to your local Postgres
dotnet ef database update

dotnet run
# https://localhost:5001
```

## 🐳 Deployment

The app ships as a Docker image (`AspendoraFileShare/Dockerfile`) orchestrated by
`docker-compose.yml` (app + PostgreSQL).

```bash
# on the production VM, in the deploy directory
# safety: back up the DB before any deploy
docker compose exec -T postgres pg_dump -U fileshare fileshare > fileshare-backup-$(date +%Y%m%d-%H%M%S).sql

docker compose up -d --build
docker compose logs -f app
```

nginx-proxy must be connected to the app's docker network and configured with
WebSocket support (Blazor SignalR). The exact host, deploy path, SSH access, and
nginx config live in the internal ops notes (kept out of this public repo).

## 📚 Documentation

- **[REQUIREMENTS.md](REQUIREMENTS.md)** — functional requirements
- **[COMPLETION-STATUS.md](COMPLETION-STATUS.md)** — detailed feature/deploy status
- **[docs/feature-file-requests.md](docs/feature-file-requests.md)** — File Requests design
- **[docs/secrets-required.md](docs/secrets-required.md)** — required secrets
- **[docs/claude-runlog.md](docs/claude-runlog.md)** — full execution / deploy log

---

**Version**: 1.0.0 — Complete, deployed, with File Requests
**Last Updated**: 2026-06-09
