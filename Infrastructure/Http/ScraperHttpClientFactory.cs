using System.Net;

namespace IWEHZ.Infrastructure.Http;

public static class ScraperHttpClientFactory
{
    private static readonly string[] UserAgents =
    [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:126.0) Gecko/20100101 Firefox/126.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4.1 Safari/605.1.15",
    ];

    public static HttpClient Create(string? proxyUrl = null)
    {
        HttpMessageHandler handler;

        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            var proxyUri = new Uri(proxyUrl);
            var proxy = new WebProxy(proxyUri.GetLeftPart(UriPartial.Authority), BypassOnLocal: false);

            if (!string.IsNullOrEmpty(proxyUri.UserInfo))
            {
                var colonIdx = proxyUri.UserInfo.IndexOf(':');
                var user = colonIdx >= 0
                    ? Uri.UnescapeDataString(proxyUri.UserInfo[..colonIdx])
                    : Uri.UnescapeDataString(proxyUri.UserInfo);
                var pass = colonIdx >= 0
                    ? Uri.UnescapeDataString(proxyUri.UserInfo[(colonIdx + 1)..])
                    : string.Empty;
                proxy.Credentials = new NetworkCredential(user, pass);
            }

            handler = new HttpClientHandler
            {
                Proxy = proxy,
                UseProxy = true,
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            };
        }
        else
        {
            handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            };
        }

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        var ua = UserAgents[Random.Shared.Next(UserAgents.Length)];

        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ua);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "nl-NL,nl;q=0.9,en-US;q=0.8,en;q=0.7");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cache-Control", "no-cache");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Pragma", "no-cache");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "none");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-User", "?1");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        client.DefaultRequestHeaders.TryAddWithoutValidation("DNT", "1");

        if (ua.Contains("Chrome"))
        {
            var secChUa = ua.Contains("125")
                ? "\"Google Chrome\";v=\"125\", \"Chromium\";v=\"125\", \"Not-A.Brand\";v=\"24\""
                : "\"Google Chrome\";v=\"124\", \"Chromium\";v=\"124\", \"Not-A.Brand\";v=\"24\"";

            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua", secChUa);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Platform", ua.Contains("Macintosh") ? "\"macOS\"" : "\"Windows\"");
        }

        return client;
    }
}
