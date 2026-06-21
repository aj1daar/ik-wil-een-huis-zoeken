using System.Text.Json;
using IWEHZ.Infrastructure.Http;

namespace IWEHZ.Scrapers;

public sealed class VestedaScraper : IPropertyScraper
{
    private const string SearchUrl = "https://www.vesteda.com/api/units/search";
    private readonly string? _proxyUrl;
    private readonly ILogger<VestedaScraper> _logger;

    public string SourceName => "vesteda";

    public VestedaScraper(IConfiguration config, ILogger<VestedaScraper> logger)
    {
        _proxyUrl = config["Scraper:ProxyUrl"];
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        using var http = ScraperHttpClientFactory.Create(_proxyUrl);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.vesteda.com/en/find-a-home");
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");

        var payload = new
        {
            placeId = "",
            placeInput = "",
            latitude = 52.3676m,
            longitude = 4.9041m,
            radius = 50,
            priceFrom = 0,
            priceTo = 5000,
            bedrooms = 0,
            page = 1,
            pageSize = 50,
            sortBy = "relevance",
        };

        JsonElement root;
        try
        {
            var response = await http.PostAsJsonAsync(SearchUrl, payload, ct);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            root = JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vesteda fetch failed");
            return [];
        }

        var listings = new List<ScrapedListing>();

        if (!root.TryGetProperty("units", out var units)) return listings;

        foreach (var unit in units.EnumerateArray())
        {
            try
            {
                var id = unit.TryGetProperty("id", out var idProp) ? idProp.GetRawText().Trim('"') : null;
                if (string.IsNullOrEmpty(id)) continue;

                var title = unit.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                var city = unit.TryGetProperty("city", out var cityProp) ? cityProp.GetString() ?? string.Empty : string.Empty;
                var price = unit.TryGetProperty("price", out var priceProp) ? priceProp.GetDecimal() : 0m;
                var slug = unit.TryGetProperty("slug", out var slugProp) ? slugProp.GetString() : id;
                var url = $"https://www.vesteda.com/en/find-a-home/{slug}";

                if (price <= 0) continue;

                listings.Add(new ScrapedListing(id, title, city, price, url, SourceName));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Vesteda: skipped malformed unit element");
            }
        }

        return listings;
    }
}
