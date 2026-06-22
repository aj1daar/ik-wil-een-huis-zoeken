namespace IWEHZ.Domain.Models;

public class RentalListing
{
    public int Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? PreviousPrice { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
    public ICollection<NotificationLog> NotificationLogs { get; set; } = [];
}
