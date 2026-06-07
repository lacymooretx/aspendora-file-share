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

    // --- File-request submission fields ---
    // When set, this ShareLink is an upload submission to a FileRequest rather than
    // an outgoing share. The owner (UserId) is the requester who receives the files.

    /// <summary>If non-null, this share is a submission to the given FileRequest.</summary>
    public string? FileRequestId { get; set; }

    /// <summary>Name the anonymous uploader provided (submissions only).</summary>
    public string? SubmitterName { get; set; }

    /// <summary>Email the anonymous uploader provided (submissions only).</summary>
    public string? SubmitterEmail { get; set; }

    // Navigation properties
    public virtual User User { get; set; } = null!;
    public virtual FileRequest? FileRequest { get; set; }
    public virtual ICollection<FileModel> Files { get; set; } = new List<FileModel>();
}
