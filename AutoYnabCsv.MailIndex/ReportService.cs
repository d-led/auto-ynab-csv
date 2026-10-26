using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Extraction;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Index;
using AutoYnabCsv.MailIndex.Mail;
using AutoYnabCsv.MailIndex.Web;

namespace AutoYnabCsv.MailIndex;

/// <summary>Everything a report run needs to know: the index, the rules, and where the mail is.</summary>
public sealed record ReportContext(
    MailIndexStore Index,
    string IndexPath,
    ExtractionRuleSet Rules,
    string ToolVersion,
    MailStore? Mailbox,
    ReportProgress Progress,
    IMailFinder? Finder = null,
    IMailBodySource? Bodies = null,
    bool AllowRebuild = true,
    bool FullReindex = false
)
{
    public string MailboxRoot => Mailbox?.Root ?? "unavailable";

    public int? MailboxVersion => Mailbox?.Version;

    /// <summary>
    /// Reads the mailbox location once; <paramref name="searchMail"/> adds the second pass over
    /// the stubs of messages the index cannot read, whose bodies Mail still holds.
    /// </summary>
    public static ReportContext For(
        MailIndexStore index,
        string indexPath,
        ExtractionRuleSet rules,
        string toolVersion,
        bool searchMail = false,
        string? mailboxRoot = null,
        ReportProgress? progress = null,
        bool allowRebuild = true,
        bool fullReindex = false
    )
    {
        var mailbox = ReadableMailbox(mailboxRoot);
        var search = searchMail && mailbox is not null;

        var hooks = progress ?? ReportProgress.Silent;

        return new ReportContext(
            index,
            indexPath,
            rules,
            toolVersion,
            mailbox,
            hooks,
            search
                ? new HeaderMailFinder(
                    mailbox!.Root,
                    path => MailSyncService.Read(path, rules),
                    hooks.Stubs
                )
                : null,
            search ? BodySource() : null,
            allowRebuild,
            fullReindex
        );
    }

    /// <summary>Only macOS can fetch a message body that was never downloaded.</summary>
    private static IMailBodySource? BodySource() =>
        OperatingSystem.IsMacOS() ? new AppleScriptMailBody() : null;

    private static MailStore? ReadableMailbox(string? root)
    {
        try
        {
            return MailStoreLocator.FindReadableStores(root).FirstOrDefault();
        }
        catch (MailStoreUnavailableException)
        {
            return null;
        }
    }
}

/// <summary>What a run wrote, so the caller can tell the user where to look.</summary>
public sealed record ReportRun(
    SiteExport Export,
    SiteAudit Audit,
    MailSyncReport? Sync,
    MailSearchResult Search
);

/// <summary>
/// The normal output of a conversion: the mail is brought up to date first, then the YNAB rows to
/// import, the searchable page to look at, and the audit trail that says what was read. One call,
/// so nothing has to be assembled by hand.
/// </summary>
public static class ReportService
{
    public const int MailLimit = 5_000;

    /// <summary>
    /// How far before the first exported row the store is read. A message can arrive a few days
    /// before or after the charge, and a file's write time is not exactly its received date.
    /// </summary>
    private const int ExportMarginDays = MailSyncService.OverlapDays;

    public static ReportRun Write(
        ReportContext context,
        IReadOnlyList<string> inputs,
        string outputDirectory
    )
    {
        var (banks, skipped) = BankSourceReader.Read(inputs);
        var range = MailRange.Of(banks.SelectMany(bank => bank.Entries));

        var sync = SyncMail(context, WindowFor(context, range));
        var (mails, search) = Mails(context, banks, range);

        var audit = new SiteAudit(
            DateTimeOffset.Now,
            context.ToolVersion,
            new MailAudit(
                context.IndexPath,
                context.MailboxRoot,
                context.MailboxVersion,
                context.Index.Count,
                context.Index.IndexedFrom,
                context.Index.IndexedThrough,
                context.Index.LastSyncedAt,
                context.Rules.Fingerprint
            ),
            banks
                .Select(bank => new BankAudit(bank.File, bank.Detected, bank.Entries.Count))
                .ToArray(),
            skipped
        );

        return new ReportRun(
            SiteExporter.Export(outputDirectory, mails, banks, audit),
            audit,
            sync,
            search
        );
    }

