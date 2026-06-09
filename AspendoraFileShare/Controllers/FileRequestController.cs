using AspendoraFileShare.Data;
using AspendoraFileShare.Data.Models;
using AspendoraFileShare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Amazon.S3.Model;

namespace AspendoraFileShare.Controllers;

/// <summary>
/// File requests: an authenticated user asks others to upload files to them.
/// Owner operations require auth; the public upload flow is anonymous but gated on
/// a valid (non-deleted, non-closed, non-expired) request short id.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class FileRequestController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly S3Service _s3Service;
    private readonly AuthService _authService;
    private readonly EmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FileRequestController> _logger;

    private const int CHUNK_SIZE = 50 * 1024 * 1024; // 50MB - must match upload.js / filerequest.js

    public FileRequestController(
        ApplicationDbContext context,
        S3Service s3Service,
        AuthService authService,
        EmailService emailService,
        IConfiguration configuration,
        ILogger<FileRequestController> logger)
    {
        _context = context;
        _s3Service = s3Service;
        _authService = authService;
        _emailService = emailService;
        _configuration = configuration;
        _logger = logger;
    }

    // ---------------------------------------------------------------------
    // Owner operations (authenticated)
    // ---------------------------------------------------------------------

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(new { Error = "Title is required" });
            }

            var user = await _authService.GetOrCreateUserAsync(User);

            var shortId = _s3Service.GenerateShortId();
            while (await _context.FileRequests.AnyAsync(r => r.ShortId == shortId)
                   || await _context.ShareLinks.AnyAsync(s => s.ShortId == shortId))
            {
                shortId = _s3Service.GenerateShortId();
            }

            var expirationDays = request.ExpirationDays
                ?? _configuration.GetValue<int>("FileShare:ExpirationDays", 30);

            var fileRequest = new FileRequest
            {
                ShortId = shortId,
                UserId = user.Id,
                Title = request.Title.Trim(),
                Message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim(),
                RecipientEmail = request.RecipientEmail,
                RecipientName = request.RecipientName,
                ExpiresAt = DateTime.UtcNow.AddDays(expirationDays)
            };

            _context.FileRequests.Add(fileRequest);
            await _context.SaveChangesAsync();

            await _authService.LogAuditAsync("REQUEST_CREATE", user, fileRequest.Id, "fileRequest",
                new { fileRequest.Title },
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString());

            return Ok(new { Success = true, ShortId = fileRequest.ShortId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating file request");
            return StatusCode(500, new { Error = "Failed to create file request" });
        }
    }

    [HttpPost("email")]
    public async Task<IActionResult> SendInvite([FromBody] SendInviteRequest request)
    {
        try
        {
            var user = await _authService.GetOrCreateUserAsync(User);

            var fileRequest = await _context.FileRequests
                .FirstOrDefaultAsync(r => r.ShortId == request.RequestShortId);

            if (fileRequest == null)
            {
                return NotFound(new { Error = "File request not found" });
            }
            if (fileRequest.UserId != user.Id)
            {
                return Forbid();
            }

            // Persist the most recent recipient for reference
            fileRequest.RecipientEmail = request.RecipientEmail;
            fileRequest.RecipientName = request.RecipientName;
            await _context.SaveChangesAsync();

            var senderEmail = user.Email;
            var domain = senderEmail.Split('@')[1].ToLower();
            var allowedDomains = new[] { "aspendora.com", "3endt.com", "ir100.com" };
            if (!allowedDomains.Contains(domain))
            {
                senderEmail = "noreply@aspendora.com";
            }

            var requestUrl = $"{Request.Scheme}://{Request.Host}/r/{fileRequest.ShortId}";

            await _emailService.SendFileRequestEmailAsync(
                request.RecipientEmail,
                request.RecipientName ?? request.RecipientEmail,
                user.Name ?? user.Email,
                senderEmail,
                requestUrl,
                fileRequest.Title,
                string.IsNullOrWhiteSpace(request.Message) ? fileRequest.Message : request.Message,
                fileRequest.ExpiresAt);

            await _authService.LogAuditAsync("REQUEST_INVITE", user, fileRequest.Id, "fileRequest",
                new { RecipientEmail = request.RecipientEmail },
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString());

            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending file request invite");
            return StatusCode(500, new { Error = "Failed to send invite" });
        }
    }

    [HttpGet("list")]
    public async Task<IActionResult> ListRequests()
    {
        try
        {
            var user = await _authService.GetOrCreateUserAsync(User);

            var requests = await _context.FileRequests
                .Where(r => r.UserId == user.Id && !r.Deleted)
                .OrderByDescending(r => r.CreatedAt)
                .Take(50)
                .Select(r => new
                {
                    r.Id,
                    r.ShortId,
                    r.Title,
                    r.Message,
                    r.RecipientEmail,
                    r.RecipientName,
                    r.CreatedAt,
                    r.ExpiresAt,
                    r.Closed,
                    IsExpired = r.ExpiresAt < DateTime.UtcNow,
                    SubmissionCount = r.Submissions.Count(s => !s.Deleted),
                    FileCount = r.Submissions.Where(s => !s.Deleted).SelectMany(s => s.Files).Count()
                })
                .ToListAsync();

            return Ok(new { Requests = requests });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing file requests");
            return StatusCode(500, new { Error = "Failed to list requests" });
        }
    }

    /// <summary>Submissions (received files) for one request the caller owns.</summary>
    [HttpGet("{shortId}/submissions")]
    public async Task<IActionResult> ListSubmissions(string shortId)
    {
        try
        {
            var user = await _authService.GetOrCreateUserAsync(User);

            var fileRequest = await _context.FileRequests
                .Include(r => r.Submissions.Where(s => !s.Deleted))
                    .ThenInclude(s => s.Files)
                .FirstOrDefaultAsync(r => r.ShortId == shortId);

            if (fileRequest == null)
            {
                return NotFound(new { Error = "File request not found" });
            }
            if (fileRequest.UserId != user.Id)
            {
                return Forbid();
            }

            var submissions = fileRequest.Submissions
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new
                {
                    s.Id,
                    s.ShortId,
                    s.SubmitterName,
                    s.SubmitterEmail,
                    s.CreatedAt,
                    FileCount = s.Files.Count,
                    TotalSize = s.Files.Sum(f => f.FileSize),
                    Files = s.Files.Select(f => new { f.Id, f.FileName, f.FileSize, f.MimeType }).ToList()
                })
                .ToList();

            return Ok(new
            {
                fileRequest.Title,
                fileRequest.ShortId,
                Submissions = submissions
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing submissions");
            return StatusCode(500, new { Error = "Failed to list submissions" });
        }
    }

    [HttpPost("close")]
    public async Task<IActionResult> Close([FromBody] RequestActionRequest request)
    {
        try
        {
            var user = await _authService.GetOrCreateUserAsync(User);

            var fileRequest = await _context.FileRequests
                .FirstOrDefaultAsync(r => r.Id == request.RequestId);

            if (fileRequest == null)
            {
                return NotFound(new { Error = "File request not found" });
            }
            if (fileRequest.UserId != user.Id)
            {
                return Forbid();
            }

            fileRequest.Closed = !fileRequest.Closed;
            await _context.SaveChangesAsync();

            return Ok(new { Success = true, fileRequest.Closed });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing file request");
            return StatusCode(500, new { Error = "Failed to update request" });
        }
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] RequestActionRequest request)
    {
        try
        {
            var user = await _authService.GetOrCreateUserAsync(User);

            var fileRequest = await _context.FileRequests
                .Include(r => r.Submissions)
                .FirstOrDefaultAsync(r => r.Id == request.RequestId);

            if (fileRequest == null)
            {
                return NotFound(new { Error = "File request not found" });
            }
            if (fileRequest.UserId != user.Id)
            {
                return Forbid();
            }

            // Delete received files from S3 and soft-delete each submission.
            foreach (var submission in fileRequest.Submissions.Where(s => !s.Deleted))
            {
                await _s3Service.DeleteShareFilesAsync(submission.Id);
                submission.Deleted = true;
                submission.DeletedAt = DateTime.UtcNow;
            }

            fileRequest.Deleted = true;
            fileRequest.DeletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _authService.LogAuditAsync("REQUEST_DELETE", user, fileRequest.Id, "fileRequest",
                null,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString());

            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file request");
            return StatusCode(500, new { Error = "Failed to delete request" });
        }
    }

    // ---------------------------------------------------------------------
    // Public upload flow (anonymous, gated on a valid request short id)
    // ---------------------------------------------------------------------

    [AllowAnonymous]
    [HttpGet("public/{shortId}")]
    public async Task<IActionResult> GetPublic(string shortId)
    {
        try
        {
            var fileRequest = await _context.FileRequests
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.ShortId == shortId);

            if (fileRequest == null || fileRequest.Deleted)
            {
                return NotFound(new { Error = "File request not found" });
            }

            return Ok(new
            {
                fileRequest.ShortId,
                fileRequest.Title,
                fileRequest.Message,
                RequesterName = fileRequest.User?.Name ?? fileRequest.User?.Email ?? "Someone",
                fileRequest.CreatedAt,
                fileRequest.ExpiresAt,
                fileRequest.Closed,
                IsExpired = fileRequest.ExpiresAt < DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting public file request");
            return StatusCode(500, new { Error = "Failed to load request" });
        }
    }

    [AllowAnonymous]
    [HttpPost("{shortId}/initiate")]
    public async Task<IActionResult> InitiateUpload(string shortId, [FromBody] InitiateUploadRequest request)
    {
        try
        {
            var fileRequest = await ValidateOpenRequestAsync(shortId);
            if (fileRequest == null)
            {
                return BadRequest(new { Error = "This file request is no longer accepting uploads." });
            }

            // Each submission is a ShareLink owned by the requester.
            var submissionId = Guid.NewGuid().ToString();
            var submissionShortId = _s3Service.GenerateShortId();
            while (await _context.ShareLinks.AnyAsync(s => s.ShortId == submissionShortId)
                   || await _context.FileRequests.AnyAsync(r => r.ShortId == submissionShortId))
            {
                submissionShortId = _s3Service.GenerateShortId();
            }

            var expirationDays = _configuration.GetValue<int>("FileShare:ExpirationDays", 30);
            var submission = new ShareLink
            {
                Id = submissionId,
                ShortId = submissionShortId,
                UserId = fileRequest.UserId,
                FileRequestId = fileRequest.Id,
                SubmitterName = string.IsNullOrWhiteSpace(request.SubmitterName) ? null : request.SubmitterName.Trim(),
                SubmitterEmail = string.IsNullOrWhiteSpace(request.SubmitterEmail) ? null : request.SubmitterEmail.Trim(),
                ExpiresAt = DateTime.UtcNow.AddDays(expirationDays)
            };

            _context.ShareLinks.Add(submission);
            await _context.SaveChangesAsync();

            var uploadSessions = new List<object>();
            foreach (var file in request.Files)
            {
                var uploadId = await _s3Service.InitiateMultipartUploadAsync(submissionId, file.FileName, file.MimeType);
                var key = $"file-share/{submissionId}/{file.FileName}";
                var totalParts = (int)Math.Ceiling((double)file.FileSize / CHUNK_SIZE);
                var presignedUrls = _s3Service.GeneratePresignedUrlsForUpload(key, uploadId, totalParts);

                uploadSessions.Add(new
                {
                    fileName = file.FileName,
                    fileSize = file.FileSize,
                    uploadId,
                    key,
                    totalParts,
                    presignedUrls
                });
            }

            return Ok(new
            {
                submissionId,
                submissionShortId,
                uploads = uploadSessions
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating file-request upload");
            return StatusCode(500, new { Error = "Failed to initiate upload" });
        }
    }

    [AllowAnonymous]
    [HttpPost("chunk")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadChunk()
    {
        try
        {
            var form = await Request.ReadFormAsync();
            var chunk = form.Files["chunk"];
            var key = form["key"].ToString();
            var uploadId = form["uploadId"].ToString();
            var partNumber = int.Parse(form["partNumber"].ToString());

            if (chunk == null || chunk.Length == 0)
            {
                return BadRequest(new { Error = "No chunk provided" });
            }

            using var stream = chunk.OpenReadStream();
            var etag = await _s3Service.UploadPartAsync(key, uploadId, partNumber, stream);
            return Ok(new { etag });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file-request chunk");
            return StatusCode(500, new { Error = "Failed to upload chunk" });
        }
    }

    [AllowAnonymous]
    [HttpPost("{shortId}/complete")]
    public async Task<IActionResult> CompleteUpload(string shortId, [FromBody] CompleteUploadRequest request)
    {
        try
        {
            var fileRequest = await _context.FileRequests
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.ShortId == shortId);

            if (fileRequest == null || fileRequest.Deleted)
            {
                return NotFound(new { Error = "File request not found" });
            }

            var submission = await _context.ShareLinks
                .FirstOrDefaultAsync(s => s.ShortId == request.SubmissionShortId
                    && s.FileRequestId == fileRequest.Id);

            if (submission == null)
            {
                return NotFound(new { Error = "Submission not found" });
            }

            foreach (var upload in request.Uploads)
            {
                var parts = upload.Parts
                    .OrderBy(p => p.PartNumber)
                    .Select(p => new PartETag(p.PartNumber, p.ETag))
                    .ToList();
                await _s3Service.CompleteMultipartUploadAsync(upload.Key, upload.UploadId, parts);

                _context.Files.Add(new FileModel
                {
                    ShareLinkId = submission.Id,
                    S3Key = upload.Key,
                    FileName = upload.FileName,
                    FileSize = upload.FileSize,
                    MimeType = upload.MimeType
                });
            }

            await _context.SaveChangesAsync();

            await _authService.LogAuditAsync("REQUEST_UPLOAD", fileRequest.User, fileRequest.Id, "fileRequest",
                new { FileCount = request.Uploads.Count, submission.SubmitterEmail },
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString());

            // Notify the requester (best-effort; don't fail the upload if email fails).
            try
            {
                var files = request.Uploads.Select(u => (u.FileName, u.FileSize)).ToList();
                var downloadUrl = $"{Request.Scheme}://{Request.Host}/s/{submission.ShortId}";
                var submitterName = submission.SubmitterName
                    ?? submission.SubmitterEmail
                    ?? "Someone";

                await _emailService.SendFileRequestReceivedEmailAsync(
                    fileRequest.User.Email,
                    fileRequest.User.Name ?? fileRequest.User.Email,
                    fileRequest.Title,
                    submitterName,
                    downloadUrl,
                    files);
            }
            catch (Exception emailEx)
            {
                _logger.LogError(emailEx, "File-request upload succeeded but notification email failed");
            }

            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing file-request upload");
            return StatusCode(500, new { Error = "Failed to complete upload" });
        }
    }

    private async Task<FileRequest?> ValidateOpenRequestAsync(string shortId)
    {
        var fileRequest = await _context.FileRequests
            .FirstOrDefaultAsync(r => r.ShortId == shortId);

        if (fileRequest == null || fileRequest.Deleted || fileRequest.Closed
            || fileRequest.ExpiresAt < DateTime.UtcNow)
        {
            return null;
        }
        return fileRequest;
    }

    // --- DTOs ---

    public class CreateRequest
    {
        public string Title { get; set; } = null!;
        public string? Message { get; set; }
        public string? RecipientEmail { get; set; }
        public string? RecipientName { get; set; }
        public int? ExpirationDays { get; set; }
    }

    public class SendInviteRequest
    {
        public string RequestShortId { get; set; } = null!;
        public string RecipientEmail { get; set; } = null!;
        public string? RecipientName { get; set; }
        public string? Message { get; set; }
    }

    public class RequestActionRequest
    {
        public string RequestId { get; set; } = null!;
    }

    public class InitiateUploadRequest
    {
        public string? SubmitterName { get; set; }
        public string? SubmitterEmail { get; set; }
        public List<UploadFileInfo> Files { get; set; } = new();
    }

    public class UploadFileInfo
    {
        public string FileName { get; set; } = null!;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = null!;
    }

    public class CompleteUploadRequest
    {
        public string SubmissionShortId { get; set; } = null!;
        public List<UploadInfo> Uploads { get; set; } = new();
    }

    public class UploadInfo
    {
        public string Key { get; set; } = null!;
        public string UploadId { get; set; } = null!;
        public List<PartInfo> Parts { get; set; } = new();
        public string FileName { get; set; } = null!;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = null!;
    }

    public class PartInfo
    {
        public int PartNumber { get; set; }
        public string ETag { get; set; } = null!;
    }
}
