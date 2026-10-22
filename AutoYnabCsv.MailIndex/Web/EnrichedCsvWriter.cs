using System.Text;
using AutoYnabCsv.Contracts;
using AutoYnabCsv.Exporters;
using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Enrichment;
using AutoYnabCsv.MailIndex.Facts;

namespace AutoYnabCsv.MailIndex.Web;

/// <summary>
/// Writes the files to import: one YNAB CSV per source export, in the same run folder as the
/// report, so what was reviewed on the page is exactly what reaches YNAB.
/// </summary>
public static class EnrichedCsvWriter
{
    public const string DirectoryName = "ynab";
    public const string Extension = ".ynab.csv";

    public static IReadOnlyList<string> Write(
        string runDirectory,
        IReadOnlyList<BankSource> banks,
        IReadOnlyList<Match> correlations
    )
    {
        var directory = Path.Combine(runDirectory, DirectoryName);
        Directory.CreateDirectory(directory);

        var transfers = TransferNames.Of(banks);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return banks
            .Select(bank =>
                WriteOne(directory, bank, RowsOf(bank, correlations, transfers), taken)
            )
            .ToArray();
    }

    private static string WriteOne(
        string directory,
        BankSource bank,
        IReadOnlyList<YnabImportEntry> rows,
        ISet<string> taken
    )
    {
        var path = Path.Combine(directory, UniqueName(bank.File, taken));

        File.WriteAllText(path, YnabCsvExporter.Export(rows), Encoding.UTF8);
        return path;
    }

    private static IReadOnlyList<YnabImportEntry> RowsOf(
        BankSource bank,
        IReadOnlyList<Match> correlations,
        IReadOnlyDictionary<int, string> transfers
    ) =>
        bank
            .Entries.Select(entry =>
                ImportRow(
                    entry,
                    MailsFor(entry, correlations),
                    transfers.GetValueOrDefault(entry.Index)
                )
            )
            .ToArray();

    private static string UniqueName(string sourceFile, ISet<string> taken)
    {
        var stem = Path.GetFileNameWithoutExtension(sourceFile);
        var name = stem + Extension;

        for (var copy = 2; !taken.Add(name); copy++)
        {
            name = $"{stem}-{copy}{Extension}";
        }

        return name;
    }

    /// <summary>
    /// A row of your own money moving between your accounts is named after the account it crossed
    /// to, not after whoever the bank happened to print: there is no merchant to find.
    /// </summary>
    private static YnabImportEntry ImportRow(
        CorrelationEntry entry,
        IReadOnlyList<MailFacts> mails,
        string? transfer
    )
    {
        var enriched = EntryEnricher.Of(entry, mails);

        return new YnabImportEntry(
            entry.Date,
            transfer ?? enriched.Payee,
            enriched.Memo,
            entry.IsInflow ? entry.Amount : 0,
            entry.IsInflow ? 0 : entry.Amount
        );
    }

    /// <summary>
    /// How much a mail knows about the purchase. The confirmation that lists the goods and what
    /// they cost stands above the dispatch and delivery notices, which mostly repeat the subject —
    /// so the titles a memo lists are the whole ones and not the shop's shortened copy.
    /// </summary>
    private static int RichnessOf(MailFacts mail) =>
        (mail.Fields.ContainsKey("total") ? 2 : 0) + mail.Items.Count;

    /// <summary>
    /// The mail the row is built from. A number the row quotes is quoted back by every mail about
    /// that purchase — the confirmation lists the goods, the delivery note says they arrived — so
    /// they add up instead of competing. Two mails that are evidenced equally well by amount alone
    /// mean the row is not decided: nothing is put on it, and the report is where it gets looked at.
    /// </summary>
    private static IReadOnlyList<MailFacts> MailsFor(
        CorrelationEntry entry,
        IReadOnlyList<Match> correlations
    )
    {
        var forTheEntry = correlations
            .Where(match => match.Entry.Index == entry.Index)
            .ByEvidence()
            .ToArray();

        if (forTheEntry.Length == 0)
        {
            return [];
        }

        if (forTheEntry[0].Evidence.Kind == EvidenceKind.Reference)
        {
            return forTheEntry
                .Where(match => match.Evidence.Kind == EvidenceKind.Reference)
                .Select(match => match.Mail)
                .DistinctBy(mail => mail.Mail.Path)
                .OrderByDescending(RichnessOf)
                .ToArray();
        }

        var best = forTheEntry[0];
        var equallyEvidenced = forTheEntry.Count(candidate =>
            candidate.Evidence == best.Evidence && candidate.DayDistance == best.DayDistance
        );

        return equallyEvidenced > 1 ? [] : [best.Mail];
    }
}
