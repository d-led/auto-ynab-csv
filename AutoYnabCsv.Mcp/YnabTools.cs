using System.ComponentModel;
using System.Text;
using AutoYnabCsv.Common;
using AutoYnabCsv.Statements;
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Categorize;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;
using AutoYnabCsv.Ynab.Report;
using AutoYnabCsv.Ynab.Settings;
using AutoYnabCsv.Ynab.Snapshot;
using ModelContextProtocol.Server;

namespace AutoYnabCsv.Mcp;

/// <summary>
/// The plan and the bank exports as answers rather than as commands. Every tool here reads files that
/// are already on disk: asking what a balance is costs no request, so the hourly allowance can never
/// be the reason a question about money goes unanswered.
/// </summary>
[McpServerToolType]
public static class YnabTools
{
    /// <summary>The variable the commands that talk to YNAB read; the tools here never do.</summary>
    private const string TokenVariable = "YNAB_TOKEN";

    [McpServerTool(Name = "ynab_check")]
    [Description(
        "Call this first: is the server alive, which build is answering, and can it see what you will "
            + "ask about? Reports the running build, whether a token is set (never its value), the "
            + "settings it would use, and — when a folder is given — whether a snapshot is there. "
            + "Makes no request, so it costs nothing."
    )]
    public static string Check(string? snapshotFolder = null, string? settingsFile = null)
    {
        var text = new StringBuilder();
        var settings = YnabConfig.Load(settingsFile);

        text.AppendLine("# connection");
        text.AppendLine($"server:   {Name()} {BuildStamp.Line(Version(), BuildStamp.Of())}");
        text.AppendLine(
            $"token:    {(TokenIsSet() ? $"{TokenVariable} is set" : $"{TokenVariable} is NOT set")}"
                + " — these tools never need one; the commands that talk to YNAB do."
        );
        text.AppendLine(
            $"settings: {settingsFile ?? YnabConfig.DefaultPath} — {SettingsSummary(settings)}"
        );
        text.AppendLine($"snapshot: {SnapshotSummary(snapshotFolder)}");
        text.AppendLine();
        text.AppendLine("No request was made: everything above comes from files and the environment.");

        return text.ToString();
    }

    /// <summary>The version of the assembly answering, so a stale process is visible at a glance.</summary>
    private static string Version() =>
        typeof(YnabTools).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    private static string Name() => typeof(YnabTools).Assembly.GetName().Name ?? "auto-ynab-csv-mcp";

