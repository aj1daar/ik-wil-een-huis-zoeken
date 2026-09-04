using IWEHZ.Scrapers;

namespace IWEHZ.Tests.Scrapers;

public sealed class KamernetScraperTests
{
    [Fact]
    public void ParsePriceFromTitle_RealShape_ExtractsRentNotSquareMeters()
    {
        // Real alt text captured live: the card's own text only has "80 m²", the rent
        // only shows up here — ParsePrice on the card text used to return 80.
        const string title = "Appartement te huur 1350 euro Zwaanshals";

        KamernetScraper.ParsePriceFromTitle(title).Should().Be(1350);
    }

    [Fact]
    public void ParsePriceFromTitle_ThousandSeparator_ParsesCorrectly()
    {
        const string title = "Appartement te huur 1.750 euro Kerkstraat";

        KamernetScraper.ParsePriceFromTitle(title).Should().Be(1750);
    }

    [Fact]
    public void ParsePriceFromTitle_NoEuroFigure_ReturnsZero()
    {
        const string title = "Huurwoning amsterdam";

        KamernetScraper.ParsePriceFromTitle(title).Should().Be(0);
    }
}
