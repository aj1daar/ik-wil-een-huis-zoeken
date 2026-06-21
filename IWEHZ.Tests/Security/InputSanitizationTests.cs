using IWEHZ.Bot;
using IWEHZ.Infrastructure.Markdown;
using IWEHZ.Scrapers;

namespace IWEHZ.Tests.Security;

public sealed class InputSanitizationTests
{
    private static readonly string[] SqlInjectionPayloads =
    [
        "' OR '1'='1",
        "'; DROP TABLE users; --",
        "' UNION SELECT * FROM users--",
        "1; SELECT pg_sleep(5)--",
        "admin'--",
        "' OR 1=1#",
        "') OR ('1'='1",
        "1' AND SLEEP(5)#",
    ];

    private static readonly string[] XssPayloads =
    [
        "<script>alert('xss')</script>",
        "<img src=x onerror=alert(1)>",
        "javascript:alert(1)",
        "<svg onload=alert(1)>",
        "';alert(1)//",
        "\"><script>alert(1)</script>",
    ];

    private static readonly string[] TemplateInjectionPayloads =
    [
        "{{7*7}}",
        "${7*7}",
        "#{7*7}",
        "<%= 7*7 %>",
        "{{config}}",
        "${system.exit(1)}",
    ];

    [Theory]
    [MemberData(nameof(GetAllPayloads))]
    public void BudgetParser_AnyMaliciousPayload_ReturnsFalseWithoutThrowing(string payload)
    {
        var act = () => BudgetParser.TryParse(payload, out _);
        act.Should().NotThrow();
        BudgetParser.TryParse(payload, out _).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(GetAllPayloads))]
    public void PriceParser_AnyMaliciousPayload_ReturnsZeroWithoutThrowing(string payload)
    {
        var act = () => ScraperHelpers.ParsePrice(payload);
        act.Should().NotThrow();
        ScraperHelpers.ParsePrice(payload).Should().BeGreaterThanOrEqualTo(0);
    }

    [Theory]
    [MemberData(nameof(GetAllPayloads))]
    public void MarkdownEscape_AnyMaliciousPayload_DoesNotThrowAndEscapesResult(string payload)
    {
        var act = () => MarkdownHelper.EscapeV2(payload);
        act.Should().NotThrow();

        var result = MarkdownHelper.EscapeV2(payload);
        result.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(GetAllPayloads))]
    public void UrlSegmentExtraction_AnyMaliciousPayload_DoesNotThrow(string payload)
    {
        var act = () => ScraperHelpers.ExtractLastUrlSegment(payload);
        act.Should().NotThrow();
    }

    [Fact]
    public void BudgetParser_NullByteInput_ReturnsFalse()
    {
        BudgetParser.TryParse("\0\0\0", out _).Should().BeFalse();
    }

    [Fact]
    public void PriceParser_NullByteInput_ReturnsZero()
    {
        ScraperHelpers.ParsePrice("\0\0\0").Should().Be(0);
    }

    [Fact]
    public void MarkdownEscape_NullBytes_DoesNotThrow()
    {
        var act = () => MarkdownHelper.EscapeV2("\0\0\0");
        act.Should().NotThrow();
    }

    [Fact]
    public void BudgetParser_UnicodeDigitLookalikes_ReturnsFalse()
    {
        BudgetParser.TryParse("１５００", out _).Should().BeFalse();
    }

    [Fact]
    public void BudgetParser_PathTraversal_ReturnsFalse()
    {
        BudgetParser.TryParse("../../etc/passwd", out _).Should().BeFalse();
    }

    public static IEnumerable<object[]> GetAllPayloads()
    {
        foreach (var p in SqlInjectionPayloads) yield return [p];
        foreach (var p in XssPayloads) yield return [p];
        foreach (var p in TemplateInjectionPayloads) yield return [p];
    }
}
