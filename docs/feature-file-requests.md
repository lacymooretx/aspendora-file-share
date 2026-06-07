# Feature: Request Files from Others

**Added:** 2026-06-07
**Status:** Implemented, builds clean, migration generated. **Not yet deployed.**

## What it does

Lets an authenticated user create a **file request** — a link they send to someone
else (who needs no account) so that person can upload files *to* the requester. It is
the inverse of the existing "share files" flow.

- Requester clicks **New File Request** on the dashboard, gives it a title (e.g.
  "Please upload your signed tax docs"), optional instructions, and optionally one or
  more recipient emails.
- They get a link: `https://share.aspendora.com/r/{shortId}`.
- The recipient opens the link (no login), optionally enters their name/email, drags
  files in, and clicks **Send Files**. Uploads use the same chunked, direct-to-S3
  pipeline as normal shares (up to 50 GB/file).
- The requester is emailed when files arrive and sees received files on their
  dashboard under **Request Files from Others**, where they can download, close,
  reopen, or delete each request.

## How it works (architecture)

Each upload submission to a request is stored as a **`ShareLink` owned by the
requester** with `FileRequestId` set. This reuses the existing storage, presigned-URL
upload, zip-download (`/api/download/{shortId}`), and cleanup machinery unchanged.

- `FileRequest` (new entity): the request itself — `ShortId`, `UserId` (owner),
  `Title`, `Message`, `ExpiresAt`, `Closed`, soft-delete flags.
- `ShareLink` (extended): added `FileRequestId` (nullable FK), `SubmitterName`,
  `SubmitterEmail`. A `ShareLink` with `FileRequestId == null` is an outgoing share
  (unchanged behaviour); non-null means it's a submission to a request and is filtered
  out of "Recent Shares".

### New files
- `Data/Models/FileRequest.cs`
- `Controllers/FileRequestController.cs`
- `wwwroot/js/filerequest.js`
- `Components/FileRequestUpload.razor`
- `Components/RequestModal.razor`
- `Components/Pages/FileRequest.razor` (public page `/r/{shortId}` and `/request/{shortId}`)
- `Data/Migrations/*_AddFileRequests.cs`

### Modified files
- `Data/Models/ShareLink.cs`, `Data/Models/User.cs`, `Data/ApplicationDbContext.cs`
- `Services/EmailService.cs` (two new templates)
- `Components/Pages/Dashboard.razor`, `Components/App.razor` (loads `filerequest.js`)

### API endpoints (`/api/filerequest`)
| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/create` | required | Create a request |
| POST | `/email` | required | Email an invite for a request |
| GET | `/list` | required | List my requests |
| GET | `/{shortId}/submissions` | required | List received files for a request |
| POST | `/close` | required | Toggle closed |
| POST | `/delete` | required | Soft-delete request + purge received files from S3 |
| GET | `/public/{shortId}` | anonymous | Request details for the upload page |
| POST | `/{shortId}/initiate` | anonymous | Begin a submission (gated on open request) |
| POST | `/chunk` | anonymous | Server-proxy chunk fallback |
| POST | `/{shortId}/complete` | anonymous | Finalize submission + notify requester |

Anonymous upload is gated: the request must exist and be non-deleted, non-closed, and
non-expired. The owner-only endpoints verify `FileRequest.UserId == caller`.

## How to test

1. `dotnet build` — succeeds (0 errors).
2. Run locally (`dotnet run`), sign in, click **New File Request**, create one.
3. Open the `/r/{shortId}` link in a private window (logged out), upload a file.
4. Confirm: requester gets a "You received files" email; the request shows the
   submission under **View Files**; **Download** returns the file(s).
5. Close the request → the upload page shows "Request Closed". Delete → received
   files are removed from S3 and the request disappears.

## Database migration

Migration `AddFileRequests` adds the `FileRequests` table and the three `ShareLinks`
columns. It is applied automatically on container start by the existing `efbundle`
step in the Dockerfile — no manual DB step needed for deploy.

## Rollback

- App: revert the commit; rebuild.
- DB: the migration's `Down()` drops the new table/columns. Existing shares are
  untouched (new columns are nullable; outgoing shares have `FileRequestId == null`).

## Secrets

No new secrets. Reuses existing SMTP2GO, Backblaze B2/S3, and Azure AD configuration.

## Deployment note

The deployment now lives in the infra repo at
`~/code/vultr-proxmox/services/file-share-blazor` (VM 301, container
`file-share-blazor`, `infra-net`, host port 3001 → 8080), which builds from this
repo's `AspendoraFileShare/`. Deploy by rebuilding that compose service.
