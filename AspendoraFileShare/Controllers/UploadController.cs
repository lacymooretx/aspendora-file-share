using AspendoraFileShare.Data;
using AspendoraFileShare.Data.Models;
using AspendoraFileShare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Amazon.S3.Model;

namespace AspendoraFileShare.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UploadController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly S3Service _s3Service;
    private readonly AuthService _authService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UploadController> _logger;

    public UploadController(
        ApplicationDbContext context,
        S3Service _s3Service,
        AuthService authService,
        IConfiguration configuration,
        ILogger<UploadController> logger)
    {
        _context = context;
        this._s3Service = _s3Service;
        _authService = authService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("initiate")]
    public async Task<IActionResult> Initiate([FromBody] InitiateRequest request)
    {
        try
        {
            var user = await _authService.GetOrCreateUserAsync(User);
            var shareId = Guid.NewGuid().ToString();
            var shortId = _s3Service.GenerateShortId();

            // Check if shortId already exists
            while (await _context.ShareLinks.AnyAsync(s => s.ShortId == shortId))
            {
                shortId = _s3Service.GenerateShortId();
            }

            var expirationDays = _configuration.GetValue<int>("FileShare:ExpirationDays", 30);
            var shareLink = new ShareLink
            {
                Id = shareId,
                ShortId = shortId,
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(expirationDays)
            };

            _context.ShareLinks.Add(shareLink);
            await _context.SaveChangesAsync();

            var uploadSessions = new List<object>();
            const int CHUNK_SIZE = 50 * 1024 * 1024; // 50MB chunks - must match JS

            foreach (var file in request.Files)
            {
                _logger.LogInformation("Initiate upload: file={FileName}, size={FileSize} bytes, chunkSize={ChunkSize}, parts={Parts}",
                    file.Name, file.Size, CHUNK_SIZE, (int)Math.Ceiling((double)file.Size / CHUNK_SIZE));

                var uploadId = await _s3Service.InitiateMultipartUploadAsync(shareId, file.Name, file.Type);
                var key = $"file-share/{shareId}/{file.Name}";
                var totalParts = (int)Math.Ceiling((double)file.Size / CHUNK_SIZE);

                // Generate presigned URLs for direct browser-to-S3 uploads
                var presignedUrls = _s3Service.GeneratePresignedUrlsForUpload(key, uploadId, totalParts);

                uploadSessions.Add(new
                {
                    fileName = file.Name,
                    fileSize = file.Size,
                    uploadId = uploadId,
                    key = key,
                    totalParts = totalParts,
                    presignedUrls = presignedUrls
                });
            }

            return Ok(new
            {
                shareId = shareId,
                shareLinkId = shortId,
                shortId = shortId,
                uploads = uploadSessions
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating upload");
            return StatusCode(500, new { Error = "Failed to initiate upload" });
        }
    }

    [HttpPost("chunk")]
    [RequestSizeLimit(60 * 1024 * 1024)] // 60MB
    [DisableRequestSizeLimit] // Allow up to Kestrel's limit
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

            return Ok(new { etag = etag });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading chunk");
            return StatusCode(500, new { Error = "Failed to upload chunk" });
        }
    }

    [HttpPost("complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteRequest request)
    {
        try
        {
            var user = await _authService.GetOrCreateUserAsync(User);

            foreach (var upload in request.Uploads)
            {
                _logger.LogInformation("Completing upload: key={Key}, uploadId={UploadId}, partsCount={PartsCount}",
                    upload.Key, upload.UploadId, upload.Parts.Count);

                // Log each part
                foreach (var part in upload.Parts.OrderBy(p => p.PartNumber))
                {
                    _logger.LogInformation("  Part {PartNumber}: ETag={ETag}", part.PartNumber, part.ETag);
                }

                var parts = upload.Parts.Select(p => new PartETag(p.PartNumber, p.ETag)).ToList();
                await _s3Service.CompleteMultipartUploadAsync(upload.Key, upload.UploadId, parts);

                // Create file record
                var shareLink = await _context.ShareLinks.FirstOrDefaultAsync(s => s.ShortId == request.ShareLinkId);
                if (shareLink == null)
                {
                    return NotFound(new { Error = "Share link not found" });
                }

                var file = new FileModel
                {
                    ShareLinkId = shareLink.Id,
                    S3Key = upload.Key,
                    FileName = upload.FileName,
                    FileSize = upload.FileSize,
                    MimeType = upload.MimeType
                };

                _context.Files.Add(file);
            }

            await _context.SaveChangesAsync();

            // Log audit
            await _authService.LogAuditAsync("UPLOAD", user, request.ShareLinkId, "shareLink",
                new { FileCount = request.Uploads.Count },
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString());

            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing upload");
            return StatusCode(500, new { Error = "Failed to complete upload" });
        }
    }

    public class InitiateRequest
    {
        public List<FileInfo> Files { get; set; } = new();
    }

    public class FileInfo
    {
        public string FileName { get; set; } = null!;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = null!;

        // Convenience aliases
        public string Name => FileName;
        public long Size => FileSize;
        public string Type => MimeType;
    }

    public class CompleteRequest
    {
        public string ShareLinkId { get; set; } = null!;
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
