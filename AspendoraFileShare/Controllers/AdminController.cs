using AspendoraFileShare.Data;
using AspendoraFileShare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AspendoraFileShare.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly AuthService _authService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        ApplicationDbContext context,
        AuthService authService,
        ILogger<AdminController> logger)
    {
        _context = context;
        _authService = authService;
        _logger = logger;
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromQuery] int page = 1, [FromQuery] int limit = 50, [FromQuery] string? action = null)
    {
        try
        {
            if (!await _authService.IsAdminAsync(User))
            {
                return StatusCode(403, new { Error = "Admin access required" });
            }

            var user = await _authService.GetOrCreateUserAsync(User);

            // Log admin view
            await _authService.LogAuditAsync("VIEW_ADMIN", user, null, null, null,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString());

            var query = _context.AuditLogs
                .Include(l => l.User)
                .AsQueryable();

            if (!string.IsNullOrEmpty(action))
            {
                query = query.Where(l => l.Action == action);
            }

            var total = await query.CountAsync();
            var logs = await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(l => new
                {
                    l.Id,
                    l.Action,
                    l.UserEmail,
                    l.UserTenant,
                    l.TargetId,
                    l.TargetType,
                    l.MetadataJson,
                    l.IpAddress,
                    l.CreatedAt,
                    User = new
                    {
                        l.User.Name,
                        l.User.Email
                    }
                })
                .ToListAsync();

            return Ok(new
            {
                Logs = logs,
                Pagination = new
                {
                    Page = page,
                    Limit = limit,
                    Total = total,
                    Pages = (int)Math.Ceiling(total / (double)limit)
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching admin logs");
            return StatusCode(500, new { Error = "Failed to fetch logs" });
        }
    }

    [HttpGet("shares")]
    public async Task<IActionResult> GetAllShares()
    {
        try
        {
            if (!await _authService.IsAdminAsync(User))
            {
                return StatusCode(403, new { Error = "Admin access required" });
            }

            var shares = await _context.ShareLinks
                .Include(s => s.Files)
                .Include(s => s.User)
                .OrderByDescending(s => s.CreatedAt)
                .Take(100)
                .Select(s => new
                {
                    s.Id,
                    s.ShortId,
                    s.RecipientEmail,
                    s.RecipientName,
                    s.CreatedAt,
                    s.ExpiresAt,
                    s.Downloads,
                    s.Deleted,
                    User = new
                    {
                        s.User.Name,
                        s.User.Email
                    },
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
            _logger.LogError(ex, "Error fetching all shares");
            return StatusCode(500, new { Error = "Failed to fetch shares" });
        }
    }
}
