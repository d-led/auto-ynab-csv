using System.Globalization;
using System.Text;

namespace Tests;

internal static class TestEmlx
{
    public static byte[] Build(string payload, long? receivedAt = null)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var trailer = receivedAt is null
            ? "<?xml version=\"1.0\" encoding=\"UTF-8\"?><plist version=\"1.0\"><dict></dict></plist>"
            : "<?xml version=\"1.0\" encoding=\"UTF-8\"?><plist version=\"1.0\"><dict>"
                + $"<key>date-received</key><integer>{receivedAt}</integer>"
                + "</dict></plist>";

        return Encoding
            .ASCII.GetBytes($"{payloadBytes.Length}\n")
            .Concat(payloadBytes)
            .Concat(Encoding.UTF8.GetBytes(trailer))
            .ToArray();
    }

    public static string Message(
        string subject,
        string from,
        string body,
        DateTimeOffset date,
        string messageId = "<test-1@example.com>"
    ) =>
        $"From: {from}\r\n"
        + "To: me@example.com\r\n"
        + $"Subject: {subject}\r\n"
        + $"Message-ID: {messageId}\r\n"
        + $"Date: {date.ToString("ddd, dd MMM yyyy HH:mm:ss '+0000'", CultureInfo.InvariantCulture)}\r\n"
        + "MIME-Version: 1.0\r\n"
        + "Content-Type: text/plain; charset=utf-8\r\n"
        + "\r\n"
        + body;
}
