using System.ComponentModel.DataAnnotations;

namespace AspendoraFileShare.Data.Models;

/// <summary>
/// A request created by an authenticated user asking someone else (anonymous) to
/// upload files. Each upload submission to the request becomes a ShareLink owned by
/// the requester (ShareLink.FileRequestId == this.Id), so received files reuse the
/// existing storage, download, and cleanup pipeline.
/// </summary>
public class FileRequest
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [StringLength(8)]
    public string ShortId { get; set; } = null!;

    /// <summary>The user who created the request and receives the files.</summary>
    [Required]
    public string UserId { get; set; } = null!;

    [Required]
    public string Title { get; set; } = null!;

    public string? Message { get; set; }

    /// <summary>Optional person the request was sent to (informational).</summary>
    public string? RecipientEmail { get; set; }

    public string? RecipientName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Manually closed by the owner; no further uploads accepted.</summary>
    public bool Closed { get; set; } = false;

    public bool Deleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    // Navigation properties
    public virtual User User { get; set; } = null!;

    /// <summary>Upload submissions; each is a ShareLink owned by the requester.</summary>
    public virtual ICollection<ShareLink> Submissions { get; set; } = new List<ShareLink>();
}
