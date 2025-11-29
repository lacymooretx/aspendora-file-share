using AspendoraFileShare.Data;
using AspendoraFileShare.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;

namespace AspendoraFileShare.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DownloadController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly S3Service _s3Service;
    private readonly ILogger<DownloadController> _logger;

    public DownloadController(
        ApplicationDbContext context,
        S3Service s3Service,
        ILogger<DownloadController> logger)
    {
        _context = context;
        _s3Service = s3Service;
        _logger = logger;
    }

    [HttpGet("{shareId}")]
    public async Task<IActionResult> Download(string shareId)
    {
        try
        {
            var shareLink = await _context.ShareLinks
                .Include(s => s.Files)
                .FirstOrDefaultAsync(s => s.ShortId == shareId);

            if (shareLink == null || shareLink.Deleted || shareLink.ExpiresAt < DateTime.UtcNow)
            {
                return NotFound();
            }

            // Increment download counter
            shareLink.Downloads++;
            shareLink.LastDownloadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Single file - direct download
            if (shareLink.Files.Count == 1)
            {
                var file = shareLink.Files.First();
                var stream = await _s3Service.GetFileAsync(file.S3Key);

                return File(stream, file.MimeType, file.FileName);
            }

            // Multiple files - create zip
            var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var file in shareLink.Files)
                {
                    var entry = archive.CreateEntry(file.FileName, CompressionLevel.Fastest);
                    using var entryStream = entry.Open();
                    using var s3Stream = await _s3Service.GetFileAsync(file.S3Key);
                    await s3Stream.CopyToAsync(entryStream);
                }
            }

            memoryStream.Position = 0;
            return File(memoryStream, "application/zip", $"files_{shareId}.zip");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading files for share {ShareId}", shareId);
            return StatusCode(500, new { Error = "Failed to download files" });
        }
    }
}
