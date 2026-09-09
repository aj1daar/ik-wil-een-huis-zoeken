namespace IWEHZ.Scrapers;

/// <param name="StatusText">
/// Raw availability wording taken from the source (card text, status badge, schema.org
/// availability). Fed to <see cref="RentedStatusDetector"/> so already-let listings never
/// reach a user. Null when the source exposes nothing useful.
/// </param>
public sealed record ScrapedListing(
    string ExternalId,
    string Title,
    string City,
    decimal Price,
    string SourceUrl,
    string Source,
    string? StatusText = null);
