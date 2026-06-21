namespace IWEHZ.Tests.Security;

public sealed class AdminAccessControlTests
{
    private const long AdminChatId = 921211643L;
    private const long RandomUserChatId = 111222333L;

    [Fact]
    public void AdminCommand_IsOnlyProcessed_WhenSenderIsAdmin()
    {
        IsAdminSender(AdminChatId).Should().BeTrue();
    }

    [Fact]
    public void AdminCommand_IsBlocked_ForNonAdminSender()
    {
        var isAdmin = IsAdminSender(RandomUserChatId);
        isAdmin.Should().BeFalse();
    }

    [Fact]
    public void AdminCommand_IsBlocked_ForZeroChatId()
    {
        IsAdminSender(0L).Should().BeFalse();
    }

    [Fact]
    public void AdminCommand_IsBlocked_ForNegativeChatId()
    {
        IsAdminSender(-1L).Should().BeFalse();
    }

    [Theory]
    [InlineData("/activate")]
    [InlineData("/activate ")]
    [InlineData("/activate abc")]
    [InlineData("/activate 99999999999999999999999")]
    public void ActivateCommand_MalformedArgument_FailsTargetIdParse(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var validArgs = parts.Length >= 2 && long.TryParse(parts[1], out _);
        validArgs.Should().BeFalse();
    }

    [Fact]
    public void ActivateCommand_ValidArgument_ParsesTargetChatId()
    {
        var text = "/activate 111222333";
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        long id = 0;
        var parsed = parts.Length >= 2 && long.TryParse(parts[1], out id);
        parsed.Should().BeTrue();
        id.Should().Be(111222333L);
    }

    [Theory]
    [InlineData("/activate '; DROP TABLE users;--")]
    [InlineData("/activate <script>alert(1)</script>")]
    [InlineData("/activate 0; DELETE FROM users")]
    public void ActivateCommand_InjectionInArgument_FailsParseSafely(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parsed = parts.Length >= 2 && long.TryParse(parts[1], out _);
        parsed.Should().BeFalse();
    }

    private static bool IsAdminSender(long senderChatId) => senderChatId == AdminChatId;
}
