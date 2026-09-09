using IWEHZ.Scrapers;

namespace IWEHZ.Tests.Scrapers;

public sealed class RentedStatusDetectorTests
{
    [Theory]
    [InlineData("Verhuurd")]
    [InlineData("VERHUURD ONDER VOORBEHOUD")]
    [InlineData("Appartement Zwaanshals - verhuurd!")]
    [InlineData("Gereserveerd")]
    [InlineData("Deze woning is onder optie")]
    [InlineData("In optie")]
    [InlineData("Onder bod")]
    [InlineData("Vergeven")]
    [InlineData("Niet meer beschikbaar")]
    [InlineData("Niet langer beschikbaar")]
    [InlineData("Niet beschikbaar")]
    [InlineData("This property is rented out")]
    [InlineData("No longer available")]
    [InlineData("Let agreed")]
    [InlineData("Under offer")]
    public void IsRented_RentedWording_ReturnsTrue(string text) =>
        RentedStatusDetector.IsRented(text).Should().BeTrue();

    [Theory]
    [InlineData("Appartement te huur in Amsterdam")]
    [InlineData("Huurwoning Rotterdam")]
    // "verhuurder" / "verhuurmakelaar" sit on nearly every live card — word boundaries keep them out.
    [InlineData("Aangeboden door verhuurmakelaar Vesteda")]
    [InlineData("Contact opnemen met de verhuurder")]
    [InlineData("Direct beschikbaar")]
    [InlineData("Per direct beschikbaar, 3 kamers")]
    [InlineData("Nog niet verhuurd")]
    [InlineData("Deze woning is niet verhuurd")]
    [InlineData("")]
    [InlineData(null)]
    public void IsRented_AvailableWording_ReturnsFalse(string? text) =>
        RentedStatusDetector.IsRented(text).Should().BeFalse();

    [Theory]
    [InlineData("https://schema.org/SoldOut")]
    [InlineData("SoldOut")]
    [InlineData("http://schema.org/OutOfStock")]
    [InlineData("schema:Discontinued")]
    public void IsUnavailableSchemaValue_ClosedOffer_ReturnsTrue(string availability) =>
        RentedStatusDetector.IsUnavailableSchemaValue(availability).Should().BeTrue();

    [Theory]
    [InlineData("https://schema.org/InStock")]
    [InlineData("InStock")]
    [InlineData("PreOrder")]
    [InlineData(null)]
    public void IsUnavailableSchemaValue_OpenOffer_ReturnsFalse(string? availability) =>
        RentedStatusDetector.IsUnavailableSchemaValue(availability).Should().BeFalse();

    [Fact]
    public void TryMatch_ReportsMatchedMarker()
    {
        RentedStatusDetector.TryMatch("Status: Onder optie", out var marker).Should().BeTrue();
        marker.Should().BeEquivalentTo("Onder optie");
    }

    [Fact]
    public void IsRented_Listing_ChecksStatusTextSchemaValueAndTitle()
    {
        var live = new ScrapedListing("1", "Appartement Keizersgracht", "Amsterdam", 1500,
            "https://x/1", "pararius", StatusText: "Per direct beschikbaar");
        RentedStatusDetector.IsRented(live).Should().BeFalse();

        RentedStatusDetector.IsRented(live with { StatusText = "Verhuurd" }).Should().BeTrue();
        RentedStatusDetector.IsRented(live with { StatusText = "https://schema.org/SoldOut" }).Should().BeTrue();
        RentedStatusDetector.IsRented(live with { Title = "Appartement Keizersgracht (verhuurd)" }).Should().BeTrue();
    }
}
