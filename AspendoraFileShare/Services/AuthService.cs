using System.Security.Claims;
using AspendoraFileShare.Data;
using AspendoraFileShare.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Identity.Web;
using AppUser = AspendoraFileShare.Data.Models.User;

namespace AspendoraFileShare.Services;

public class AuthService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;
    private readonly GraphServiceClient _graphClient;

    public AuthService(
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<AuthService> logger,
        GraphServiceClient graphClient)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _graphClient = graphClient;
    }

    public async Task<AppUser> GetOrCreateUserAsync(ClaimsPrincipal principal)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("preferred_username");
        // Azure AD provides name in multiple claim types - try them all
        var name = principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("name")
            ?? principal.FindFirstValue(ClaimTypes.GivenName);
        var tenantId = principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");

        if (string.IsNullOrEmpty(email))
        {
            throw new Exception("Email claim not found");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            user = new AppUser
            {
                Email = email,
                Name = name,
                TenantId = tenantId
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
        }
        else if (user.Name != name || user.TenantId != tenantId)
        {
            user.Name = name;
            user.TenantId = tenantId;
            await _context.SaveChangesAsync();
        }

        return user;
    }

    public async Task<bool> IsAdminAsync(ClaimsPrincipal principal)
    {
        var tenantId = principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");
        var aspendoraTenantId = _configuration["AspendoraTenantId"];

        if (tenantId != aspendoraTenantId)
        {
            return false;
        }

        // Temporary: Allow lacy@aspendora.com
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("preferred_username");
        if (email == "lacy@aspendora.com")
        {
            return true;
        }

        try
        {
            var memberOf = await _graphClient.Me.MemberOf.GetAsync();
            var adminGroupName = _configuration["AdminGroupName"] ?? "file-share-app-admin";

            return memberOf?.Value?.Any(m =>
                m is Group group &&
                group.DisplayName?.Equals(adminGroupName, StringComparison.OrdinalIgnoreCase) == true) ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking admin status");
            return false;
        }
    }

    public bool IsTenantAllowed(string? tenantId)
    {
        if (string.IsNullOrEmpty(tenantId))
        {
            return false;
        }

        var allowedTenants = _configuration.GetSection("AllowedTenants").Get<List<string>>() ?? new List<string>();
        return allowedTenants.Contains(tenantId);
    }

    public async Task LogAuditAsync(string action, AppUser user, string? targetId = null, string? targetType = null, object? metadata = null, string? ipAddress = null, string? userAgent = null)
    {
        var log = new AuditLog
        {
            Action = action,
            UserId = user.Id,
            UserEmail = user.Email,
            UserTenant = user.TenantId ?? "unknown",
            TargetId = targetId,
            TargetType = targetType,
            MetadataJson = metadata != null ? System.Text.Json.JsonSerializer.Serialize(metadata) : null,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        _context.AuditLogs.Add(log);
        await _context.SaveChangesAsync();
    }
}
