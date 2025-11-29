# Aspendora File Share - Complete Requirements Document

## Project Overview

A secure file sharing application for Aspendora Technologies that allows authenticated users to upload large files (up to 50GB) and share them via temporary links with external recipients.

**Production URL**: https://share.aspendora.com

---

## Branding & Design

### Color Scheme
- **Primary Brand Color**: `#660000` (dark red)
- **Hover State**: Black (`#000000`)
- **Background**: White (`#FFFFFF`) and light gray (`#F9FAFB`)
- **Borders**: 2px solid `#660000` for primary elements

### Logo
- **File**: `aspendora-logo.png`
- **Display Size**: 64px height (h-16) for headers
- **Placement**: All pages (login, dashboard, share page, admin)

### Typography
- **Font**: System font stack (Inter or similar sans-serif)
- **Headings**: Bold, dark gray or brand color
- **Body**: Regular weight, gray-600

### Layout Principles
- Clean, minimal design
- 2px border-[#660000] on all card elements
- Rounded corners (rounded-lg)
- Ample padding and whitespace
- Responsive design (mobile-friendly)

---

## Authentication & Authorization

### Azure AD Multi-Tenant Authentication
- **Provider**: Microsoft Entra ID (Azure AD)
- **Type**: Multi-tenant OAuth2/OpenID Connect
- **Supported Tenants**:
  - `aspendora.com` (db1a2b88-6458-429d-a3a6-7df2d5d701c0)
  - `3endt.com` (72d4846d-9115-4dac-97b8-91a2c003b0ca)
  - `ir100.com` (via 3endt.com tenant)

### Required Permissions
- **User.Read**: Read user profile
- **Directory.Read.All**: Read directory data (for group membership)
- **GroupMember.Read.All**: Read group memberships

### Admin Access
- **Admin Group**: `file-share-app-admin` (Aspendora tenant only)
- **Admin Features**: View audit logs, view all shares, export data
- **Temporary Override**: `lacy@aspendora.com` has automatic admin access

### Session Management
- **Strategy**: Database sessions (not JWT)
- **Duration**: 24 hours
- **Storage**: PostgreSQL with NextAuth adapter

---

## Database Schema

### Users (`User` table)
- id, name, email, emailVerified, image
- **tenantId**: Azure AD tenant ID
- Relations: accounts, sessions, shareLinks, auditLogs

### Share Links (`ShareLink` table)
- **id**: UUID (internal)
- **shortId**: 8-character random alphanumeric (public URL)
- **recipientEmail**: Optional recipient email
- **recipientName**: Optional recipient name
- **message**: Optional message from sender
- **createdAt**: Timestamp
- **expiresAt**: 30 days from creation
- **downloads**: Download counter
- **lastDownloadAt**: Last download timestamp
- **deleted**: Soft delete flag
- Relations: files[], user

### Files (`File` table)
- **s3Key**: file-share/{shareId}/{filename}
- **fileName**: Original filename
- **fileSize**: BigInt (up to 50GB)
- **mimeType**: Content type
- **uploadedAt**: Timestamp
- Relations: shareLink

### Audit Logs (`AuditLog` table)
- **action**: UPLOAD, SHARE, DOWNLOAD, DELETE, VIEW_ADMIN
- **userId, userEmail, userTenant**
- **targetId, targetType**: Reference to share/file
- **metadata**: JSON (additional context)
- **ipAddress, userAgent**: Request details
- **createdAt**: Timestamp

---

## Functionality Requirements

### 1. Login Page (`/login`)

**Features**:
- Microsoft logo button
- Aspendora logo at top
- Brand color accent (#660000)
- Error message display (AccessDenied, general errors)
- List of authorized domains
- Footer with copyright

**Flow**:
1. User clicks "Sign in with Microsoft"
2. Redirects to Azure AD OAuth flow
3. After auth, validates tenant ID
4. Stores user in database with tenant ID
5. Creates session
6. Redirects to dashboard

### 2. Dashboard Page (`/dashboard`)

**Header**:
- Aspendora logo
- "Aspendora File Share" title
- Welcome message with user name/email
- Admin Panel link (for admins only)
- Sign Out button

**Instructions Card**:
- Numbered steps (1-2-3)
- Upload files, add recipient, send link
- Mention 30-day expiration and 50GB limit

**File Uploader Component**:
- Drag-and-drop zone
- Browse button
- Multi-file selection
- File list with remove buttons
- File size display
- Total size display
- Progress bar during upload
- "Upload & Create Share Link" button

**Recent Shares Section**:
- List of user's recent share links
- Display: file count, total size, recipient, dates
- Download count
- "Copy Link" button
- "Delete" button (with confirmation)
- Clicking Delete removes share and deletes files from S3

**Modal After Upload**:
- Shows generated share link
- "Copy Link" button
- Email sending form:
  - Recipient email (required)
  - Recipient name (optional)
  - Custom message (optional)
  - "Send Email" button
- Close button to dismiss

### 3. Public Share Page (`/s/{shortId}`)

**Header**:
- Aspendora logo centered
- "Aspendora File Share" title

**Main Content** (if not expired):
- Sender's name: "{name} shared files with you"
- Optional message in bordered box
- File list with icons, names, sizes
- Total size display
- Large "Download Files" button
- Expiration date/time
- Note about auto-zipping multiple files

**Expired State**:
- Clock icon
- "Link Expired" heading
- Explanation text
- "Contact sender for new link" message

**Footer**:
- Copyright notice

**Download Behavior**:
- Single file: Direct download
- Multiple files: Auto-zip with progress, then download
- Increments download counter
- Logs download event

### 4. Admin Panel (`/admin`)

**Access Control**:
- Only accessible to admin group members
- Shows access denied message for non-admins

**Features**:
- Header with logo and title
- Back to Dashboard link
- Sign Out button

**Audit Log Viewer**:
- Filterable table (by action type)
- Columns: timestamp, action, user, tenant, target, IP, user agent
- Pagination (50 records per page)
- Export to JSON button
- Real-time loading state

**Actions Logged**:
- UPLOAD: User uploads files
- SHARE: User sends share email
- DOWNLOAD: Recipient downloads files
- DELETE: User deletes share
- VIEW_ADMIN: Admin views audit logs

---

## File Upload System

### Constraints
- **Max File Size**: 50GB per file
- **Multiple Files**: Yes (no total limit)
- **Chunk Size**: 50MB per chunk
- **Upload Method**: Multipart upload via server proxy

### Upload Flow

**Step 1: Initiate**
- POST `/api/upload/initiate`
- Request: Array of file metadata (name, size, type)
- Response: shareId, shareLinkId, uploadSessions[]
- Creates ShareLink record in database
- Creates multipart upload sessions in S3

**Step 2: Upload Chunks**
- POST `/api/upload/chunk` (per chunk)
- Request: FormData with chunk, key, uploadId, partNumber
- Server receives chunk and uploads to S3
- Returns ETag for part
- Client updates progress bar

**Step 3: Complete**
- POST `/api/upload/complete`
- Request: shareLinkId, uploads[] with all ETags
- Server completes multipart uploads
- Creates File records in database
- Returns success

### Storage Backend

**Service**: Backblaze B2 (S3-compatible)
- **Endpoint**: https://s3.us-west-004.backblazeb2.com
- **Bucket**: aspendora-file-share
- **Region**: us-west-004
- **Access Key**: ${B2_ACCESS_KEY}
- **Secret Key**: ${B2_SECRET_KEY}

**Key Structure**: `file-share/{shareId}/{filename}`

**Permissions**: Bucket-level public read (all files publicly accessible)

---

## Email System

### Service
- **Provider**: SMTP2GO REST API
- **API Key**: ${SMTP2GO_API_KEY}
- **Endpoint**: https://api.smtp2go.com/v3/email/send

### From Address Logic
```
if user.email ends with aspendora.com, 3endt.com, or ir100.com:
  from = user.email (send from user)
else:
  from = noreply@aspendora.com (guest accounts)
```

### Email Template

**Subject**: `{sender} shared files with you via Aspendora`

**Body** (HTML):
- Aspendora logo embedded (base64 as inline attachment)
- Greeting: "Hi {recipientName},"
- Message: "{sender} has shared {count} file(s) with you"
- Optional custom message in bordered box
- File list with names and sizes
- Prominent "Download Files" button (links to `/s/{shortId}`)
- Expiration notice (30 days)
- Footer with copyright

**Attachments**:
- Logo as inline attachment with Content-ID: `<aspendora-logo>`

---

## Download System

### Single File Download
- Direct streaming from S3
- Sets Content-Disposition header with filename
- Logs download event

### Multiple Files Download
- Server creates zip file in-memory
- Streams zip to client
- Filename: `files_{shareId}.zip`
- Logs download event

### Tracking
- Increments `downloads` counter
- Updates `lastDownloadAt` timestamp
- Creates DOWNLOAD audit log

---

## Automated Cleanup

### Cron Job: `/api/cron/cleanup`
- **Schedule**: Daily
- **Function**: Delete expired shares
- **Process**:
  1. Find ShareLinks where expiresAt < now AND deleted = false
  2. For each: delete files from S3
  3. Mark as deleted in database
  4. Log action

### Cron Job: `/api/cron/weekly-report`
- **Schedule**: Weekly (Mondays 9 AM)
- **Function**: Send activity report
- **Recipient**: lacy@aspendora.com
- **Content**:
  - Total shares created
  - Total files uploaded
  - Total downloads
  - Storage usage
  - Top users

### Security
- Requires `CRON_SECRET` header for authentication

---

## Environment Variables

### Required
```
# Database
DATABASE_URL=postgresql://...

# NextAuth
NEXTAUTH_URL=https://share.aspendora.com
NEXTAUTH_SECRET=<random-secret>

# Azure AD
AZURE_AD_CLIENT_ID=e407b8b3-ab87-4240-9723-31fa3c767453
AZURE_AD_CLIENT_SECRET=${AZURE_AD_CLIENT_SECRET}
AZURE_AD_TENANT_ID=common

# Allowed Tenants (comma-separated)
ALLOWED_TENANT_IDS=db1a2b88-6458-429d-a3a6-7df2d5d701c0,72d4846d-9115-4dac-97b8-91a2c003b0ca

# Aspendora Tenant (for admin check)
ASPENDORA_TENANT_ID=db1a2b88-6458-429d-a3a6-7df2d5d701c0

# S3 Storage (Backblaze B2)
S3_ENDPOINT=https://s3.us-west-004.backblazeb2.com
S3_BUCKET=aspendora-file-share
S3_ACCESS_KEY=${B2_ACCESS_KEY}
S3_SECRET_KEY=${B2_SECRET_KEY}
S3_REGION=us-west-004

# Email (SMTP2GO API)
SMTP2GO_API_KEY=${SMTP2GO_API_KEY}

# Admin Group
ADMIN_GROUP_NAME=file-share-app-admin

# Cron Security
CRON_SECRET=<random-secret>
```

---

## Deployment

### Docker Configuration
- **Base Image**: node:20-alpine
- **Build**: Multi-stage (deps, builder, runner)
- **Port**: 3000
- **Services**:
  - app (Next.js)
  - postgres (PostgreSQL 16)
  - redis (Redis 7)

### nginx-proxy Integration
- **Virtual Host**: share.aspendora.com
- **SSL**: Let's Encrypt
- **Network**: proxy-network

### Health Check
- **Endpoint**: /api/health
- **Response**: `{"status":"healthy","timestamp":"...","database":"connected"}`

---

## User Flows

### Flow 1: User Shares Files

1. User logs in with Microsoft account
2. Lands on dashboard
3. Drags files into uploader OR clicks browse
4. Files validate (under 50GB each)
5. Clicks "Upload & Create Share Link"
6. Progress bar shows upload status (50MB chunks)
7. Upload completes, modal appears
8. User enters recipient email, name, message
9. Clicks "Send Email"
10. Email sent via SMTP2GO API
11. Modal closes, share appears in Recent Shares

### Flow 2: Recipient Downloads Files

1. Recipient receives email
2. Clicks "Download Files" button
3. Lands on `/s/{shortId}` page
4. Sees file list and sender message
5. Clicks "Download Files" button
6. If single file: Direct download
7. If multiple: Server creates zip, streams to browser
8. Download counter increments

### Flow 3: Admin Reviews Activity

1. Admin logs in
2. Clicks "Admin Panel" link
3. Sees audit log table
4. Filters by action type (e.g., DOWNLOAD)
5. Reviews logs (who downloaded what, when)
6. Exports to JSON for analysis

---

## Security Considerations

1. **Authentication**: All uploads/shares require Azure AD login
2. **Authorization**: Tenant validation prevents unauthorized access
3. **Public Access**: Share links are public (8-char random ID)
4. **Expiration**: All shares expire after 30 days
5. **Soft Delete**: Deleted shares remain in database for audit trail
6. **Audit Logging**: All actions logged with IP, user agent, timestamp
7. **HTTPS Only**: Enforce TLS for all connections
8. **CORS**: No CORS (same-origin only for authenticated endpoints)
9. **File Scanning**: None (trust users to upload safe files)
10. **Rate Limiting**: None currently (could add if needed)

---

## Known Issues with Current Next.js Implementation

1. **Prisma Schema Sync**: Prisma generate must run in container
2. **NextAuth Middleware**: Redirect issues with health endpoint
3. **Environment Variables**: Require full rebuild to update (restart doesn't work)
4. **Azure AD Config**: Lost multiple times, required recreation
5. **Build Complexity**: Multi-stage Docker builds are slow
6. **TypeScript Overhead**: Type errors block deployments
7. **Turbopack Warnings**: Middleware deprecated warnings

---

## Success Criteria for C#/Blazor Rewrite

✅ **Same Functionality**: All features above implemented
✅ **Better Azure AD Integration**: Native MSAL library
✅ **Simpler Deployment**: Single executable or simpler Docker
✅ **Faster Rebuild**: Configuration changes don't require full rebuild
✅ **Better Logging**: Built-in ILogger
✅ **Type Safety**: C# static typing without TypeScript quirks
✅ **Entity Framework**: Native database migrations
✅ **Blazor Server**: Real-time updates without complex state management

---

**Document Version**: 1.0
**Date**: 2025-11-29
**Author**: Claude Code (from analysis of existing Next.js app)
