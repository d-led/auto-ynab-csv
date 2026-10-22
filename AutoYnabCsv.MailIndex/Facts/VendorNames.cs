namespace AutoYnabCsv.MailIndex.Facts;

/// <summary>
/// The name a sender domain is shown under. The shipped pairs are data in <c>patterns.jsonc</c>, so
/// a user file adds a shop the same way it adds a rule.
/// </summary>
public sealed class VendorNames
{
    private readonly Dictionary<string, string> _names;

    public VendorNames(IEnumerable<KeyValuePair<string, string>>? names = null) =>
        _names = new(names ?? [], StringComparer.OrdinalIgnoreCase);

    public static VendorNames Empty { get; } = new();

    /// <summary>The domain to name pairs as configured; unsorted, so callers order them.</summary>
    public IReadOnlyDictionary<string, string> Names => _names;

    /// <summary>This map with <paramref name="more"/> laid over it; a later name for a domain wins.</summary>
    public VendorNames Over(IEnumerable<KeyValuePair<string, string>> more)
    {
        var merged = new Dictionary<string, string>(_names, StringComparer.OrdinalIgnoreCase);

        foreach (var (domain, name) in more)
        {
            merged[domain] = name;
        }

        return new(merged);
    }

    /// <summary>The vendor a sender belongs to, or the domain when no name is configured.</summary>
    public string Of(string address)
    {
        var domain = DomainOf(address);
        if (domain.Length == 0)
        {
            return string.Empty;
        }

        var known = _names
            .Keys.Where(key =>
                domain.Equals(key, StringComparison.OrdinalIgnoreCase)
                || domain.EndsWith($".{key}", StringComparison.OrdinalIgnoreCase)
            )
            .OrderByDescending(key => key.Length)
            .FirstOrDefault();

        return known is null ? domain : _names[known];
    }

    /// <summary>The mail domain of an address, lower-cased; empty when there is no <c>@</c>.</summary>
    public static string DomainOf(string address)
    {
        var at = address.LastIndexOf('@');
        return at < 0 || at == address.Length - 1
            ? string.Empty
            : address[(at + 1)..].ToLowerInvariant();
    }
}
