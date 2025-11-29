using System.ComponentModel.DataAnnotations;

namespace AspendoraFileShare.Data.Models;

public class ShareLink
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(8)]
    public string ShortId { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = null!;

    public string? RecipientEmail { get; set; }

    public string? RecipientName { get; set; }

    public string? Message { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public int Downloads { get; set; } = 0;

    public DateTime? LastDownloadAt { get; set; }

    public bool Deleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    // Navigation properties
    public virtual User User { get; set; } = null!;
    public virtual ICollection<FileModel> Files { get; set; } = new List<FileModel>();
}