    /// <summary>
    /// Mail is indexed on demand: a conversion reads what could belong to a row — never further
    /// back than the exports reach, never further than what changed since the last run. No
    /// readable mailbox is not an error, just no mail yet.
    /// </summary>
    private static MailSyncReport? SyncMail(ReportContext context, DateTimeOffset? since)
    {
        if (context.Mailbox is not { } mailbox)
        {
            return null;
        }

        // Stored facts built by other rules would all have to be read again — the whole mailbox, for
        // facts about years nobody asked about. A short import can say no to that; the report then
        // rests on exports and whatever the index already knows.
        if (context.Index.Fingerprint != context.Rules.Fingerprint && !context.AllowRebuild)
        {
            return null;
        }

        try
        {
            return MailSyncService.Sync(
                context.Index,
                mailbox.Root,
                context.Rules,
                since,
                forceFull: context.FullReindex,
                starting: context.Progress.SyncStarting,
                progress: context.Progress.Sync
            );
        }
        catch (Exception exception) when (exception is MailStoreUnavailableException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// How far back the store has to be read: the exports' span with a margin, or the overlap since
    /// the last sync — whichever reaches further. A rule change does not widen it: the window is read
    /// again under the new rules, and older facts wait for the run that reaches their dates.
    /// </summary>
    private static DateTimeOffset? WindowFor(ReportContext context, MailRange? range) =>
        MailRange.Window(
            range,
            context.Index.LastSyncedAt,
            ExportMarginDays,
            MailSyncService.OverlapDays
        );

    /// <summary>The indexed mail, plus what a search turns up when one was asked for.</summary>
    private static (IReadOnlyList<MailFacts> Mails, MailSearchResult Search) Mails(
        ReportContext context,
        IReadOnlyList<BankSource> banks,
        MailRange? range
    )
    {
        var indexed = IndexedMail(context, range);

        if (context.Finder is not { } finder || banks.Count == 0)
        {
            return (indexed, MailSearchResult.None);
        }

        var found = Searched(context, finder, banks, indexed);

        return (found.Replacing(indexed), found);
    }

    private static IReadOnlyList<MailFacts> IndexedMail(ReportContext context, MailRange? range) =>
        range is { } days
            ? context.Index.LoadFacts(
                since: days.Since(Correlator.SearchWindowDays),
                until: days.Until(Correlator.SearchWindowDays)
            )
            : context.Index.LoadFacts(limit: MailLimit);

    /// <summary>
    /// The second pass: the rows that say too little are looked up, and only those. Every other
    /// row is left exactly as the bank wrote it, which is why most of an export costs nothing.
    /// </summary>
    private static MailSearchResult Searched(
        ReportContext context,
        IMailFinder finder,
        IReadOnlyList<BankSource> banks,
        IReadOnlyList<MailFacts> indexed
    )
    {
        var entries = banks.SelectMany(bank => bank.Entries).ToArray();
        var worthSearching = WorthSearching.Of(entries, context.Rules);
        context.Progress.Rows(worthSearching.Count);

        return Search(context, finder, worthSearching, entries, indexed);
    }

    private static MailSearchResult Search(
        ReportContext context,
        IMailFinder finder,
        IReadOnlyList<CorrelationEntry> worthSearching,
        IReadOnlyList<CorrelationEntry> entries,
        IReadOnlyList<MailFacts> indexed
    ) =>
        MailSearchFallback.Search(
            worthSearching,
            Correlator.Match(entries, indexed),
            finder,
            Lookup(context),
            MailSearchFallback.QueryOf,
            context.Progress.Entry
        );

    /// <summary>Every message a search reads, counted as it happens, so a slow run says so.</summary>
    private static MailLookup Lookup(ReportContext context) =>
        MailSyncService.Lookup(context.Rules, context.Bodies, context.Index, BodyCounter(context));

    private static Action<MailFacts> BodyCounter(ReportContext context)
    {
        var readSoFar = 0;
        return _ => context.Progress.BodyRead(++readSoFar);
    }
}
