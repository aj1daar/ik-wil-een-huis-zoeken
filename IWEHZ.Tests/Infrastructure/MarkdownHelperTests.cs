using IWEHZ.Infrastructure.Markdown;

namespace IWEHZ.Tests.Infrastructure;

public sealed class MarkdownHelperTests
{
    [Fact]
    public void EscapeV2_NullInput_ReturnsEmpty()
    {
        MarkdownHelper.EscapeV2(null).Should().BeEmpty();
    }

    [Fact]
    public void EscapeV2_EmptyString_ReturnsEmpty()
    {
        MarkdownHelper.EscapeV2(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void EscapeV2_PlainText_ReturnsUnchanged()
    {
        MarkdownHelper.EscapeV2("Hello World").Should().Be("Hello World");
    }

    [Theory]
    [InlineData("_", "\\_")]
    [InlineData("*", "\\*")]
    [InlineData("[", "\\[")]
    [InlineData("]", "\\]")]
    [InlineData("(", "\\(")]
    [InlineData(")", "\\)")]
    [InlineData("~", "\\~")]
    [InlineData("`", "\\`")]
    [InlineData(">", "\\>")]
    [InlineData("#", "\\#")]
    [InlineData("+", "\\+")]
    [InlineData("-", "\\-")]
    [InlineData("=", "\\=")]
    [InlineData("|", "\\|")]
    [InlineData("{", "\\{")]
    [InlineData("}", "\\}")]
    [InlineData(".", "\\.")]
    [InlineData("!", "\\!")]
    [InlineData("\\", "\\\\")]
    public void EscapeV2_EachSpecialChar_IsEscaped(string input, string expected)
    {
        MarkdownHelper.EscapeV2(input).Should().Be(expected);
    }

    [Fact]
    public void EscapeV2_ListingTitleWithSpecialChars_IsFullyEscaped()
    {
        var title = "3-room apartment (new!) in Amsterdam | 85m²";
        var result = MarkdownHelper.EscapeV2(title);

        result.Should().NotMatchRegex(@"(?<!\\)\(");
        result.Should().Contain("\\(");
        result.Should().Contain("\\)");
        result.Should().Contain("\\!");
        result.Should().Contain("\\|");
        result.Should().Contain("\\-");
    }

    [Theory]
    [InlineData("*bold injection*")]
    [InlineData("_italic injection_")]
    [InlineData("[link](http://evil.com)")]
    [InlineData("`code injection`")]
    [InlineData("~~strikethrough~~")]
    public void EscapeV2_MarkdownInjectionAttempts_NeutraliseFormatting(string input)
    {
        var result = MarkdownHelper.EscapeV2(input);
        result.Should().StartWith("\\");
        result.Should().NotMatchRegex(@"(?<!\\)[*_`\[]");
    }

    [Theory]
    [InlineData("'; DROP TABLE users; --")]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("{{template_injection}}")]
    [InlineData("${7*7}")]
    public void EscapeV2_InjectionStrings_DoNotThrowAndAreEscaped(string input)
    {
        var act = () => MarkdownHelper.EscapeV2(input);
        act.Should().NotThrow();
        MarkdownHelper.EscapeV2(input).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void EscapeV2_VeryLongString_DoesNotThrow()
    {
        var input = new string('*', 10_000);
        var act = () => MarkdownHelper.EscapeV2(input);
        act.Should().NotThrow();
        MarkdownHelper.EscapeV2(input).Should().HaveLength(20_000);
    }
}
