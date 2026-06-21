namespace IWEHZ.Scrapers;

public sealed record ScrapedListing(
    string ExternalId,
    string Title,
    string City,
    decimal Price,
    string SourceUrl,
    string Source);
