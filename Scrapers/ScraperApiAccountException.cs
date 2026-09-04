namespace IWEHZ.Scrapers;

/// <summary>
/// Raised when ScraperAPI itself rejects a request for an account-level reason
/// (401 bad key, 403 out of credits, 429 concurrency limit) rather than the
/// target site blocking us. One such failure means every source is dead this
/// cycle, so the worker alerts once and skips the rest of the run.
/// </summary>
public sealed class ScraperApiAccountException(int statusCode)
    : Exception($"ScraperAPI rejected the request with HTTP {statusCode}")
{
    public int StatusCode { get; } = statusCode;
}
