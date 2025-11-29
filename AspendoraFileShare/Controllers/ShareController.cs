using AspendoraFileShare.Data;
using AspendoraFileShare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AspendoraFileShare.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ShareController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly EmailService _emailService;
    private readonly AuthService _authService;
    private readonly S3Service _s3Service;
    private readonly ILogger<ShareController> _logger;

    public ShareController(
        ApplicationDbContext context,
        EmailService emailService,
        AuthService authService,
        S3Service s3Service,
        ILogger<ShareController> logger)
    {
        _context = context;
        _emailService = emailService;
        _authService = authService;
        _s3Service = s3Service;
        _logger = logger;
    }

    [HttpPost("email")]
    public async Task<IActionResult> SendEmail([FromBody] SendEmailRequest request)
    {
        _logger.LogInformation("SendEmail endpoint called: ShareLinkId={ShareLinkId}, RecipientEmail={RecipientEmail}",
            request.ShareLinkId, request.RecipientEmail);

        try
        {
            var user = await _authService.GetOrCreateUserAsync(User);
            _logger.LogInformation("User authenticated: {UserEmail}", user.Email);

            var shareLink = await _context.ShareLinks
                .Include(s => s.Files)
                .FirstOrDefaultAsync(s => s.ShortId == request.ShareLinkId);

            if (shareLink == null)
            {
                return NotFound(new { Error = "Share link not found" });
            }

            if (shareLink.UserId != user.Id)
            {
                return Forbid();
            }

            // Update share link with recipient info
            shareLink.RecipientEmail = request.RecipientEmail;
            shareLink.RecipientName = request.RecipientName;
            shareLink.Message = request.Message;
            await _context.SaveChangesAsync();

            // Determine sender email
            var senderEmail = user.Email;
            var domain = senderEmail.Split('@')[1].ToLower();
            var allowedDomains = new[] { "aspendora.com", "3endt.com", "ir100.com" };

            if (!allowedDomains.Contains(domain))
            {
                senderEmail = "noreply@aspendora.com";
            }

            // Send email
            var shareUrl = $"{Request.Scheme}://{Request.Host}/s/{shareLink.ShortId}";
            var files = shareLink.Files.Select(f => (f.FileName, f.FileSize)).ToList();

            _logger.LogInformation("Calling EmailService.SendShareEmailAsync: shareUrl={ShareUrl}, senderEmail={SenderEmail}, fileCount={FileCount}",
                shareUrl, senderEmail, files.Count);

            await _emailService.SendShareEmailAsync(
                request.RecipientEmail,
                request.RecipientName ?? request.RecipientEmail,
                user.Name ?? user.Email,
                senderEmail,
                shareUrl,
                request.Message,
                files
            );

            _logger.LogInformation("Email sent successfully, logging audit");

            // Log audit
            await _authService.LogAuditAsync("SHARE", user, shareLink.Id, "shareLink",
                new { RecipientEmail = request.RecipientEmail },
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString());

            _logger.LogInformation("SendEmail completed successfully for ShareLinkId={ShareLinkId}", request.ShareLinkId);
            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending share email");
            return StatusCode(500, new { Error = "Failed to send email" });
        }
    }

    [HttpGet("list")]
    public async Task<IActionResult> ListShares()
    {
        try
        {
            var user = await _authService.GetOrCreateUserAsync(User);

            var shares = await _context.ShareLinks
                .Include(s => s.Files)
                .Where(s => s.UserId == user.Id && !s.Deleted)
                .OrderByDescending(s => s.CreatedAt)
                .Take(50)
                .Select(s => new
                {
                    s.Id,
                    s.ShortId,
                    s.RecipientEmail,
                    s.RecipientName,
                    s.CreatedAt,
                    s.ExpiresAt,
                    s.Downloads,
                    Files = s.Files.Select(f => new
                    {
                        f.Id,
                        f.FileName,
                        f.FileSize,
                        f.MimeType
                    }).ToList()
                })
                .ToListAsync();

            return Ok(new { Shares = shares });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing shares");
            return StatusCode(500, new { Error = "Failed to list shares" });
        }
    }

    [HttpPost("delete")]
    public async Task<IActionResult> DeleteShare([FromBody] DeleteRequest request)
    {
        try
        {
            var user = await _authService.GetOrCreateUserAsync(User);

            var shareLink = await _context.ShareLinks
                .Include(s => s.Files)
                .FirstOrDefaultAsync(s => s.Id == request.ShareId);

            if (shareLink == null)
            {
                return NotFound(new { Error = "Share not found" });
            }

            if (shareLink.UserId != user.Id)
            {
                return Forbid();
            }

            // Delete files from S3
            await _s3Service.DeleteShareFilesAsync(shareLink.Id);

            // Mark as deleted
            shareLink.Deleted = true;
            shareLink.DeletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Log audit
            await _authService.LogAuditAsync("DELETE", user, shareLink.Id, "shareLink",
                null,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString());

            return Ok(new { Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting share");
            return StatusCode(500, new { Error = "Failed to delete share" });
        }
    }

    public class SendEmailRequest
    {
        public string ShareLinkId { get; set; } = null!;
        public string RecipientEmail { get; set; } = null!;
        public string? RecipientName { get; set; }
        public string? Message { get; set; }
    }

    public class DeleteRequest
    {
        public string ShareId { get; set; } = null!;
    }

    /// <summary>
    /// Get public share details (no authentication required)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("public/{shortId}")]
    public async Task<IActionResult> GetPublicShare(string shortId)
    {
        try
        {
            var shareLink = await _context.ShareLinks
                .Include(s => s.Files)
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.ShortId == shortId);

            if (shareLink == null)
            {
                return NotFound(new { Error = "Share not found" });
            }

            if (shareLink.Deleted)
            {
                return NotFound(new { Error = "Share has been deleted" });
            }

            var isExpired = shareLink.ExpiresAt < DateTime.UtcNow;

            return Ok(new
            {
                shareLink.ShortId,
                SenderName = shareLink.User?.Name ?? shareLink.User?.Email ?? "Unknown",
                shareLink.Message,
                shareLink.CreatedAt,
                shareLink.ExpiresAt,
                IsExpired = isExpired,
                shareLink.Downloads,
                Files = shareLink.Files.Select(f => new
                {
                    f.Id,
                    f.FileName,
                    f.FileSize,
                    f.MimeType
                }).ToList(),
                TotalSize = shareLink.Files.Sum(f => f.FileSize)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting public share");
            return StatusCode(500, new { Error = "Failed to get share details" });
        }
    }
}
