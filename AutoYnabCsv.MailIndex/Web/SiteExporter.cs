using System.Globalization;
using System.Text.Json;
using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Enrichment;
using AutoYnabCsv.MailIndex.Facts;

namespace AutoYnabCsv.MailIndex.Web;

/// <summary>Money that moved one way between two exported accounts.</summary>
public sealed record SiteFlow(string From, string To, decimal Total, int Count);

/// <summary>One report run, in its own timestamped folder.</summary>
public sealed record SiteExport(
    string Directory,
    string HtmlPath,
    string JsonPath,
    string AuditPath,
    IReadOnlyList<string> CsvPaths,
    int EvidenceMails
);

/// <summary>
/// Writes a self-contained searchable page plus the JSON index behind it. The page needs no
/// server, no network and no build step: open <c>index.html</c> and type.
/// </summary>
public static class SiteExporter
{
    public const string JsonFileName = "mail-index.json";
    public const string HtmlFileName = "index.html";
    public const string AuditFileName = "audit.json";

    private const int MaxTextChars = 1_200;

    public static SiteExport Export(
        string baseDirectory,
        IReadOnlyList<MailFacts> mails,
        IReadOnlyList<BankSource> banks,
        SiteAudit audit
    )
    {
        var directory = RunDirectory(baseDirectory, audit.GeneratedAt);

        var entries = EntriesOf(banks);
        var correlations = Correlator.Match(
            entries.Select(pair => pair.Entry).ToArray(),
            mails
        );
        var evidence = Evidence(correlations, mails);
        var documents = Documents(evidence, entries, correlations);
        var flows = Flows(banks);
        var json = JsonSerializer.Serialize(
            new
            {
                generated = audit.GeneratedAt.ToString("o", CultureInfo.InvariantCulture),
                indexedThrough = audit.Mail.IndexedThrough?.ToString(
                    "o",
                    CultureInfo.InvariantCulture
                ),
                audit,
                documents,
                flows,
            },
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
            }
        );

        var jsonPath = Path.Combine(directory, JsonFileName);
        var htmlPath = Path.Combine(directory, HtmlFileName);
        var auditPath = Path.Combine(directory, AuditFileName);
        var csvPaths = EnrichedCsvWriter.Write(directory, banks, correlations);

