using System.ComponentModel.DataAnnotations;

namespace AspendoraFileShare.Data.Models;

public class AuditLog
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string Action { get; set; } = null!; // UPLOAD, SHARE, DOWNLOAD, DELETE, VIEW_ADMIN

    [Required]
    public string UserId { get; set; } = null!;

    [Required]
    public string UserEmail { get; set; } = null!;

    [Required]
    public string UserTenant { get; set; } = null!;

    public string? TargetId { get; set; }

    public string? TargetType { get; set; }

    public string? MetadataJson { get; set; } // JSON string for additional context

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual User User { get; set; } = null!;
}
