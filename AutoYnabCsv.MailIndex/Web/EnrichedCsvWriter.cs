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

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return banks.Select(bank => WriteOne(directory, bank, correlations, taken)).ToArray();
    }

    private static string WriteOne(
        string directory,
        BankSource bank,
        IReadOnlyList<Match> correlations,
        ISet<string> taken
    )
    {
        var path = Path.Combine(directory, UniqueName(bank.File, taken));

        var rows = bank
            .Entries.Select(entry => ImportRow(entry, MailsFor(entry, correlations)))
            .ToArray();

        File.WriteAllText(path, YnabCsvExporter.Export(rows), Encoding.UTF8);
        return path;
    }

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

    private static YnabImportEntry ImportRow(CorrelationEntry entry, IReadOnlyList<MailFacts> mails)
    {
        var enriched = EntryEnricher.Of(entry, mails);

        return new YnabImportEntry(
            entry.Date,
            enriched.Payee,
            enriched.Memo,
            entry.IsInflow ? entry.Amount : 0,
            entry.IsInflow ? 0 : entry.Amount
        );
    }

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
                .ToArray();
        }

        var best = forTheEntry[0];
        var equallyEvidenced = forTheEntry.Count(candidate =>
            candidate.Evidence == best.Evidence && candidate.DayDistance == best.DayDistance
        );

        return equallyEvidenced > 1 ? [] : [best.Mail];
    }
}
