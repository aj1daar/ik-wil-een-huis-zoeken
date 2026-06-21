namespace IWEHZ.Domain.Models;

public class NotificationLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ListingId { get; set; }
    public RentalListing Listing { get; set; } = null!;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
