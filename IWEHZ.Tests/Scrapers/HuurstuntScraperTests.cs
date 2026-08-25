using System.Text.Json;
using IWEHZ.Scrapers;

namespace IWEHZ.Tests.Scrapers;

public sealed class HuurstuntScraperTests
{
    // Trimmed to the fields the parser reads, but the shape (nesting, property names)
    // matches the real schema.org JSON-LD captured live from huurstunt.nl/huren/nederland.
    private const string SampleJson = """
        {
          "mainContentOfPage": {
            "about": {
              "mainEntity": {
                "itemListElement": [
                  {
                    "item": {
                      "url": "https://www.huurstunt.nl/appartement/huren/in/amsterdam/marcantilaan/feeHp",
                      "offers": { "price": 2750, "priceCurrency": "EUR" }
                    }
                  },
                  {
                    "item": {
                      "url": "https://www.huurstunt.nl/kamer/huren/in/den-haag/laan-van-meerdervoort/feT69",
                      "offers": { "price": 709, "priceCurrency": "EUR" }
                    }
                  }
                ]
              }
            }
          }
        }
        """;

    [Fact]
    public void ExtractListingElements_RealShape_ReturnsBothItems()
    {
        var items = HuurstuntScraper.ExtractListingElements(SampleJson).ToList();
        items.Should().HaveCount(2);
    }

    [Fact]
    public void TryParseListing_ApartmentListing_ParsesAllFields()
    {
        var item = HuurstuntScraper.ExtractListingElements(SampleJson).First();

        HuurstuntScraper.TryParseListing(item, out var listing).Should().BeTrue();

        listing.ExternalId.Should().Be("feeHp");
        listing.City.Should().Be("amsterdam");
        listing.Title.Should().Be("Appartement Marcantilaan");
        listing.Price.Should().Be(2750);
        listing.SourceUrl.Should().Be("https://www.huurstunt.nl/appartement/huren/in/amsterdam/marcantilaan/feeHp");
        listing.Source.Should().Be("huurstunt");
    }

    [Fact]
    public void TryParseListing_MultiWordCity_NormalisesDashesToSpaces()
    {
        var item = HuurstuntScraper.ExtractListingElements(SampleJson).Last();

        HuurstuntScraper.TryParseListing(item, out var listing).Should().BeTrue();

        listing.ExternalId.Should().Be("feT69");
        listing.City.Should().Be("den haag");
        listing.Title.Should().Be("Kamer Laan van meerdervoort");
        listing.Price.Should().Be(709);
    }

    [Fact]
    public void TryParseListing_ZeroPrice_ReturnsFalse()
    {
        var json = """{"url": "https://www.huurstunt.nl/kamer/huren/in/breda/kerkstraat/abc12", "offers": {"price": 0}}""";
        var item = JsonSerializer.Deserialize<JsonElement>(json);

        HuurstuntScraper.TryParseListing(item, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseListing_UnexpectedUrlShape_ReturnsFalse()
    {
        var json = """{"url": "https://www.huurstunt.nl/huren/nederland", "offers": {"price": 1000}}""";
        var item = JsonSerializer.Deserialize<JsonElement>(json);

        HuurstuntScraper.TryParseListing(item, out _).Should().BeFalse();
    }

    [Fact]
    public void ExtractListingElements_MalformedJson_ReturnsEmpty()
    {
        HuurstuntScraper.ExtractListingElements("{not json").Should().BeEmpty();
    }

    [Fact]
    public void ExtractListingElements_MissingItemListElement_ReturnsEmpty()
    {
        HuurstuntScraper.ExtractListingElements("""{"@type": "Organization"}""").Should().BeEmpty();
    }
}
