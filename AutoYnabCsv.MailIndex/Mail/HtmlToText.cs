using System.Net;
using System.Text.RegularExpressions;

namespace AutoYnabCsv.MailIndex.Mail;

/// <summary>
/// The plain text of an HTML mail: scripts and styles dropped, tags turned into spaces, breaks
/// where a reader would see them kept, image descriptions carried over, entities decoded, runs of
/// whitespace collapsed. Enough for pattern matching, not a renderer.
/// </summary>
/// <remarks>
/// An HTML-only mail says where its lines are by which tags close them, so those closes are what
/// becomes a newline. Image descriptions are kept because a shop writes a product's whole title
/// there when the visible link beside it is cut short.
/// </remarks>
public static partial class HtmlToText
{
    public static string Convert(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var withoutScripts = ScriptOrStyle().Replace(html, " ");
        var withImages = ImageAlt().Replace(withoutScripts, " $1$2 ");
        var withBreaks = Break().Replace(withImages, "\n");
        var withoutTags = Tags().Replace(withBreaks, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);

        return Lines(decoded);
    }

    /// <summary>One line per line a reader would see, whitespace collapsed, no empty lines.</summary>
    private static string Lines(string decoded) =>
        string.Join(
            "\n",
            decoded
                .Split('\n')
                .Select(line => Spaces().Replace(line, " ").Trim())
                .Where(line => line.Length > 0)
        );

    [GeneratedRegex(
        "<(script|style)[^>]*>.*?</\\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline
    )]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(
        "<img\\b[^>]*\\balt\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline
    )]
    private static partial Regex ImageAlt();

    [GeneratedRegex(
        "<br\\b[^>]*>|<hr\\b[^>]*>|</?(?:p|div|tr|td|th|li|ul|ol|table|tbody|thead|h[1-6]|section|article|header|footer|blockquote|pre|title)\\b[^>]*>",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex Break();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex("\\s+")]
    private static partial Regex Spaces();
}
