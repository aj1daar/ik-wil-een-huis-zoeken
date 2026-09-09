using IWEHZ.Services;

namespace IWEHZ.Tests.Services;

public sealed class ListingAvailabilityVerifierTests
{
    [Fact]
    public void LooksRented_StatusBadgeSaysVerhuurd_ReturnsTrueWithMarker()
    {
        const string html = """
            <html><head><title>Keizersgracht 12 - huurwoning</title></head>
            <body><h1>Keizersgracht 12</h1>
            <span class="listing-status-label">Verhuurd</span>
            <p>Prachtig appartement aan de gracht.</p></body></html>
            """;

        ListingAvailabilityVerifier.LooksRented(html, out var marker).Should().BeTrue();
        marker.Should().BeEquivalentTo("Verhuurd");
    }

    [Fact]
    public void LooksRented_MarkerInPageTitle_ReturnsTrue() =>
        ListingAvailabilityVerifier.LooksRented(
            "<html><head><title>Keizersgracht 12 [VERHUURD]</title></head><body></body></html>", out _)
            .Should().BeTrue();

    [Fact]
    public void LooksRented_MarkerInHeading_ReturnsTrue() =>
        ListingAvailabilityVerifier.LooksRented(
            "<html><body><h1>Keizersgracht 12 - onder optie</h1></body></html>", out _)
            .Should().BeTrue();

    [Fact]
    public void LooksRented_AvailableListing_ReturnsFalse()
    {
        const string html = """
            <html><head><title>Keizersgracht 12 - huurwoning Amsterdam</title></head>
            <body><h1>Keizersgracht 12</h1>
            <span class="listing-status-label">Beschikbaar</span>
            <p>Aangeboden door verhuurmakelaar Jansen.</p></body></html>
            """;

        ListingAvailabilityVerifier.LooksRented(html, out _).Should().BeFalse();
    }

    [Fact]
    public void LooksRented_SidebarFilterLink_IsIgnored()
    {
        // Every listing page carries a "Verhuurd aanbod" nav link — scanning the whole page
        // would flag every single listing as rented.
        const string html = """
            <html><head><title>Keizersgracht 12 - huurwoning</title></head>
            <body><h1>Keizersgracht 12</h1>
            <nav class="site-menu"><a href="/verhuurd">Verhuurd aanbod</a></nav>
            <p>Beschikbaar per 1 oktober.</p></body></html>
            """;

        ListingAvailabilityVerifier.LooksRented(html, out _).Should().BeFalse();
    }

    [Fact]
    public void LooksRented_EmptyHtml_ReturnsFalse() =>
        ListingAvailabilityVerifier.LooksRented("", out _).Should().BeFalse();
}
