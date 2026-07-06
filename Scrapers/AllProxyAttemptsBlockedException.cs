namespace IWEHZ.Scrapers;

public sealed class AllProxyAttemptsBlockedException(string source, int attempts)
    : Exception($"[{source}] blocked on all {attempts} proxy attempts")
{
    public string SourceName { get; } = source;
    public int Attempts { get; } = attempts;
}
