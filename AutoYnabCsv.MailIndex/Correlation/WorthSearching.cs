using AutoYnabCsv.MailIndex.Enrichment;
using AutoYnabCsv.MailIndex.Extraction;

namespace AutoYnabCsv.MailIndex.Correlation;

/// <summary>
/// Which rows are worth a mailbox search. Downloading a body is the slowest thing a run does, so
/// it is reserved for the rows that say nothing a person could categorize from: a processor or a
/// bare shop named in the configuration, or a row whose merchant the bank masked away. Money that
/// merely moved — a withdrawal, a deposit, a card settlement — is never one of them.
/// </summary>
public static class WorthSearching
{
    public static IReadOnlyList<CorrelationEntry> Of(
        IEnumerable<CorrelationEntry> entries,
        ExtractionRuleSet rules
    ) => entries.Where(entry => NeedsMail(entry, rules)).ToArray();

    private static bool NeedsMail(CorrelationEntry entry, ExtractionRuleSet rules) =>
        !rules.IsTransfer(entry.Payee, entry.Memo)
        && (rules.NeedsMail(entry.Payee) || NamesNoMerchant(entry));

    /// <summary>A row of dots, or of nothing at all, reveals no merchant whatever its payee says.</summary>
    private static bool NamesNoMerchant(CorrelationEntry entry) =>
        !EntryPatterns.Of(entry.Payee, entry.Memo).Merchant.Any(char.IsLetter);
}