    private static bool TokenIsSet() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TokenVariable));

    /// <summary>What the settings actually set — counted, because the ids are not the point here.</summary>
    private static string SettingsSummary(YnabConfig settings) =>
        settings.Plan is null && settings.Accounts.Count == 0
            ? "nothing set (no file, or an empty one); labels and account ids are still passed per call"
            : $"plan {(settings.Plan is null ? "missing" : "set")}, {settings.Accounts.Count} account(s), "
                + $"{settings.OwnIbans.Count} own iban(s), {settings.ExpectedBalances.Count} expected balance(s)";

    private static string SnapshotSummary(string? folder)
    {
        if (folder is null)
        {
            return "not asked for — pass a folder to check one";
        }

        if (!File.Exists(Path.Combine(folder, SnapshotStore.FileName)))
        {
            return $"no {SnapshotStore.FileName} in {folder} — `auto-ynab-csv ynab snapshot` writes one, "
                + "and that is the command that costs requests";
        }

        var snapshot = SnapshotStore.Load(folder);

        return $"{snapshot.PlanName}, read {snapshot.FetchedAt:yyyy-MM-dd HH:mm} UTC "
            + $"({Ago(snapshot.FetchedAt)}), {snapshot.Accounts.Count} account(s), "
            + $"{snapshot.Transactions.Count} transaction(s)";
    }

    /// <summary>How old the snapshot is, in the unit a person would say it in.</summary>
    private static string Ago(DateTimeOffset moment)
    {
        var age = DateTimeOffset.UtcNow - moment;

        return age switch
        {
            { TotalMinutes: < 2 } => "just now",
            { TotalHours: < 1 } => $"{(int)age.TotalMinutes} min ago",
            { TotalDays: < 1 } => $"{(int)age.TotalHours} h ago",
            _ => $"{(int)age.TotalDays} day(s) ago",
        };
    }

    [McpServerTool(Name = "ynab_accounts")]
    [Description(
        "The accounts of a snapshot, with the balance each one holds. Reads the snapshot folder only — "
            + "no request is made, so this is always safe to call."
    )]
    public static string Accounts(string snapshotFolder)
    {
        var snapshot = SnapshotStore.Load(snapshotFolder);
        var text = new StringBuilder();

        text.AppendLine($"# {snapshot.PlanName} — read {snapshot.FetchedAt:yyyy-MM-dd HH:mm}");
        text.AppendLine();
        text.AppendLine("| account | type | balance | cleared | uncleared |");
        text.AppendLine("| --- | --- | ---: | ---: | ---: |");

        foreach (var account in snapshot.Accounts.OrderBy(account => account.Name))
        {
            text.AppendLine(
                $"| {Table.Cell(account.Name, 28)} | {account.Type} | {Table.Amount(account.Balance.Amount)} "
                    + $"| {Table.Amount(account.Cleared.Amount)} | {Table.Amount(account.Uncleared.Amount)} |"
            );
        }

        var onBudget = Money.Sum(snapshot.Accounts.Where(account => account.OnBudget).Select(account => account.Balance));

        text.AppendLine();
        text.AppendLine($"On budget: {Table.Amount(onBudget.Amount)} across {snapshot.Accounts.Count} account(s).");

        return text.ToString();
    }

    [McpServerTool(Name = "ynab_exports")]
    [Description(
        "What a folder of bank exports contains, as the tool reads it: one row per file with the account "
            + "it belongs to, the period it covers, the balance it declares and how many rows it has. "
            + "Reads files only."
    )]
    public static string Exports(string exportsFolder)
    {
        var statements = BankStatementReader.ReadFolder(exportsFolder);
        if (statements.Count == 0)
        {
            return $"No export in {exportsFolder} was recognised. Nothing was read.";
        }

        var text = new StringBuilder();
        text.AppendLine($"# {statements.Count} export(s) in {exportsFolder}");
        text.AppendLine();
        text.AppendLine("| account | file | period | closing balance | rows |");
        text.AppendLine("| --- | --- | --- | ---: | ---: |");

        foreach (var statement in statements)
        {
            var closing = statement.Statement.ClosingBalance is { } balance ? Table.Amount(balance) : "not printed";

            text.AppendLine(
                $"| {Table.Cell(statement.Label, 22)} | {Table.Cell(Path.GetFileName(statement.SourceFile), 34)} "
                    + $"| {Period(statement.Statement)} | {closing} | {statement.Rows.Count} |"
            );
        }

        return text.ToString();
    }

    /// <summary>A recognised export prints its period; an unreadable one says so instead of guessing.</summary>
    private static string Period(ExportStatement statement) =>
        $"{statement.PeriodFrom?.ToString("yyyy-MM-dd") ?? "unknown"} … {statement.PeriodTo?.ToString("yyyy-MM-dd") ?? "unknown"}";

    [McpServerTool(Name = "ynab_reconcile")]
    [Description(
        "Holds a snapshot against a folder of bank exports and answers with the report: does the plan "
            + "hold the balance each export declares, which rows are missing, which are extra, and when the "
            + "two ledgers stopped agreeing. The account mapping, own account numbers and any balance the "
            + "export does not print are read from ~/.config/auto-ynab-csv/accounts.txt unless given here. "
            + "Offline; nothing is changed and no request is made."
    )]
    public static string Reconcile(
        string snapshotFolder,
        string exportsFolder,
        string[]? accounts = null,
        string[]? ownIbans = null,
        string[]? expectedBalances = null
    )
    {
        var config = YnabConfig.Load();
        var snapshot = SnapshotStore.Load(snapshotFolder);
        var statements = BankStatementReader.ReadFolder(exportsFolder);

        var reconciliation = Reconciler.Of(
            snapshot,
            statements,
            accounts is { Length: > 0 } ? Pairs(accounts) : config.Accounts,
            ownIbans is { Length: > 0 } ? Pairs(ownIbans) : config.OwnIbans,
            expectedBalances is { Length: > 0 } ? Amounts(expectedBalances) : Amounts(config.ExpectedBalances)
        );

        return ReconcileReport.Markdown(reconciliation, CategoryProposals.For(snapshot));
    }

    [McpServerTool(Name = "ynab_plan")]
    [Description(
        "A repair plan as tables instead of JSON: what would be added, moved between accounts and removed, "
            + "the largest removals first. Reads the plan file only."
    )]
    public static string Plan(string planPath, int largest = PlanSummary.LargestShown) =>
        PlanSummary.Markdown(PlanStore.Load(planPath), largest);

    [McpServerTool(Name = "ynab_journal")]
    [Description(
        "What an apply did, from its undo journal: how many rows were created, moved, deleted or "
            + "categorized, and the newest of them with their detail. Reads the journal file only."
    )]
    public static string JournalSummaryOf(string journalPath, int newest = JournalSummary.NewestShown) =>
        JournalSummary.Markdown(Journal.Read(journalPath), newest);

    /// <summary>A repeated <c>label=value</c> option, the way the command line takes them.</summary>
    private static Dictionary<string, string> Pairs(string[]? pairs) =>
        (pairs ?? [])
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0].Length > 0)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

    private static Dictionary<string, Money> Amounts(IReadOnlyDictionary<string, string> pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => new Money(Amount(pair.Key, pair.Value)));

    private static Dictionary<string, Money> Amounts(string[]? pairs) => Amounts(Pairs(pairs));

    private static decimal Amount(string label, string text) =>
        GermanAmount.TryParse(text, out var amount)
            ? amount
            : throw new ArgumentException($"The balance for '{label}' is not an amount: '{text}'.");
}
