using System.Globalization;
using System.Text;

namespace AutoYnabCsv.MailIndex.Mail;

/// <summary>
/// The raw RFC 822 message of an Apple Mail <c>.emlx</c> file, with the receipt date
/// Apple records next to it.
/// </summary>
public sealed record EmlxMessage(string Path, byte[] RawMessage, DateTimeOffset? ReceivedAt);

/// <summary>What was wrong with a file, as a stable code a report can group by.</summary>
public enum EmlxProblem
{
    NoLengthLine,
    UnreadableLengthLine,
    LengthExceedsFile,
    NoPayload,
}

public sealed class EmlxFormatException(EmlxProblem problem, string message) : Exception(message)
{
    public EmlxProblem Problem => problem;
}

/// <summary>
/// Reads Apple Mail's <c>.emlx</c> container: a byte count on the first line, that many
/// bytes of RFC 822 message, then a property list with the message's metadata. The count is
/// trusted when it fits; when it does not (it is a hint, and Mail writes it inconsistently),
/// the message is taken up to the property list that ends the container.
/// </summary>
public static class EmlxReader
{
    private const int MaxLengthLineBytes = 32;

    public static EmlxMessage Read(string path) => Read(File.ReadAllBytes(path), path);

    public static EmlxMessage Read(ReadOnlySpan<byte> file, string path = "")
    {
        var (declared, payloadStart) = ParseLengthLine(file);
        var length = PayloadLength(file[payloadStart..], declared, path);
        var rawMessage = file.Slice(payloadStart, length).ToArray();
        var receivedAt = ReadDateReceived(file[(payloadStart + length)..]);

        return new EmlxMessage(path, rawMessage, receivedAt);
    }

    private static int PayloadLength(ReadOnlySpan<byte> rest, int? declared, string path)
    {
        var untilTrailer = LengthBeforeTrailer(rest);
        if (untilTrailer > 0)
        {
            return untilTrailer;
        }

        if (declared is { } length && length > 0 && length <= rest.Length)
        {
            return length;
        }

        throw new EmlxFormatException(
            declared is null ? EmlxProblem.NoLengthLine : EmlxProblem.LengthExceedsFile,
            declared is null
                ? $"{Path.GetFileName(path)} has no length line and no trailer, so it is not an .emlx file."
                : $"{Path.GetFileName(path)} declares {declared} bytes but holds {rest.Length}."
        );
    }

    /// <summary>Everything before the property list Mail appends after the message.</summary>
    private static int LengthBeforeTrailer(ReadOnlySpan<byte> rest)
    {
        var text = Encoding.Latin1.GetString(rest);
        var start = text.LastIndexOf("<?xml", StringComparison.Ordinal);
        if (start < 0)
        {
            start = text.LastIndexOf("bplist", StringComparison.Ordinal);
        }

        return start <= 0 ? -1 : text[..start].TrimEnd().Length;
    }

    private static (int? Length, int PayloadStart) ParseLengthLine(ReadOnlySpan<byte> file)
    {
        var searchLength = Math.Min(file.Length, MaxLengthLineBytes);
        var lineEnd = file[..searchLength].IndexOf((byte)'\n');
        if (lineEnd <= 0)
        {
            return (null, 0);
        }

        var line = file[..lineEnd];
        if (line[^1] == (byte)'\r')
        {
            line = line[..^1];
        }

        var parsed = int.TryParse(
            Encoding.ASCII.GetString(line),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var length
        );

        return (parsed ? length : null, lineEnd + 1);
    }

    private static DateTimeOffset? ReadDateReceived(ReadOnlySpan<byte> trailer)
    {
        var text = Encoding.ASCII.GetString(trailer);
        var keyIndex = text.IndexOf("<key>date-received</key>", StringComparison.Ordinal);
        if (keyIndex < 0)
        {
            return null;
        }

        var integerIndex = text.IndexOf("<integer>", keyIndex, StringComparison.Ordinal);
        if (integerIndex < 0)
        {
            return null;
        }

        var digitsStart = integerIndex + "<integer>".Length;
        var digitsEnd = text.IndexOf('<', digitsStart);
        if (digitsEnd < 0)
        {
            return null;
        }

        return long.TryParse(
            text.AsSpan(digitsStart, digitsEnd - digitsStart),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var seconds
        )
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }
}
