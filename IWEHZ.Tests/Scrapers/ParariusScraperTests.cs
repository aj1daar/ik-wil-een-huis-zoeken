using IWEHZ.Scrapers;

namespace IWEHZ.Tests.Scrapers;

public sealed class ParariusScraperTests
{
    [Fact]
    public void ExtractCity_PostcodeAndNeighbourhood_ReturnsBareCity()
    {
        ParariusScraper.ExtractCity("1102 RR Amsterdam (Amsterdamse Poort e.o.)")
            .Should().Be("Amsterdam");
    }

    [Fact]
    public void ExtractCity_MultiWordCity_ReturnsFullCityName()
    {
        // Sub-title neighbourhood can itself contain a comma — the city must stop at "Den Haag".
        ParariusScraper.ExtractCity("2594 AC Den Haag (Bezuidenhout-West)")
            .Should().Be("Den Haag");

        ParariusScraper.ExtractCity("3511 BH Utrecht (Lange Elisabethstraat, Mariaplaats en omgeving)")
            .Should().Be("Utrecht");
    }

    [Fact]
    public void ExtractCity_NoNeighbourhoodSuffix_ReturnsCityAfterPostcode()
    {
        ParariusScraper.ExtractCity("3063 BX Rotterdam").Should().Be("Rotterdam");
    }

    [Fact]
    public void ExtractCity_NoPostcodePrefix_FallsBackToWholeString()
    {
        ParariusScraper.ExtractCity("Amsterdam").Should().Be("Amsterdam");
    }
}
