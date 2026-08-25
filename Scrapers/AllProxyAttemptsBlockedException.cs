namespace IWEHZ.Scrapers;

public sealed class AllProxyAttemptsBlockedException(string source, int attempts, string? reason = null)
    : Exception($"[{source}] blocked on all {attempts} proxy attempts" + (reason is null ? "" : $" (last: {reason})"))
{
    public string SourceName { get; } = source;
    public int Attempts { get; } = attempts;
    public string? Reason { get; } = reason;
}
