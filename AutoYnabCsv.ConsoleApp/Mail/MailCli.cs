using System.Globalization;
using System.Reflection;
using System.Text;
using AutoYnabCsv.Converters;
using AutoYnabCsv.MailIndex;
using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Enrichment;
using AutoYnabCsv.MailIndex.Extraction;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Index;
using AutoYnabCsv.MailIndex.Mail;
using AutoYnabCsv.MailIndex.Suggestions;
using AutoYnabCsv.MailIndex.Web;

namespace AutoYnabCsv.App.Mail;

/// <summary>
/// The analysis CLI: walks the local Apple Mail store, extracts the facts correlation needs,
/// pairs them with a bank export, and writes the rules it would propose.
/// </summary>
public static class MailCli
{
    private const int DefaultLimit = 500;

    public static int Run(ReadOnlySpan<string> arguments)
    {
        if (arguments.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        if (arguments[0] is "--help" or "-h" or "help")
        {
            PrintUsage();
            return 0;
        }

        var command = arguments[0];
        var options = MailOptions.Parse(arguments[1..]);

        try
        {
            return command switch
            {
                "sync" => Sync(options),
                "scan" => Scan(options),
                "text" => Text(options),
                "correlate" => Correlate(options),
                "export" => Export(options),
                "transfers" => Transfers(options),
                "patterns" => Patterns(options),
                "where" => Where(options),
                _ => UnknownCommand(command),
            };
        }
        catch (MailStoreUnavailableException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (DirectoryNotFoundException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (FileNotFoundException exception)
        {
            Console.Error.WriteLine($"File not found: {exception.FileName}");
            return 1;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int Scan(MailOptions options)
    {
        var index = ReadIndex(options);
        Console.WriteLine(Describe(index));

        foreach (var facts in index.Mails)
        {
            Console.WriteLine(
                $"{Day(facts.Mail.ReceivedAt)}  {facts.Vendor,-16}  {facts.Mail.SenderAddress}"
            );
            Console.WriteLine($"    {facts.Mail.Subject}");

            if (facts.OrderNumbers.Count > 0)
            {
                Console.WriteLine($"    orders: {string.Join(", ", facts.OrderNumbers)}");
            }

            if (facts.Amounts.Count > 0)
            {
                Console.WriteLine($"    amounts: {string.Join(", ", facts.Amounts.Select(Amount))}");
            }

            if (facts.Fields.Count > 0)
            {
                var fields = facts
                    .Fields.OrderBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(field => $"{field.Key}={field.Value}");
                Console.WriteLine($"    fields: {string.Join(", ", fields)}");
            }
        }

        Console.WriteLine();
        PrintScanSummary(index.Mails);
        return 0;
    }

    /// <summary>
    /// The plain text a message was reduced from, so a pattern can be written against the real
    /// thing instead of a guess. The index keeps facts and not bodies, so it is read from disk.
    /// </summary>
    private static int Text(MailOptions options)
    {
        var needle = options.Value("match") ?? throw new ArgumentException("--match is required.");
        var rules = LoadRules(options);
        var found = ReadIndex(options)
            .Mails.Where(mail => Mentions(mail, needle))
            .ToArray();

        if (found.Length == 0)
        {
            Console.WriteLine($"No indexed message mentions {needle}.");
            return 0;
        }

        foreach (var match in found)
        {
            PrintText(match, rules);
        }

        return 0;
    }

    private static void PrintText(MailFacts match, ExtractionRuleSet rules)
    {
        Console.WriteLine($"{Day(match.Mail.ReceivedAt)}  {match.Mail.SenderAddress}");
        Console.WriteLine($"    {match.Mail.Subject}");
        Console.WriteLine();
        Console.WriteLine(
            MailSyncService.Read(match.Mail.Path, rules)?.Mail.Text is { Length: > 0 } text
                ? text
                : "(no body on this Mac — use --search)"
        );
        Console.WriteLine();
    }

    /// <summary>What the index can tell without reading a body: who, when, and which numbers.</summary>
    private static bool Mentions(MailFacts mail, string needle) =>
        mail.Mail.Subject.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || mail.Mail.SenderAddress.Contains(needle, StringComparison.OrdinalIgnoreCase)
        || mail.OrderNumbers.Any(order =>
            order.Contains(needle, StringComparison.OrdinalIgnoreCase)
        );

    private static int Correlate(MailOptions options)
    {
        var input = options.Value("input") ?? throw new ArgumentException("--input is required.");

        var entries = ReadEntries(input);
        var rules = LoadRules(options);

        using var store = OpenStore(options);
        var mails = store.LoadFacts(options.Int("limit", DefaultLimit), options.Since());
        if (mails.Count == 0)
        {
            Console.Error.WriteLine("No indexed mail yet. Run: auto-ynab-csv mail sync");
        }

        var correlations = WithSearchedMatches(options, entries, mails, rules, store);
        PrintCorrelations(entries, correlations);
        var output = options.Value("out") ?? Path.ChangeExtension(input, ".rules.toml");
        File.WriteAllText(output, RuleSuggestionWriter.Write(correlations));

        Console.WriteLine();
        Console.WriteLine($"Proposed rules written to {output}");
        return 0;
    }

    /// <summary>
    /// Pairs entries with indexed mail, and — when asked — with what a mailbox search turns up for
    /// the entries the index cannot explain.
    /// </summary>
    private static IReadOnlyList<Match> WithSearchedMatches(
        MailOptions options,
        IReadOnlyList<CorrelationEntry> entries,
        IReadOnlyList<MailFacts> mails,
        ExtractionRuleSet rules,
        MailIndexStore store
    )
    {
        var indexed = Correlator.Match(entries, mails);
        if (!options.Flag("search"))
        {
            return indexed;
        }

        return indexed
            .Concat(Found(options, entries, mails, rules, store).Matches)
            .ByEvidence()
            .ToArray();
    }

    private static MailSearchResult Found(
        MailOptions options,
        IReadOnlyList<CorrelationEntry> entries,
        IReadOnlyList<MailFacts> mails,
        ExtractionRuleSet rules,
        MailIndexStore store
    ) =>
        MailSearchFallback.Search(
            WorthSearching.Of(entries, rules),
            Correlator.Match(entries, mails),
            Finder(options, rules),
            MailSyncService.Lookup(
                rules,
                OperatingSystem.IsMacOS() ? new AppleScriptMailBody() : null,
                store
            ),
            MailSearchFallback.QueryOf,
            ConsoleReportProgress.Hooks.Entry
        );

    private static IMailFinder Finder(MailOptions options, ExtractionRuleSet rules) =>
        new HeaderMailFinder(
            MailboxOf(options.Value("root"))?.Root ?? MailStoreLocator.DefaultRoot(),
            path => MailSyncService.Read(path, rules)
        );

    private static IndexSnapshot ReadIndex(MailOptions options, int defaultLimit = DefaultLimit)
    {
        using var store = OpenStore(options);
        var mails = store.LoadFacts(options.Int("limit", defaultLimit), options.Since());

        if (mails.Count == 0)
        {
            Console.Error.WriteLine("No indexed mail yet. Run: auto-ynab-csv mail sync");
        }

        return new IndexSnapshot(mails, store.IndexedFrom, store.IndexedThrough, store.LastSyncedAt);
    }

    private sealed record IndexSnapshot(
        IReadOnlyList<MailFacts> Mails,
        DateTimeOffset? IndexedFrom,
        DateTimeOffset? IndexedThrough,
        DateTimeOffset? LastSyncedAt
    );

    private static string Describe(IndexSnapshot index) =>
        index.Mails.Count == 0
            ? "Index: empty."
            : $"Index: {index.Mails.Count} mails covering {Day(index.IndexedFrom)} … "
                + $"{Day(index.IndexedThrough)}, last sync {Day(index.LastSyncedAt)}.";

    private static int Export(MailOptions options)
    {
        var inputs = options.Values("input");
        var outputDirectory = options.Value("out") ?? ReportLocation.For(inputs);
        var rules = LoadRules(options);

        using var store = OpenStore(options);
        var run = ReportService.Write(
            ReportContext.For(
                store,
                options.Value("index") ?? MailIndexStore.DefaultPath(),
                rules,
                ToolVersion(),
                options.Flag("search"),
                options.Value("root"),
                ConsoleReportProgress.Hooks
            ),
            inputs,
            outputDirectory
        );

        var export = run.Export;

        ConsoleReportProgress.Flush();
        Console.WriteLine($"Run: {export.Directory}");
        Console.WriteLine($"  {run.Audit.Summary}");
        Console.WriteLine($"  {export.EvidenceMails} mail(s) kept as evidence");
        Console.WriteLine(run.Sync is null
            ? "  mail not indexed (grant Full Disk Access, then run again)"
            : $"  {run.Sync.Describe()}");
        ConsoleReportProgress.Unreadable(run.Sync);
        Console.WriteLine($"  {export.HtmlPath}");
        Console.WriteLine($"  {export.JsonPath}");
        Console.WriteLine($"  {export.AuditPath}");

        foreach (var csv in export.CsvPaths)
        {
            Console.WriteLine($"  {csv}");
        }

        return 0;
    }

    /// <summary>
    /// A report belongs next to the exports it describes, in the same folder: the page is simply
    /// there after a conversion. Without inputs the app data folder.
    private static string DefaultReportDirectory(IReadOnlyList<string> inputs) =>
        ReportLocation.For(inputs);

    private static MailStore? MailboxOf(string? root)
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

    private static string ToolVersion() =>
        Assembly
            .GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "dev";

    private static int Sync(MailOptions options)
    {
        var rules = LoadRules(options);
        using var store = OpenStore(options);
        var root = ResolveStore(options.Value("root"));

        Console.WriteLine($"Reading {root}…");

        var report = MailSyncService.Sync(
            store,
            root,
            rules,
            options.Since(),
            options.Flag("full"),
            ConsoleReportProgress.Hooks.SyncStarting,
            ConsoleReportProgress.Hooks.Sync
        );

        Console.WriteLine();
        Console.WriteLine(report.Describe());
        ConsoleReportProgress.Unreadable(report);

        if (report.WindowSince is { } window)
        {
            Console.WriteLine($"  looked at files written since {Day(window)}");
        }

        Console.WriteLine(
            $"  {store.Count} messages in {options.Value("index") ?? MailIndexStore.DefaultPath()}"
        );
        Console.WriteLine(
            store.Count == 0
                ? "  the index is empty"
                : $"  covers {Day(store.IndexedFrom)} … {Day(store.IndexedThrough)}; "
                    + $"last sync {Day(store.LastSyncedAt)}"
        );

        return report.Result.Unreadable > 0 && report.Result.Indexed == 0 ? 1 : 0;
    }

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static int Where(MailOptions options)
    {
        var index = options.Value("index") ?? MailIndexStore.DefaultPath();

        Console.WriteLine($"index:     {index}");
        Console.WriteLine($"patterns:  {string.Join(", ", AppDirectories.PatternDirectories().Select(directory => Path.Combine(directory, RuleSetLoader.FileName)))}");
        Console.WriteLine($"export:    {AppDirectories.DefaultExportDirectory()} (fallback when no --input)");
        Console.WriteLine($"report:    beside the exports (default --out)");

        if (File.Exists(index))
        {
            using var store = MailIndexStore.Open(index);
            Console.WriteLine(
                store.Count == 0
                    ? "contents:  empty"
                    : $"contents:  {store.Count} messages covering "
                        + $"{Day(store.IndexedFrom)} … {Day(store.IndexedThrough)}; "
                        + $"last sync {Day(store.LastSyncedAt)}"
            );
        }

        return 0;
    }

    private static int Patterns(MailOptions options)
    {
        var rules = LoadRules(options);

        Console.WriteLine($"rules: {string.Join(", ", rules.RuleIds)}");
        Console.WriteLine($"fingerprint: {rules.Fingerprint}");
        Console.WriteLine($"  shipped in the assembly as {RuleSetLoader.ResourceName}");

        foreach (var directory in AppDirectories.PatternDirectories())
        {
            Console.WriteLine($"  looks for {RuleSetLoader.FileName} in {directory}");
        }

        if (options.Flag("write"))
        {
            Console.WriteLine($"written: {WriteShippedPatterns()}");
        }

        return 0;
    }

    /// <summary>
    /// Hands the shipped rules over as a file to edit instead of guess at. It lands in the user's
    /// config directory, which is read after the assembly, so it wins by id from then on.
    /// </summary>
    private static string WriteShippedPatterns()
    {
        var path = Path.Combine(AppDirectories.ConfigDirectory(), RuleSetLoader.FileName);
        File.WriteAllText(path, RuleSetLoader.ShippedText());
        return path;
    }

    private static ExtractionRuleSet LoadRules(MailOptions options) =>
        RuleSetLoader.Load(options.Value("patterns"));

    private static MailIndexStore OpenStore(MailOptions options) =>
        MailIndexStore.Open(options.Value("index") ?? MailIndexStore.DefaultPath());

    private static int Transfers(MailOptions options)
    {
        var (banks, _) = BankSourceReader.Read(options.Values("input"));
        var entries = banks
            .SelectMany(bank => bank.Entries.Select(entry => (bank.File, entry)))
            .ToArray();

        var pairs = TransferDetector.Find(
            entries.Select(entry => (entry.File, entry.entry)).ToArray()
        );

        if (pairs.Count == 0)
        {
            Console.WriteLine("No mirrored transfers between the given files.");
            return 0;
        }

        var sources = entries.ToDictionary(entry => entry.entry.Index, entry => entry.File);
        foreach (var pair in pairs)
        {
            Console.WriteLine(
                $"{pair.Outgoing.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}  {Amount(pair.Outgoing.Amount),10}  "
                    + $"{Path.GetFileName(sources[pair.Outgoing.Index])} -> "
                    + $"{Path.GetFileName(sources[pair.Incoming.Index])}"
                    + (pair.DayDistance == 0 ? "" : $"  (+{pair.DayDistance}d)")
            );
        }

        Console.WriteLine($"{pairs.Count} transfer(s).");
        return 0;
    }

    private static string ResolveStore(string? root)
    {
        var stores = MailStoreLocator.FindReadableStores(root);
        if (stores.Count == 0)
        {
            throw new DirectoryNotFoundException(
                $"No Apple Mail store found under {root ?? MailStoreLocator.DefaultRoot()}."
            );
        }

        return stores[0].Root;
    }

    private static IReadOnlyList<CorrelationEntry> ReadEntries(string path) =>
        DetectAndConvert
            .Instance.Convert(File.ReadAllText(path, Encoding.UTF8))
            .Select(
                (entry, index) =>
                    new CorrelationEntry(
                        index,
                        entry.Date,
                        entry.Payee,
                        entry.Inflow > 0 ? entry.Inflow : entry.Outflow,
                        entry.Memo,
                        entry.Inflow > 0
                    )
            )
            .ToArray();

    private static void PrintCorrelations(
        IReadOnlyList<CorrelationEntry> entries,
        IReadOnlyList<Match> correlations
    )
    {
        var matched = 0;
        var ambiguous = 0;

        foreach (var entry in entries)
        {
            var candidates = correlations
                .Where(correlation => correlation.Entry.Index == entry.Index)
                .ByEvidence()
                .ToArray();

            Console.WriteLine(
                $"{entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}  {entry.Payee,-32} {Amount(entry.Amount),10}"
            );

            if (candidates.Length == 0)
            {
                Console.WriteLine("    no candidate");
                continue;
            }

            var best = candidates[0];
            var bestCandidates = candidates.Count(candidate =>
                candidate.Evidence == best.Evidence && candidate.DayDistance == best.DayDistance
            );

            if (bestCandidates > 1)
            {
                ambiguous++;
                Console.WriteLine($"    ambiguous: {bestCandidates} candidates, {best.Evidence.Describe()}");
            }
            else
            {
                matched++;
            }

            foreach (var candidate in candidates)
            {
                Console.WriteLine(
                    $"    {candidate.Evidence.Describe(),-34} {candidate.Mail.Vendor,-14} "
                        + $"{Day(candidate.Mail.Mail.ReceivedAt)}  {candidate.Mail.Mail.Subject}"
                );
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            $"{matched} matched, {ambiguous} ambiguous, {entries.Count - matched - ambiguous} without a candidate."
        );
    }

    private static void PrintScanSummary(IReadOnlyList<MailFacts> mails)
    {
        var withOrders = mails.Count(mail => mail.OrderNumbers.Count > 0);
        var withAmounts = mails.Count(mail => mail.Amounts.Count > 0);

        Console.WriteLine(
            $"Scanned {mails.Count} mails: {withOrders} with order numbers, {withAmounts} with amounts."
        );

        foreach (
            var vendor in mails
                .Where(mail => mail.Vendor.Length > 0)
                .GroupBy(mail => mail.Vendor)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Take(10)
        )
        {
            Console.WriteLine($"  {vendor.Key,-20} {vendor.Count()}");
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown mail command: {command}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: auto-ynab-csv mail <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  sync       Index new and changed messages (incremental).");
        Console.WriteLine("  scan       Print the facts stored in the index.");
        Console.WriteLine("  text       Print the plain text of the messages that mention something.");
        Console.WriteLine("  correlate  Pair a bank export with indexed mail and propose rules.");
        Console.WriteLine("  export     Write a searchable report, and one YNAB CSV per input.");
        Console.WriteLine("  transfers  Show transfers mirrored between the given files.");
        Console.WriteLine("  patterns   Show the extraction rules in effect.");
        Console.WriteLine("  where      Show the index, patterns and export paths.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --root PATH         Apple Mail store root (default: ~/Library/Mail, sync only).");
        Console.WriteLine("  --limit N           Messages to load, newest first (default: 500).");
        Console.WriteLine("  --since YYYY-MM-DD  Only messages written on or after that day.");
        Console.WriteLine("  --input FILE        Bank export to correlate or export; a directory is expanded.");
        Console.WriteLine("  --search            Also search the mailbox for entries the index cannot explain.");
        Console.WriteLine("  --out DIR           Where the report goes (default: the export folder).");
        Console.WriteLine("  --index FILE        Index database (default: the app data directory).");
        Console.WriteLine("  --patterns FILE     Use this patterns.jsonc instead of the layered defaults.");
        Console.WriteLine("  --write             With patterns: write the shipped rules out to edit.");
        Console.WriteLine("  --full              Re-read every message (sync only).");
    }

    private static string Day(DateTimeOffset value) =>
        value == DateTimeOffset.MinValue ? "unknown  " : value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Day(DateTimeOffset? value) =>
        value is { } at ? at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "unknown";

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
