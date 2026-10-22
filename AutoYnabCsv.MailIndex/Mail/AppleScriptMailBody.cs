using System.Diagnostics;
using System.Text;

namespace AutoYnabCsv.MailIndex.Mail;

/// <summary>
/// Asks Apple Mail for a message's body through AppleScript. Reading <c>content</c> makes Mail
/// download the message, which is the only supported way to get at what is not on this Mac.
/// macOS asks once for permission to control Mail; a refusal is reported as "no body".
/// </summary>
public sealed class AppleScriptMailBody(int timeoutMilliseconds = 20_000) : IMailBodySource
{
    private readonly int _timeoutMilliseconds = timeoutMilliseconds;

    public string? BodyOf(MailRecord mail)
    {
        var id = mail.MessageId.Trim().TrimStart('<').TrimEnd('>');
        if (id.Length == 0)
        {
            return null;
        }

        try
        {
            var body = Run(Script(id));
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Mail's <c>message id</c> may or may not carry the angle brackets, so the lookup compares the
    /// bare id. A timeout keeps a slow mailbox from hanging the conversion.
    /// </summary>
    private static string Script(string id) =>
        $"""
        tell application "Mail"
            repeat with accountRef in accounts
                repeat with boxRef in mailboxes of accountRef
                    set hits to (messages of boxRef whose message id contains "{id}")
                    if (count of hits) > 0 then return content of item 1 of hits
                end repeat
            end repeat
        end tell
        return ""
        """;

    private string Run(string script)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("osascript")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
            },
        };

        process.StartInfo.ArgumentList.Add("-e");
        process.StartInfo.ArgumentList.Add(script);

        process.Start();

        if (!process.WaitForExit(_timeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            return string.Empty;
        }

        return process.StandardOutput.ReadToEnd();
    }
}
