using System.Globalization;

namespace AutoYnabCsv.App.Mail;

/// <summary>The <c>--name value</c> pairs the mail commands accept; a name may repeat.</summary>
internal sealed class MailOptions
{
    private readonly IReadOnlyDictionary<string, List<string>> _values;

    private MailOptions(IReadOnlyDictionary<string, List<string>> values) => _values = values;

    public static MailOptions Parse(ReadOnlySpan<string> arguments)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = argument[2..];
            var followedByValue =
                index + 1 < arguments.Length
                && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal);

            if (!values.TryGetValue(name, out var list))
            {
                list = [];
                values[name] = list;
            }

            list.Add(followedByValue ? arguments[++index] : "true");
        }

        return new MailOptions(values);
    }

    public string? Value(string name) =>
        _values.TryGetValue(name, out var list) ? list[^1] : null;

    public IReadOnlyList<string> Values(string name) =>
        _values.TryGetValue(name, out var list) ? list : [];

    public bool Flag(string name) =>
        string.Equals(Value(name), "true", StringComparison.OrdinalIgnoreCase);

    public int Int(string name, int fallback) =>
        Value(name) is { } text
        && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    public DateTimeOffset? Since() =>
        Value("since") is { } text
        && DateOnly.TryParseExact(
            text,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date
        )
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue))
            : null;
}
