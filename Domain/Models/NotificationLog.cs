namespace IWEHZ.Domain.Models;

public class NotificationLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ListingId { get; set; }
    public RentalListing Listing { get; set; } = null!;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    /// <summary>Telegram message id of the alert, so it can be deleted once the home is let. Null for alerts sent before this was tracked, or when the send failed.</summary>
    public int? MessageId { get; set; }

    /// <summary>When the alert was withdrawn (deleted, or edited to say the home is gone). Null while it still stands.</summary>
    public DateTime? RetractedAt { get; set; }
}
