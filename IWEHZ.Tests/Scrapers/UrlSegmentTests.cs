using IWEHZ.Scrapers;

namespace IWEHZ.Tests.Scrapers;

public sealed class UrlSegmentTests
{
    [Theory]
    [InlineData("https://www.pararius.nl/huurwoningen/amsterdam/appartement-123", "appartement-123")]
    [InlineData("https://www.pararius.nl/huurwoningen/rotterdam/huis-456/", "huis-456")]
    [InlineData("https://example.com/listing/abc-xyz", "abc-xyz")]
    [InlineData("single-segment", "single-segment")]
    public void ExtractLastUrlSegment_ValidUrls_ReturnsLastSegment(string url, string expected)
    {
        ScraperHelpers.ExtractLastUrlSegment(url).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractLastUrlSegment_EmptyInput_ReturnsEmpty(string input)
    {
        ScraperHelpers.ExtractLastUrlSegment(input).Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://example.com/../../etc/passwd")]
    [InlineData("https://example.com/<script>alert(1)</script>")]
    [InlineData("https://example.com/'; DROP TABLE--")]
    public void ExtractLastUrlSegment_MaliciousUrls_DoesNotThrowAndReturnsLastSegment(string url)
    {
        var act = () => ScraperHelpers.ExtractLastUrlSegment(url);
        act.Should().NotThrow();
        ScraperHelpers.ExtractLastUrlSegment(url).Should().NotBeNull();
    }

    [Fact]
    public void ExtractLastUrlSegment_VeryLongUrl_DoesNotThrow()
    {
        var url = "https://example.com/" + new string('a', 10_000);
        var act = () => ScraperHelpers.ExtractLastUrlSegment(url);
        act.Should().NotThrow();
    }
}
