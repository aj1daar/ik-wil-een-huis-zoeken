using IWEHZ.Scrapers;

namespace IWEHZ.Tests.Scrapers;

public sealed class PriceParserTests
{
    [Theory]
    [InlineData("€ 1.500 per maand", 1500)]
    [InlineData("€1500", 1500)]
    [InlineData("€ 2.250", 2250)]
    [InlineData("€ 850,00", 850)]
    [InlineData("€1.500,00 per maand", 1500)]
    [InlineData("€ 2.000 p.m.", 2000)]
    [InlineData("750", 750)]
    [InlineData("€ 3.750 per maand", 3750)]
    [InlineData("1,250.00", 1250)]
    public void ParsePrice_ValidDutchFormats_ReturnsCorrectAmount(string input, decimal expected)
    {
        ScraperHelpers.ParsePrice(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("prijs op aanvraag")]
    [InlineData("n.v.t.")]
    [InlineData("op aanvraag")]
    [InlineData("price on request")]
    public void ParsePrice_NonNumericText_ReturnsZero(string input)
    {
        ScraperHelpers.ParsePrice(input).Should().Be(0);
    }

    [Theory]
    [InlineData("'; DROP TABLE rental_listings; --")]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("../../etc/passwd")]
    [InlineData("\0\0\0\0")]
    [InlineData("{{7*7}}")]
    [InlineData("SELECT * FROM users")]
    [InlineData("OR 1=1--")]
    [InlineData("99999999999999999999999999999999999999999")]
    [InlineData("𝟏𝟓𝟎𝟎")]
    public void ParsePrice_MaliciousInput_DoesNotThrowAndReturnsSafeValue(string input)
    {
        var act = () => ScraperHelpers.ParsePrice(input);
        act.Should().NotThrow();
        ScraperHelpers.ParsePrice(input).Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void ParsePrice_NegativeSignPresent_IgnoresSignAndParses()
    {
        ScraperHelpers.ParsePrice("-1500").Should().Be(1500);
    }

    [Fact]
    public void ParsePrice_ExtremelyLongString_DoesNotThrow()
    {
        var input = new string('9', 10_000);
        var act = () => ScraperHelpers.ParsePrice(input);
        act.Should().NotThrow();
    }
}