        File.WriteAllText(jsonPath, json);
        File.WriteAllText(
            auditPath,
            JsonSerializer.Serialize(
                audit,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                }
            )
        );
        File.WriteAllText(
            htmlPath,
            SiteTemplate.Html(
                json,
                audit.GeneratedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                Coverage(audit.Mail),
                SiteTemplate.AuditHtml(audit, evidence.Count)
            )
        );

        return new SiteExport(directory, htmlPath, jsonPath, auditPath, csvPaths, evidence.Count);
    }

    /// <summary>The span the index reaches. Messages outside it were never seen, not found irrelevant.</summary>
    private static string Coverage(MailAudit mail) =>
        mail.IndexedFrom is null || mail.IndexedThrough is null
            ? "no message yet"
            : $"{Span(mail.IndexedFrom.Value)} … {Span(mail.IndexedThrough.Value)} ({mail.Messages} messages)";

    private static string Span(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>One folder per run, so two runs never overwrite each other's page.</summary>
    private static string RunDirectory(string baseDirectory, DateTimeOffset generatedAt)
    {
        var stamp = generatedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var directory = Path.Combine(baseDirectory, stamp);

        for (var copy = 2; Directory.Exists(directory); copy++)
        {
            directory = Path.Combine(baseDirectory, $"{stamp}-{copy}");
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// The mails the report carries: the ones a row was built from. The index can hold thousands
    /// of messages; this page is the evidence for what was imported, not a second mail archive.
    /// </summary>
    private static IReadOnlyList<MailFacts> Evidence(
        IReadOnlyList<Match> correlations,
        IReadOnlyList<MailFacts> mails
    )
    {
        var used = correlations
            .Select(match => match.Mail.Mail.Path)
            .ToHashSet(StringComparer.Ordinal);

        return mails.Where(mail => used.Contains(mail.Mail.Path)).ToArray();
    }

    private static (string File, CorrelationEntry Entry)[] EntriesOf(
        IReadOnlyList<BankSource> banks
    ) =>
        banks.SelectMany(bank => bank.Entries.Select(entry => (bank.File, entry))).ToArray();

    private static IReadOnlyList<SiteDocument> Documents(
        IReadOnlyList<MailFacts> mails,
        (string File, CorrelationEntry Entry)[] entries,
        IReadOnlyList<Match> correlations
    )
    {
        var transferNotes = TransferNotes(entries);
        var byEntry = correlations
            .GroupBy(match => match.Entry.Index)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<Match>)group.ToArray());

        var documents = new List<SiteDocument>(mails.Count + entries.Length);

        foreach (var mail in mails)
        {
            documents.Add(MailDocument(mail));
        }

        foreach (var (file, entry) in entries)
        {
            documents.Add(
                EntryDocument(
                    file,
                    entry,
                    byEntry.GetValueOrDefault(entry.Index) ?? [],
                    transferNotes.GetValueOrDefault(entry.Index)
                )
            );
        }

        return documents;
    }

    /// <summary>
    /// Internal transfers grouped per direction. A → B and B → A stay two separate flows; they are
    /// never netted, so a round trip reads as a round trip.
    /// </summary>
    public static IReadOnlyList<SiteFlow> Flows(IReadOnlyList<BankSource> banks)
    {
        var entries = banks
            .SelectMany(bank => bank.Entries.Select(entry => (File: bank.File, Entry: entry)))
            .ToArray();

        var sources = entries.ToDictionary(pair => pair.Entry.Index, pair => pair.File);

        return TransferDetector
            .Find(entries.Select(entry => (entry.File, entry.Entry)).ToArray())
            .GroupBy(pair => (
                From: AccountLabel.Of(sources[pair.Outgoing.Index]),
                To: AccountLabel.Of(sources[pair.Incoming.Index])
            ))
            .Select(group => new SiteFlow(
                group.Key.From,
                group.Key.To,
                group.Sum(pair => pair.Outgoing.Amount),
                group.Count()
            ))
            .OrderByDescending(flow => flow.Total)
            .ToArray();
    }

    private static IReadOnlyDictionary<int, string> TransferNotes(
        IReadOnlyList<(string File, CorrelationEntry Entry)> entries
    )
    {
        var sources = entries.ToDictionary(pair => pair.Entry.Index, pair => pair.File);
        var notes = new Dictionary<int, string>();
        var pairs = TransferDetector.Find(
            entries.Select(entry => (entry.File, entry.Entry)).ToArray()
        );

        foreach (var pair in pairs)
        {
            notes[pair.Outgoing.Index] =
                $"transfer to {Path.GetFileName(sources[pair.Incoming.Index])}";
            notes[pair.Incoming.Index] =
                $"transfer from {Path.GetFileName(sources[pair.Outgoing.Index])}";
        }

        return notes;
    }

    private static SiteDocument MailDocument(MailFacts facts) =>
        new(
            IdOf(facts.Mail.Path),
            "mail",
            Day(facts.Mail.ReceivedAt),
            facts.Mail.Subject,
            facts.Vendor,
            facts.Vendor,
            string.Join(' ', facts.OrderNumbers),
            string.Join(' ', facts.Amounts.Select(Amount)),
            string.Join(
                ' ',
                facts.Fields.Select(field => $"{field.Key}={field.Value}")
            ),
            Excerpt(facts.Mail.Text),
            facts.Mail.SenderAddress,
            [],
            MailLink.Of(facts.Mail.MessageId)
        );

    private static SiteDocument EntryDocument(
        string file,
        CorrelationEntry entry,
        IReadOnlyList<Match> correlations,
        string? transferNote
    )
    {
        var facts = EntryPatterns.Of(entry.Payee, entry.Memo);
        var forTheEntry = correlations.Where(match => match.Entry.Index == entry.Index);
        var best = forTheEntry.ByEvidence().FirstOrDefault();
        var related = forTheEntry
            .ByEvidence()
            .Select(match => IdOf(match.Mail.Mail.Path))
            .ToArray();

        return new SiteDocument(
            $"bank:{Path.GetFileNameWithoutExtension(file)}:{entry.Index}",
            "bank",
            entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            facts.Payee,
            entry.Payee,
            facts.Merchant,
            facts.OrderNumber,
            Amount(entry.Amount),
            transferNote ?? string.Empty,
            entry.Memo,
            Path.GetFileName(file),
            related,
            best is null ? string.Empty : MailLink.Of(best.Mail.Mail.MessageId)
        );
    }

    /// <summary>
    /// Bank entries keep their memo in <see cref="CorrelationEntry.Payee"/>'s companion only when
    /// the caller has it; the site shows the payee otherwise.
    /// </summary>
    public static string IdOf(string path) => $"mail:{path}";

    private static string Day(DateTimeOffset value) =>
        value == DateTimeOffset.MinValue ? string.Empty : value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Amount(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Excerpt(string text) =>
        text.Length <= MaxTextChars ? text : text[..MaxTextChars];
}
