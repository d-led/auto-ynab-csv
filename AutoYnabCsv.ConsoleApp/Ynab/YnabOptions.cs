namespace AutoYnabCsv.App.Ynab;

/// <summary>The <c>--name value</c> pairs the ynab commands accept; a name may repeat.</summary>
internal sealed class YnabOptions
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);

    public static YnabOptions Parse(ReadOnlySpan<string> arguments)
    {
        var options = new YnabOptions();

        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var name = argument[2..];
            var value = index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? arguments[++index]
                : string.Empty;

            if (!options._values.TryGetValue(name, out var values))
            {
                values = [];
                options._values[name] = values;
            }

            values.Add(value);
        }

        return options;
    }

    public bool Has(string name) => _values.ContainsKey(name);

    public string? Get(string name) => _values.TryGetValue(name, out var values) ? values[^1] : null;

    public IReadOnlyList<string> All(string name) =>
        _values.TryGetValue(name, out var values) ? values : [];

    public string Required(string name) =>
        string.IsNullOrWhiteSpace(Get(name)) ? throw new ArgumentException($"--{name} is required") : Get(name)!;

    public string Or(string name, string fallback) => string.IsNullOrWhiteSpace(Get(name)) ? fallback : Get(name)!;
}
