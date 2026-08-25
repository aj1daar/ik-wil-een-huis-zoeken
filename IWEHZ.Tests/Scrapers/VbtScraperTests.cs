using AngleSharp;
using AngleSharp.Dom;
using IWEHZ.Scrapers;

namespace IWEHZ.Tests.Scrapers;

public sealed class VbtScraperTests
{
    // Trimmed to the markup the parser reads, but the structure and class names match
    // a real card captured live from vbtverhuurmakelaars.nl/woningen (SvelteKit SSR output).
    private const string SampleCardHtml = """
        <a href="/woning/amsterdam-fregelaan-55" class="property svelte-16bhc06">
          <div class="visual">
            <div class="visimage" style="background-image: url(/images/f6ac55-w300-s-fwj/fregelaan-55)"></div>
            <span class="status available svelte-16bhc06">Beschikbaar</span>
          </div>
          <div class="items">
            <div>Amsterdam</div>
            <span class="normal">Fregelaan 55</span>
            <div class="price">&euro; 1.713,-</div>
            <table>
              <tr><td>Soort object</td><td>Appartement</td></tr>
              <tr><td>Woonoppervlakte</td><td>83 m&sup2;</td></tr>
              <tr><td>Servicekosten</td><td>&euro; 50,- per maand</td></tr>
            </table>
            <button class="rounded button respond">reageer</button>
          </div>
        </a>
        """;

    private const string SampleCardHtmlNoType = """
        <a href="/woning/purmerend-rocamadour-86" class="property svelte-16bhc06">
          <div class="items">
            <div>Purmerend</div>
            <span class="normal">Rocamadour 86</span>
            <div class="price">&euro; 1.761,-</div>
            <table></table>
          </div>
        </a>
        """;

    private static async Task<IElement> ParseCardAsync(string html)
    {
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html));
        return document.QuerySelector("a.property")!;
    }

    [Fact]
    public async Task TryParseCard_RealCardMarkup_ParsesAllFields()
    {
        var card = await ParseCardAsync(SampleCardHtml);

        VbtScraper.TryParseCard(card, out var listing).Should().BeTrue();

        listing.ExternalId.Should().Be("amsterdam-fregelaan-55");
        listing.City.Should().Be("Amsterdam");
        listing.Title.Should().Be("Appartement Fregelaan 55");
        listing.Price.Should().Be(1713);
        listing.SourceUrl.Should().Be("https://vbtverhuurmakelaars.nl/woning/amsterdam-fregelaan-55");
        listing.Source.Should().Be("vbt");
    }

    [Fact]
    public async Task TryParseCard_MissingPropertyTypeRow_FallsBackToStreetTitle()
    {
        var card = await ParseCardAsync(SampleCardHtmlNoType);

        VbtScraper.TryParseCard(card, out var listing).Should().BeTrue();

        listing.ExternalId.Should().Be("purmerend-rocamadour-86");
        listing.City.Should().Be("Purmerend");
        listing.Title.Should().Be("Rocamadour 86");
        listing.Price.Should().Be(1761);
    }

    [Fact]
    public async Task TryParseCard_NoHref_ReturnsFalse()
    {
        var card = await ParseCardAsync("""<a class="property"><div class="items"><div>Utrecht</div></div></a>""");

        VbtScraper.TryParseCard(card, out _).Should().BeFalse();
    }

    [Fact]
    public async Task TryParseCard_NoPriceElement_ReturnsFalse()
    {
        var card = await ParseCardAsync("""
            <a href="/woning/utrecht-teststraat-1" class="property">
              <div class="items"><div>Utrecht</div><span class="normal">Teststraat 1</span></div>
            </a>
            """);

        VbtScraper.TryParseCard(card, out _).Should().BeFalse();
    }
}
