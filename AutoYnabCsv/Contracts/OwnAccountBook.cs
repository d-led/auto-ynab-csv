namespace AutoYnabCsv.Contracts;

/// <summary>
/// Your own accounts, by the number an export prints for them. A movement whose other side is one of
/// these is not a payment to a stranger but a transfer between two of your accounts, and saying so
/// is what lets YNAB read it as one.
/// </summary>
public sealed class OwnAccountBook
{
    public static readonly OwnAccountBook None = new([]);

    private readonly Dictionary<string, string> _nameByDigits;

    public OwnAccountBook(IEnumerable<(string Iban, string Name)> accounts) =>
        _nameByDigits = accounts
            .GroupBy(account => Digits(account.Iban), StringComparer.Ordinal)
            .Where(group => group.Key.Length >= 4)
            .ToDictionary(group => group.Key, group => group.Last().Name, StringComparer.Ordinal);

    /// <summary>The name of the account whose number this is, or null when it is somebody else's.</summary>
    public string? NameOf(string counterpartyIban) =>
        _nameByDigits.TryGetValue(Digits(counterpartyIban), out var name) ? name : null;

    /// <summary>An account number prints the same digits whether or not it is written in groups.</summary>
    private static string Digits(string iban) =>
        new([.. iban.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);
}
