using System.Globalization;
using System.Reflection;
using System.Text;
using AutoYnabCsv.App.Mail;
using AutoYnabCsv.Common;
using AutoYnabCsv.Converters;
using AutoYnabCsv.Detectors;
using AutoYnabCsv.Interactions;
using AutoYnabCsv.MailIndex;
using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Extraction;
using AutoYnabCsv.MailIndex.Index;
using AutoYnabCsv.MailIndex.Web;
using ConsoleAppFramework;

namespace AutoYnabCsv.App;

internal static class Program
{
    private static void Main(string[] args)
    {
        // The heartbeat is a timer: if the run is interrupted, the line it was rewriting is closed
        // and the timer is let go, so the shell comes back instead of the last number staying put.
        Console.CancelKeyPress += (_, _) => ConsoleReportProgress.Flush();

        if (args.Length > 0 && args[0] == "mail")
        {
            Environment.ExitCode = MailCli.Run(args.AsSpan(1));
            return;
        }

        if (args.Length == 1 && File.Exists(args[0]))
        {
            ConvertToConsole(args[0]);
            return;
        }

        var app = ConsoleApp.Create();

        app.Add("detect", DetectCommand);
        app.Add("convert", ConvertCommand);
        app.Add("version", VersionCommand);

        app.Run(args);
    }

    private static void ConvertToConsole(string path)
    {
        try
        {
            Console.WriteLine(TextConversion.ConvertFile(path));
        }
        catch (FormatNotSupportedException)
        {
            SetExitCode(1);
            Console.WriteLine($"Format not supported: {path}");
        }
        catch (NoConverterFoundException)
        {
            SetExitCode(1);
            Console.WriteLine($"No converter found for: {path}");
        }
    }

    private static void VersionCommand()
    {
        Console.WriteLine(Assembly.GetEntryAssembly()
        ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion);
    }

    private static void DetectCommand(string input)
    {
        try
        {
            Console.WriteLine(DetectInputType(input));
        }
        catch (FileNotFoundException)
        {
            SetExitCode(1);
            Console.WriteLine($"File not found: {input}");
        }
    }

    /// <summary>
    /// Converts and reports in one run: the page to look at and the rows to import come out
    /// together, so there is never a half-converted file to import by mistake.
    /// </summary>
    private static void ConvertCommand(string input, bool search = false)
    {
        var inputs = Directory.Exists(input)
            ? ConvertedFiles.ExportsIn(input).ToArray()
            : [input];

        if (inputs.Length == 0)
        {
            Console.WriteLine($"No CSV export in {input}");
            SetExitCode(1);
            return;
        }

        WriteReportFor(inputs, search);
    }

    /// <summary>
    /// A conversion is not done until the report exists: the page to look at and the rows to
    /// import belong to the same run.
    /// </summary>
    private static void WriteReportFor(IReadOnlyList<string> inputs, bool search)
    {
        var outputDirectory = ReportLocation.For(inputs);
        var indexPath = MailIndexStore.DefaultPath();

        using var index = MailIndexStore.Open(indexPath);
        var rules = RuleSetLoader.Load();
        var version =
            Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "dev";

        var context = ReportContext.For(
            index,
            indexPath,
            rules,
            version,
            searchMail: search,
            progress: ConsoleReportProgress.Hooks
        );

        Echo(inputs, outputDirectory, search);
        EchoRules(rules);
        EchoIndex(index, indexPath);
        EchoMailbox(context);
        Console.WriteLine($"Version: {version}");
        Console.WriteLine();
        Console.WriteLine(
            search
                ? "Converting, indexing mail, and searching the mailbox for what it cannot explain…"
                : "Converting and indexing mail…"
        );

        var run = ReportService.Write(context, inputs, outputDirectory);

        ConsoleReportProgress.Flush();
        Console.WriteLine();
        Console.WriteLine($"Mail:    {MailStatus(run.Sync)}");
        ConsoleReportProgress.Unreadable(run.Sync);

        if (search)
        {
            Console.WriteLine($"Found:   {SearchStatus(context, run)}");
        }

        Console.WriteLine($"Report:  {run.Export.HtmlPath}");
        Console.WriteLine(
            $"Import:  {Path.Combine(run.Export.Directory, EnrichedCsvWriter.DirectoryName)} "
                + $"({run.Export.CsvPaths.Count} file(s))"
        );
    }

