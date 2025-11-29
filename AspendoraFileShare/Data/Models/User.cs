using System.ComponentModel.DataAnnotations;

namespace AspendoraFileShare.Data.Models;

public class User
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string? Name { get; set; }

    [Required]
    public string Email { get; set; } = null!;

    public string? TenantId { get; set; }

    public DateTime? EmailVerified { get; set; }

    public string? Image { get; set; }

    // Navigation properties
    public virtual ICollection<ShareLink> ShareLinks { get; set; } = new List<ShareLink>();
    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
