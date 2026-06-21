using System.Text;

namespace IWEHZ.Infrastructure.Markdown;

internal static class MarkdownHelper
{
    private static readonly HashSet<char> SpecialChars =
    [
        '_', '*', '[', ']', '(', ')', '~', '`', '>',
        '#', '+', '-', '=', '|', '{', '}', '.', '!', '\\'
    ];

    internal static string EscapeV2(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder(text.Length * 2);
        foreach (var ch in text)
        {
            if (SpecialChars.Contains(ch))
                sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
