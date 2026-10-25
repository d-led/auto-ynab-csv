namespace AutoYnabCsv.Contracts;

/// <summary>
/// YNAB calls the payee of a transfer <c>Transfer : &lt;account&gt;</c>. A row imported under that
/// name reads as the transfer it is instead of as a payment to a person, and the memo keeps the
/// number the export printed, so two accounts with a similar name cannot be confused for each other.
/// </summary>
public static class TransferNaming
{
    public const string Prefix = "Transfer : ";

    public static string Payee(string payee, string counterpartyIban, OwnAccountBook own) =>
        own.NameOf(counterpartyIban) is { } account ? Prefix + account : payee;

    public static string Memo(string memo, string counterpartyIban, OwnAccountBook own) =>
        own.NameOf(counterpartyIban) is null
            ? memo
            : string.IsNullOrWhiteSpace(memo)
                ? Hint(counterpartyIban)
                : $"{memo} · {Hint(counterpartyIban)}";

    private static string Hint(string counterpartyIban) =>
        $"…{new string([.. Digits(counterpartyIban).TakeLast(4)])}";

    /// <summary>
    /// The same payee for a row that is not a transfer. The API refuses a payee starting with one of its
    /// own internal names, so the account the money came from is named without the prefix.
    /// </summary>
    public static string WithoutPrefix(string payee) =>
        payee.StartsWith(Prefix, StringComparison.Ordinal) ? payee[Prefix.Length..] : payee;

    private static string Digits(string iban) =>
        new([.. iban.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);
}
