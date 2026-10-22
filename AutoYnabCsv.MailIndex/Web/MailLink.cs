namespace AutoYnabCsv.MailIndex.Web;

/// <summary>
/// The <c>message://</c> URL Apple Mail registers, so a row on the page opens the mail it was
/// built from. Mail needs the Message-ID wrapped in angle brackets, percent-encoded because plain
/// ones break the URL; a parsed record has lost them already, so they are put back here.
/// </summary>
public static class MailLink
{
    public static string Of(string messageId)
    {
        var bare = messageId.Trim().TrimStart('<').TrimEnd('>');

        return bare.Length == 0 ? string.Empty : $"message://{Uri.EscapeDataString($"<{bare}>")}";
    }
}
