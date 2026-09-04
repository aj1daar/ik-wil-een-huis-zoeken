using System.Net;
using System.Net.Security;
using System.Text.Json;

namespace IWEHZ.Infrastructure.Http;

/// <summary>
/// Builds <see cref="HttpClient"/> instances that route scraper traffic through
/// ScraperAPI's proxy endpoint (rotating IPs, geo-targeting, and optional
/// headless-browser rendering / anti-bot bypass).
///
/// When <c>Scraper:ScraperApiKey</c> is not configured the fetcher falls back to a
/// plain direct client so local development works without spending credits.
///
/// Free / Hobby ScraperAPI plans only allow <c>us</c> or <c>eu</c> geo-targeting —
/// individual country codes (e.g. <c>nl</c>) need the Business plan. Default is
/// <c>eu</c>; override with <c>Scraper:ScraperApiCountry</c>.
///
/// Per-source upgrades can be forced from config without a redeploy, e.g.
/// <c>Scraper:ScraperApiRender:kamernet=true</c> or
/// <c>Scraper:ScraperApiUltraPremium:directwonen=true</c>.
/// </summary>
public sealed class ScraperFetcher(IConfiguration config)
{
    private const string ProxyHost = "proxy-server.scraperapi.com";
    private const int ProxyPort = 8001;

    private readonly string? _apiKey = config["Scraper:ScraperApiKey"];
    private readonly string _country = config["Scraper:ScraperApiCountry"] is { Length: > 0 } c ? c : "eu";

    public bool UsesScraperApi => !string.IsNullOrWhiteSpace(_apiKey);

    /// <param name="source">Source name, used to pick up per-source config overrides. Null = no overrides.</param>
    /// <param name="render">Run the target through a headless browser (JS execution). 10 credits.</param>
    /// <param name="ultraPremium">Hardest anti-bot pool. 30 credits (75 with render) — avoid on the free plan.</param>
    public HttpClient CreateClient(string? source = null, bool render = false, bool ultraPremium = false)
    {
        if (source is not null)
        {
            render |= config.GetValue($"Scraper:ScraperApiRender:{source}", false);
            ultraPremium |= config.GetValue($"Scraper:ScraperApiUltraPremium:{source}", false);
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
            return ScraperHttpClientFactory.Create(null);

        // ScraperAPI proxy-mode options are passed as a dot-joined list in the proxy username.
        var options = new List<string> { "scraperapi", $"country_code={_country}" };
        if (render) options.Add("render=true");
        if (ultraPremium) options.Add("ultra_premium=true");
        var username = string.Join('.', options);

        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy($"http://{ProxyHost}:{ProxyPort}", BypassOnLocal: false)
            {
                Credentials = new NetworkCredential(username, _apiKey),
            },
            UseProxy = true,
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            // ScraperAPI's proxy mode terminates TLS itself, dynamically re-signing each
            // target's certificate with its own private root (documented requirement, not
            // optional). That root isn't in our trust store, so chain validation always fails —
            // but the leaf still matches the real target hostname, so only chain-trust errors
            // are waived here; a genuine MITM (wrong host, no cert) still fails the connection.
            ServerCertificateCustomValidationCallback = (_, _, _, errors) =>
                (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.None,
        };

        var client = new HttpClient(handler)
        {
            // Rendered / ultra-premium requests plus ScraperAPI's internal retries can take a while.
            Timeout = TimeSpan.FromSeconds(render || ultraPremium ? 150 : 90),
        };

        ScraperHttpClientFactory.ApplyDefaultHeaders(client);
        return client;
    }

    /// <summary>
    /// Reads the account's credit usage from ScraperAPI's <c>/account</c> endpoint
    /// (does not consume credits). Returns null when no key is configured or the
    /// call fails. <c>requestCount</c>/<c>requestLimit</c> track API credits.
    /// </summary>
    public async Task<(int Used, int Limit)?> GetCreditUsageAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return null;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var json = await http.GetStringAsync(
                $"https://api.scraperapi.com/account?api_key={Uri.EscapeDataString(_apiKey)}", ct);
            var root = JsonSerializer.Deserialize<JsonElement>(json);
            var used = root.GetProperty("requestCount").GetInt32();
            var limit = root.GetProperty("requestLimit").GetInt32();
            return (used, limit);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
