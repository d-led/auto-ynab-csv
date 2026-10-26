using AutoYnabCsv.MailIndex;
using AutoYnabCsv.Statements;
using AutoYnabCsv.Ynab;
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Categorize;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Http;
using AutoYnabCsv.Ynab.Reconcile;
using AutoYnabCsv.Ynab.Report;
using AutoYnabCsv.Ynab.Settings;
using AutoYnabCsv.Ynab.Snapshot;

namespace AutoYnabCsv.App.Ynab;

/// <summary>
/// The commands that hold the plan against the bank's own exports. Reading is free and always safe;
/// changing the plan needs <c>--apply</c> and writes a journal, so a wrong decision is a revert and
/// not an afternoon of typing.
/// </summary>
public static class YnabCli
{
    public const string TokenVariable = "YNAB_TOKEN";

    private const string DefaultSince = "2024-01-01";

    public static int Run(ReadOnlySpan<string> arguments) => RunAsync(arguments.ToArray()).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(string[] arguments)
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
        var options = YnabOptions.Parse(arguments[1..]);

        try
        {
            return command switch
            {
                "plans" => await PlansAsync(options),
                "snapshot" => await SnapshotAsync(options),
                "accounts" => Accounts(options),
                "balances" => await BalancesAsync(options),
                "reconcile" => Reconcile(options),
                "apply" => await ApplyAsync(options),
                "import" => await ImportAsync(options),
                "undo" => await UndoAsync(options),
                _ => Unknown(command),
            };
        }
        catch (YnabApiException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (RequestBudgetExhaustedException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 3;
        }
        catch (SnapshotUnreadableException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (PlanUnreadableException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (ArgumentNullException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (MissingTokenException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static async Task<int> PlansAsync(YnabOptions options)
    {
        using var http = new HttpClient();
        var gateway = Gateway(http);

        foreach (var plan in await gateway.PlansAsync())
        {
            Console.WriteLine($"{plan.Id}  {plan.Name}");
        }

        return 0;
    }

    private static async Task<int> SnapshotAsync(YnabOptions options)
    {
        using var http = new HttpClient();
        var gateway = Gateway(http, Console.WriteLine);
        var planId = await ChosenPlanAsync(gateway, options);

        var since = DateOnly.TryParse(options.Or("since", DefaultSince), out var day)
            ? day
            : throw new ArgumentException($"--since must be a date, not '{options.Get("since")}'");

        var outDirectory = options.Or("out", Path.Combine("artifacts", "ynab", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        var snapshot = await YnabSnapshotService.ReadAsync(gateway, planId, since, Console.WriteLine);
        var path = SnapshotStore.Save(snapshot, outDirectory);

        Console.WriteLine($"snapshot: {path}");
        Console.WriteLine($"          {snapshot.Accounts.Count} account(s), {snapshot.Categories.Count} category(ies), {snapshot.Transactions.Count} transaction(s)");

        return 0;
    }

    private static int Accounts(YnabOptions options)
    {
        var snapshot = SnapshotStore.Load(options.Or("snapshot", "."));

        Console.WriteLine($"plan {snapshot.PlanName} read {snapshot.FetchedAt:yyyy-MM-dd HH:mm} UTC");
        Console.WriteLine();
        Console.WriteLine($"{"account",-24} {"type",-14} {"budget",-7} {"balance",12}  id");

        foreach (var account in snapshot.Accounts.OrderBy(account => account.Name, StringComparer.Ordinal))
        {
            Console.WriteLine(
                $"{account.Name,-24} {account.Type,-14} {(account.OnBudget ? "on" : "off"),-7} {account.Balance,12}  {account.Id}"
            );
        }

        return 0;
    }

    private static int Reconcile(YnabOptions options)
    {
        var snapshotDirectory = options.Or("snapshot", ".");
        var snapshot = SnapshotStore.Load(snapshotDirectory);
        var input = options.Required("input");
        var statements = BankStatementReader.ReadFolder(input);
        var outDirectory = options.Or("out", snapshotDirectory);

        if (statements.Count == 0)
        {
            Console.Error.WriteLine($"No bank export in {input}");
            return 1;
        }

        var reconciliation = Reconciler.Of(
            snapshot,
            statements,
            Overrides(options),
            OwnIbans(options),
            ExpectedBalances(options)
        );
        var proposals = CategoryProposals.For(snapshot);
        var (markdownPath, jsonPath) = ReconcileReport.WriteAll(reconciliation, proposals, outDirectory);
        var plan = RepairPlanBuilder.From(
            reconciliation,
            proposals,
            repairBalances: !options.Has("no-balance-repair"),
            categorize: !options.Has("no-categorize"),
            deleteDuplicates: !options.Has("no-duplicates"),
            deleteExtras: !options.Has("keep-extras")
        );
        var planPath = PlanStore.Save(plan, Path.Combine(outDirectory, PlanStore.FileName));

        Summary(reconciliation);

        Console.WriteLine();
        Console.WriteLine($"report: {markdownPath}");
        Console.WriteLine($"        {jsonPath}");
        Console.WriteLine($"plan:   {planPath}  ({plan.Creates.Count} create(s), {plan.Deletes.Count} delete(s), {plan.Moves.Count} move(s), {plan.Categories.Count} categor(ies))");
        Console.WriteLine("        nothing was changed — review it, then run `ynab apply --plan <file> --apply`");

        return 0;
    }

    /// <summary>
    /// The plan's balance per account, read with one request instead of a whole snapshot. It answers
    /// "does the plan hold what the bank says" when the exports' closing balances are to hand, and it is
    /// the check to run between repairs: a snapshot costs a request per month of history.
    /// </summary>
    private static async Task<int> BalancesAsync(YnabOptions options)
    {
        var planId = options.Or("plan", YnabConfig.Load().Plan ?? string.Empty);
        if (planId.Length == 0)
        {
            Console.Error.WriteLine(
                "No plan: add plan=<id> to ~/.config/auto-ynab-csv/accounts.txt, or pass --plan."
            );
            return 2;
        }

        using var http = new HttpClient();
        // One request is the whole command: sitting a few minutes for it beats being told to come back.
        var accounts = await Gateway(http, waitWorthTaking: TimeSpan.FromMinutes(5)).AccountsAsync(planId);
        var width = accounts.Count == 0 ? 7 : accounts.Max(account => account.Name.Length);

        Console.WriteLine(
            $"{"account".PadRight(width)}  {"balance".PadLeft(12)}  {"cleared".PadLeft(12)}  {"uncleared".PadLeft(12)}"
        );
        foreach (var account in accounts.Where(account => !account.Closed).OrderBy(account => account.Name, StringComparer.Ordinal))
        {
            Console.WriteLine(
                $"{account.Name.PadRight(width)}  {Amount(account.Balance).PadLeft(12)}  "
                    + $"{Amount(account.Cleared).PadLeft(12)}  {Amount(account.Uncleared).PadLeft(12)}"
            );
        }

        Console.WriteLine();
        Console.WriteLine("One request. Compare against the closing balance each export prints.");
        return 0;
    }

    private static string Amount(Money money) =>
        money.Amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<int> ApplyAsync(YnabOptions options)
    {
        var path = options.Required("plan");
        var plan = PlanStore.Load(path);

        if (plan.IsEmpty)
        {
            Console.WriteLine("The plan is empty: the ledgers already agree.");
            return 0;
        }

        PrintPlan(plan);

        // Rows to add leave as files, because importing them costs nothing: the allowance is spent on
        // the changes a file cannot carry — removing, moving and categorising.
        var importFolder = options.Or("csv", Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", "import"));
        var files = PlanCsv.Write(plan, importFolder);

        if (!options.Has("apply"))
        {
            Console.WriteLine();
            Console.WriteLine($"Rows to add are in {importFolder} — import them in YNAB, which costs no request.");
            foreach (var file in files)
            {
                Console.WriteLine($"  {file}");
            }

            Console.WriteLine("Dry run — nothing was changed. Add `--apply` to remove, move and categorise.");
            return 0;
        }

        using var http = new HttpClient();
        var throughTheApi = options.Has("create") ? plan : plan with { Creates = [] };

        // A handful of rows is worth sitting out the hour's spread for; a long repair is not, because
        // every one of its waits would add up to hours. The pace is announced either way.
        var result = await PlanApplier.ApplyAsync(
            throughTheApi,
            Gateway(http, waitWorthTaking: plan.Total <= 10 ? TimeSpan.FromMinutes(5) : null),
            options.Or("journal", "artifacts/ynab/journal.jsonl")
        );
        Report(result);

        Console.WriteLine();
        Console.WriteLine($"{files.Count} file(s) in {importFolder} hold the {plan.Creates.Count} row(s) to add.");
        Console.WriteLine("Import them in YNAB — that costs no request from the allowance.");

        return result.Clean ? 0 : 1;
    }

    private static async Task<int> UndoAsync(YnabOptions options)
    {
        var journal = options.Required("journal");
        var planId = options.Required("plan-id");

        Console.WriteLine($"Journal: {journal}");

        if (!options.Has("apply"))
        {
            Console.WriteLine("Dry run — add `--apply` to reverse what the journal lists.");
            return 0;
        }

        using var http = new HttpClient();
        var result = await PlanApplier.UndoAsync(journal, planId, Gateway(http));
        Report(result);

        return result.Clean ? 0 : 1;
    }

    private static void Summary(Reconciliation reconciliation)
    {
        Console.WriteLine($"plan {reconciliation.Snapshot.PlanName}, snapshot {reconciliation.Snapshot.FetchedAt:yyyy-MM-dd HH:mm} UTC");
        Console.WriteLine($"{"export",-24} {"bank",12} {"plan",12} {"difference",12} {"missing",8} {"extra",6}");

        foreach (var account in reconciliation.Accounts)
        {
            Console.WriteLine(
                $"{account.Label,-24} {Amount(account.ExportBalance),12} {account.PlanBalanceAtCutoff,12:0.00} "
                + $"{Signed(account.Difference),12} {account.MissingInPlan.Count,8} {account.OnlyInPlan.Count,6}"
            );
        }

        foreach (var statement in reconciliation.Unresolved)
        {
            Console.WriteLine($"{statement.Label,-24} {"unresolved",12}  {Path.GetFileName(statement.SourceFile)}");
        }
    }

    private static void PrintPlan(RepairPlan plan)
    {
        foreach (var create in plan.Creates)
        {
            Console.WriteLine($"create  {create.Date:yyyy-MM-dd} {create.AccountName,-20} {create.Amount,10:0.00}  {create.PayeeName}");
        }

        foreach (var move in plan.Moves)
        {
            Console.WriteLine(
                $"move    {move.Date:yyyy-MM-dd} {move.Amount,10:0.00}  {move.FromLabel} -> {move.ToLabel}  {move.Description}"
            );
        }

        foreach (var delete in plan.Deletes)
        {
            Console.WriteLine(
                $"delete  {delete.Transaction.Date:yyyy-MM-dd} {delete.AccountName,-20} {delete.Transaction.Amount,10:0.00}  "
                + $"{delete.Transaction.Description} — {delete.Why}"
            );
        }

        foreach (var category in plan.Categories)
        {
            Console.WriteLine($"category {category.TransactionId} -> {category.CategoryName}");
        }

        foreach (var note in plan.Notes)
        {
            Console.WriteLine($"note    {note}");
        }
    }

    private static void Report(ApplyResult result)
    {
        Console.WriteLine($"created {result.Created}, deleted {result.Deleted}, categorized {result.Categorized}, moved {result.Moved}");

        foreach (var problem in result.Problems)
        {
            Console.Error.WriteLine($"problem: {Secrets.Redact(problem, Token())}");
        }
    }

    /// <summary>
    /// The files a plan wrote, imported with the account chosen here. Two answers stand between the
    /// review and the first request, <c>--yes</c> skips them only when every account is already named
    /// with <c>--account</c>, and the rows carry an <c>import_id</c> so the same file twice is a no-op.
    /// </summary>
    private static async Task<int> ImportAsync(YnabOptions options)
    {
        var folder = options.Required("csv");
        var files = CsvImport.Read(folder);

        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No CSV file with rows in {folder}");
            return 1;
        }

        using var http = new HttpClient();
        var gateway = Gateway(http);
        var planId = await ChosenPlanAsync(gateway, options);
        var accounts = await gateway.AccountsAsync(planId);
        var given = Overrides(options);

        async Task<int> Create(IReadOnlyDictionary<string, string> ids)
        {
            var plan = CsvImport.Plan(planId, ids, files);
            var result = await PlanApplier.ApplyAsync(
                plan,
                gateway,
                options.Or("journal", "artifacts/ynab/journal.jsonl")
            );
            Report(result);
            return result.Clean ? 0 : 1;
        }

        if (options.Has("yes"))
        {
            var unresolved = files
                .Select(file => file.AccountName)
                .Where(name => !given.ContainsKey(name))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (unresolved.Length > 0)
            {
                Console.Error.WriteLine(
                    $"--yes needs an account for every file; missing: {string.Join(", ", unresolved)}"
                );
                return 1;
            }

            return await Create(given);
        }

        return await ImportSession.RunAsync(
            files,
            accounts,
            given,
            ask: prompt =>
            {
                Console.Write(prompt);
                return Console.ReadLine();
            },
            say: Console.WriteLine,
            create: Create,
            folder: folder
        );
    }

    private static string? Token() => Environment.GetEnvironmentVariable(TokenVariable);    private static IYnabGateway Gateway(HttpClient http, Action<string>? say = null, TimeSpan? waitWorthTaking = null) =>
        string.IsNullOrWhiteSpace(Token())
            ? throw new MissingTokenException(TokenVariable)
            : new YnabApi(http, Token()!, Requests(), say: say, waitWorthTaking: waitWorthTaking);

    /// <summary>
    /// The hour is counted in one file, so two commands a minute apart spend one allowance of 200
    /// instead of four hundred and a refusal neither of them could see coming.
    /// </summary>
    private static RequestBudget Requests() =>
        new(ledgerPath: Path.Combine(AppDirectories.DataDirectory(), "ynab-requests.txt"));

    private static async Task<string> ChosenPlanAsync(IYnabGateway gateway, YnabOptions options)
    {
        var requested = options.Get("plan");
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested!;
        }

        var plans = await gateway.PlansAsync();
        return plans.Count == 1
            ? plans[0].Id
            : throw new ArgumentException(
                "--plan is required; this token sees: " + string.Join(", ", plans.Select(plan => $"{plan.Id} ({plan.Name})"))
            );
    }

    private static IReadOnlyDictionary<string, string> Overrides(YnabOptions options) => Pairs(options, "account");

    private static IReadOnlyDictionary<string, string> OwnIbans(YnabOptions options) => Pairs(options, "own-iban");

    private static IReadOnlyDictionary<string, Money> ExpectedBalances(YnabOptions options)
    {
        var balances = new Dictionary<string, Money>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in options.All("expect-balance"))
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || !GermanAmount.TryParse(parts[1], out var amount))
            {
                throw new ArgumentException($"--expect-balance wants <label>=<amount>, not '{pair}'");
            }

            balances[parts[0]] = amount;
        }

        return balances;
    }

    private static IReadOnlyDictionary<string, string> Pairs(YnabOptions options, string name)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in options.All(name))
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                throw new ArgumentException($"--{name} wants <label>=<value>, not '{pair}'");
            }

            pairs[parts[0]] = parts[1];
        }

