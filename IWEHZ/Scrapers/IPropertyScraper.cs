namespace IWEHZ.Scrapers;

public interface IPropertyScraper
{
    string SourceName { get; }
    Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct);
}
