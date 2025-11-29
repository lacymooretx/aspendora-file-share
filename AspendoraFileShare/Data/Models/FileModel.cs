using System.ComponentModel.DataAnnotations;

namespace AspendoraFileShare.Data.Models;

public class FileModel
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string ShareLinkId { get; set; } = null!;

    [Required]
    public string S3Key { get; set; } = null!;

    [Required]
    public string FileName { get; set; } = null!;

    public long FileSize { get; set; }

    [Required]
    public string MimeType { get; set; } = null!;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ShareLink ShareLink { get; set; } = null!;
}