        return pairs;
    }

    private static string Amount(Money? value) => value is null ? "unknown" : value.Value.ToString();

    private static string Signed(Money? value) =>
        value is null ? "unknown" : value.Value.IsZero ? Money.Zero.ToString() : value.Value.ToString("+0.00;-0.00");

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown ynab command: {command}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: auto-ynab-csv ynab <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  plans      List the plans this token can see.");
        Console.WriteLine("  snapshot   Read the plan into a file all other commands work offline from.");
        Console.WriteLine("  accounts   Print the accounts and balances of a snapshot.");
        Console.WriteLine("  balances   Print the plan's balance per account; one request, no snapshot.");
        Console.WriteLine("  reconcile  Hold the export against the plan; write the report and the repair plan.");
        Console.WriteLine("  apply      Show the repair plan; with --apply, carry it out and journal it.");
        Console.WriteLine("  import     Import the CSV files a plan wrote, with the account chosen per file.");
        Console.WriteLine("  undo       Reverse what a journal lists; needs --apply.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --out DIR            Where the snapshot or the report goes.");
        Console.WriteLine("  --snapshot DIR       The folder holding snapshot.json (default: the working directory).");
        Console.WriteLine("  --input FOLDER       The folder of bank exports to reconcile.");
        Console.WriteLine("  --plan ID            The plan (default: the only one the token sees).");
        Console.WriteLine("  --since YYYY-MM-DD   First day of the snapshot (default: 2024-01-01).");
        Console.WriteLine("  --account L=ID       Pair an export label with a plan account; may repeat.");
        Console.WriteLine("  --own-iban L=IBAN    An account number an export never prints; may repeat.");
        Console.WriteLine("  --expect-balance L=A A balance an export does not carry, as the bank app shows it.");
        Console.WriteLine("  --journal FILE       The undo journal (default: artifacts/ynab/journal.jsonl).");
        Console.WriteLine("  --plan-id ID         The plan an undo belongs to.");
        Console.WriteLine("  --apply              Do it; without it, every changing command is a dry run.");
        Console.WriteLine("  --csv DIR            Where the rows to add are written for importing (free of charge).");
        Console.WriteLine("  --create             Add the rows through the API as well, instead of importing them.");
        Console.WriteLine("  --no-balance-repair  Leave the ledger alone; only report.");
        Console.WriteLine("  --no-categorize      Do not carry the category proposals into the plan.");
        Console.WriteLine("  --no-duplicates      Leave duplicated transactions where they are.");
        Console.WriteLine("  --keep-extras        Leave plan rows the export does not have; only report.");
        Console.WriteLine();
        Console.WriteLine($"Reads the access token from ${TokenVariable}.");
    }
}

[Serializable]
public sealed class MissingTokenException(string variable)
    : Exception($"{variable} is not set. Create a personal access token in YNAB under Developer Settings.");
