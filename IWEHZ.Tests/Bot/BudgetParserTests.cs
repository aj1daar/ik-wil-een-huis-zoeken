using IWEHZ.Bot;

namespace IWEHZ.Tests.Bot;

public sealed class BudgetParserTests
{
    [Theory]
    [InlineData("€1000", 1000)]
    [InlineData("1500", 1500)]
    [InlineData("€2500", 2500)]
    [InlineData("€ 3000", 3000)]
    [InlineData("10000", 10000)]
    [InlineData("100", 100)]
    public void TryParse_ValidBudgets_ReturnsTrueWithCorrectValue(string input, decimal expected)
    {
        BudgetParser.TryParse(input, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    [InlineData("10001")]
    [InlineData("999999")]
    public void TryParse_OutOfRangeBudget_ReturnsFalse(string input)
    {
        BudgetParser.TryParse(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("€€€")]
    [InlineData("one thousand")]
    public void TryParse_NonNumericInput_ReturnsFalse(string? input)
    {
        BudgetParser.TryParse(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("'; DROP TABLE users; --")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("1; SELECT * FROM users")]
    public void TryParse_MaliciousInput_ReturnsFalseWithoutThrowing(string input)
    {
        var act = () => BudgetParser.TryParse(input, out _);
        act.Should().NotThrow();
        BudgetParser.TryParse(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("1 OR 1=1", 111)]
    [InlineData("1e10", 110)]
    public void TryParse_SqlLookingInputWithEmbeddedDigits_ExtractsDigitsOnly(string input, decimal expected)
    {
        BudgetParser.TryParse(input, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Fact]
    public void TryParse_InputExceeding20Chars_ReturnsFalse()
    {
        var input = new string('1', 21);
        BudgetParser.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void Min_Is100()
    {
        BudgetParser.Min.Should().Be(100m);
    }

    [Fact]
    public void Max_Is10000()
    {
        BudgetParser.Max.Should().Be(10_000m);
    }
}
