using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Enrichment;

namespace AutoYnabCsv.MailIndex.Web;

/// <summary>
/// What a detected transfer is called on each of the two rows it joins. Money leaves one exported
/// account and arrives in another, so a row carries the payee of the other side of the crossing —
/// <c>Transfer : Girokonto · 9004</c> — which is the payee YNAB itself gives a transfer, so the
/// import lands as a transfer instead of a payment to a person.
/// </summary>
public static class TransferNames
{
    /// <summary>The name of the row, by entry index; rows no transfer was found for are absent.</summary>
    public static IReadOnlyDictionary<int, string> Of(IReadOnlyList<BankSource> banks)
    {
        var entries = EntriesOf(banks);
        var accountOf = entries.ToDictionary(
            entry => entry.Entry.Index,
            entry => AccountLabel.Of(entry.File)
        );

        var names = new Dictionary<int, string>();

        foreach (var pair in TransferDetector.Find(entries))
        {
            names[pair.Outgoing.Index] = $"Transfer : {accountOf[pair.Incoming.Index]}";
            names[pair.Incoming.Index] = $"Transfer : {accountOf[pair.Outgoing.Index]}";
        }

        return names;
    }

    private static (string File, CorrelationEntry Entry)[] EntriesOf(
        IReadOnlyList<BankSource> banks
    ) =>
        banks.SelectMany(bank => bank.Entries.Select(entry => (bank.File, entry))).ToArray();
}