    /// <summary>The parameters of the run, before any work is done.</summary>
    private static void Echo(IReadOnlyList<string> inputs, string outputDirectory, bool search)
    {
        var folder = Directory.Exists(inputs[0])
            ? inputs[0]
            : Path.GetDirectoryName(inputs[0]) ?? inputs[0];

        Console.WriteLine(
            inputs.Count == 1
                ? $"Input:   {inputs[0]}"
                : $"Input:   {inputs.Count} export(s) in {folder}"
        );
        Console.WriteLine($"Output:  {outputDirectory}");
        Console.WriteLine($"Search:  {(search ? "on" : "off (add --search)")}");
    }

    /// <summary>
    /// Which patterns are in force and where they came from. A rule file in a config directory
    /// silently wins over a shipped rule, so the run says so instead of leaving it to be guessed.
    /// </summary>
    private static void EchoRules(ExtractionRuleSet rules)
    {
        var files = RuleSetLoader.Sources();

        Console.WriteLine(
            $"Rules:   {rules.RuleIds.Count} ({rules.PatternCount} patterns, "
                + $"{rules.Vendors.Names.Count} vendor names): "
                + string.Join(", ", rules.RuleIds)
        );
        Console.WriteLine(
            files.Count == 0
                ? $"         shipped in the tool; no {RuleSetLoader.FileName} in "
                    + $"{AppDirectories.ConfigDirectory()}, fingerprint {rules.Fingerprint[..12]}"
                : $"         shipped in the tool, then {string.Join(", ", files)}"
                    + $" — fingerprint {rules.Fingerprint[..12]}"
        );
    }

    /// <summary>Where the cache is and what it already holds: the other thing a result depends on.</summary>
    private static void EchoIndex(MailIndexStore index, string path)
    {
        Console.WriteLine($"Index:   {path}");
        Console.WriteLine(
            $"         {index.Count:N0} messages, {ConsoleFormat.Size(index.DiskBytes)} on disk"
                + (index.Migrated ? ", rebuilt once for a new search tokenizer" : string.Empty)
        );
    }

    /// <summary>The mailbox that will be read, or why there is none.</summary>
    private static void EchoMailbox(ReportContext context) =>
        Console.WriteLine(
            context.Mailbox is null
                ? "Mailbox: not readable"
                : $"Mailbox: {context.MailboxRoot} (version {context.MailboxVersion})"
        );

    /// <summary>Why there is no mail, in terms of the operating system that failed.</summary>
    private static string MailStatus(MailSyncReport? sync) =>
        sync is not null
            ? sync.Coverage
            : OperatingSystem.IsMacOS()
                ? "not indexed (grant Full Disk Access, then run again)."
                : "not read — reading mail is macOS (Apple Mail) only for now.";

    private static string SearchStatus(ReportContext context, ReportRun run)
    {
        if (context.Finder is null && !OperatingSystem.IsMacOS())
        {
            return "skipped — searching mail is macOS (Apple Mail) only for now.";
        }

        if (context.Finder is null)
        {
            return "skipped — the mailbox is not readable (Full Disk Access?).";
        }

        return run.Search.Entries == 0
            ? $"{run.Search.NotWorthSearching} rows already name what they are; none needs mail."
            : $"{run.Search.Entries} rows worth mail (of {run.Search.Entries + run.Search.NotWorthSearching}), "
                + $"{run.Search.Candidates} mailbox hits, "
                + $"{run.Search.Matches.Count} matched, "
                + $"{run.Search.Fetched} bodies fetched.";
    }

    private static void SetExitCode(int code)
    {
        Environment.ExitCode = code;
    }

    private static string DetectInputType(string input)
    {
        var contents = File.ReadAllText(input, Encoding.UTF8);
        var detection = DetectFirst.Instance.TryDetect(contents);
        return detection.SourceType;
    }
}