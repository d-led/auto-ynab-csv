using System.Globalization;
using System.Text;
using System.Text.Json;
using AutoYnabCsv.Ynab.Categorize;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace AutoYnabCsv.Ynab.Report;

/// <summary>
/// The whole argument on one page: what each account holds on both sides, where the two ledgers stop
/// agreeing, what is missing, what should not be there, and the handful of entries whose category is
/// a guess. A person reads this once and knows what to decide; nothing else has to be explained.
/// </summary>
public static class ReconcileReport
{
    public const string MarkdownFileName = "reconcile.md";

    public const string JsonFileName = "reconcile.json";

    /// <summary>More than this and the list stops being a decision and becomes a chore.</summary>
    public const int MaxReviewItems = 50;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static (string MarkdownPath, string JsonPath) WriteAll(
        Reconciliation reconciliation,
        IReadOnlyList<CategoryProposal> proposals,
        string directory
    )
    {
        Directory.CreateDirectory(directory);
        var markdownPath = Path.Combine(directory, MarkdownFileName);
        var jsonPath = Path.Combine(directory, JsonFileName);

        File.WriteAllText(markdownPath, Markdown(reconciliation, proposals));
        File.WriteAllText(jsonPath, Json(reconciliation, proposals));

        return (markdownPath, jsonPath);
    }

    public static string Json(Reconciliation reconciliation, IReadOnlyList<CategoryProposal> proposals) =>
        JsonSerializer.Serialize(new { reconciliation, proposals }, JsonOptions);

    public static string Markdown(Reconciliation reconciliation, IReadOnlyList<CategoryProposal> proposals)
    {
        var text = new StringBuilder();

        text.AppendLine($"# Reconciliation — {reconciliation.Snapshot.PlanName}");
        text.AppendLine();
        text.AppendLine($"Snapshot read {reconciliation.Snapshot.FetchedAt:yyyy-MM-dd HH:mm} UTC.");
        text.AppendLine();
        Summary(text, reconciliation);
        Balances(text, reconciliation);
        Divergence(text, reconciliation);
        Missing(text, reconciliation);
        Extras(text, reconciliation);
        Categories(text, proposals);

        return text.ToString();
    }

    private static void Summary(StringBuilder text, Reconciliation reconciliation)
    {
        var paired = reconciliation.Accounts.Count;
        var unresolved = reconciliation.Unresolved.Count;
        var off = reconciliation.Accounts.Count(account => account.Balances == false);

        text.AppendLine("## Summary");
        text.AppendLine();
        text.AppendLine($"- {paired} account(s) paired with an export, {unresolved} unresolved.");
        text.AppendLine($"- {off} account(s) do not hold what the export says.");
        text.AppendLine(
            $"- {reconciliation.Transfers.Count} movement(s) are missing on both sides of a transfer between two exported accounts; "
                + "each is added once"
        );
        var byHand = reconciliation.Accounts.Where(account => !account.BalanceFromExport).ToList();
        if (byHand.Count > 0)
        {
            text.AppendLine(
                $"- {byHand.Count} balance(s) were told by hand, not read from the export: "
                    + string.Join(", ", byHand.Select(account => account.Label))
            );
        }        text.AppendLine($"- Repairing every missing and extra row would move the plan by {Amount(reconciliation.TotalRepairDelta)}.");

        if (reconciliation.Unresolved.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Unresolved — name these accounts or pass `--account \"<label>=<account id>\"`:");
            text.AppendLine();

            foreach (var statement in reconciliation.Unresolved)
            {
                text.AppendLine($"- `{statement.Label}` ({Path.GetFileName(statement.SourceFile)})");
            }
        }

        text.AppendLine();
    }

    private static void Balances(StringBuilder text, Reconciliation reconciliation)
    {
        text.AppendLine("## Balances");
        text.AppendLine();
        text.AppendLine("| account | export says | plan at cutoff | difference | after repair |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: |");

        foreach (var account in reconciliation.Accounts)
        {
            text.AppendLine(
                $"| {Cell(account.Label)}{(account.BalanceFromExport ? string.Empty : " †")} | {Amount(account.ExportBalance)} "
                + $"| {Amount(account.PlanBalanceAtCutoff)} | {Signed(account.Difference)} | {Signed(account.DifferenceAfterRepair)} |"
            );
        }

        text.AppendLine();
    }

    private static void Divergence(StringBuilder text, Reconciliation reconciliation)
    {
        var dated = reconciliation.Accounts.Where(account => account.Gap is not null).ToList();
        if (dated.Count == 0)
        {
            return;
        }

        text.AppendLine("## Where the ledgers stop agreeing");
        text.AppendLine();

        foreach (var account in dated)
        {
            var gap = account.Gap!;
            text.AppendLine(
                gap.LedgersAgree
                    ? $"- **{account.Label}**: the two ledgers agree day by day."
                    : $"- **{account.Label}**: they disagree from {gap.DivergesFrom:yyyy-MM-dd} "
                        + $"to {gap.DivergesTo:yyyy-MM-dd} ({gap.Days} day(s)), by up to {Amount(gap.WorstDifference)}."
            );
        }

        text.AppendLine();
    }

    private static void Missing(StringBuilder text, Reconciliation reconciliation)
    {
        var rows = reconciliation.Accounts.SelectMany(account => account.MissingInPlan.Select(row => (account, row))).ToList();
        if (rows.Count == 0)
        {
            text.AppendLine("## Missing from the plan");
            text.AppendLine();
            text.AppendLine("Nothing: every row the export shows is in the plan.");
            text.AppendLine();
            return;
        }

        text.AppendLine($"## Missing from the plan — {rows.Count} row(s), {Amount(Money.Sum(rows.Select(pair => pair.row.Amount)))}");
        text.AppendLine();
        text.AppendLine("| date | account | amount | payee | memo | moves to |");
        text.AppendLine("| --- | --- | ---: | --- | --- | --- |");

        foreach (var (account, row) in rows.OrderBy(pair => pair.row.Date))
        {
            text.AppendLine(
                $"| {row.Date:yyyy-MM-dd} | {Cell(account.Label)} | {Amount(row.Amount)} | {Cell(row.Payee)} "
                + $"| {Cell(row.Memo)} | {Cell(account.TransferTargetOf(row) ?? string.Empty)} |"
            );
        }

        text.AppendLine();
    }

    private static void Extras(StringBuilder text, Reconciliation reconciliation)
    {
        var extras = reconciliation.Accounts
            .SelectMany(account => account.OnlyInPlan.Select(transaction => (account, transaction)))
            .ToList();
        var duplicates = reconciliation.Accounts
            .SelectMany(account => account.Duplicates.Select(duplicate => (account, duplicate)))
            .ToList();
        var adjustments = reconciliation.Accounts
            .SelectMany(account => account.Adjustments.Select(adjustment => (account, adjustment)))
            .ToList();

        text.AppendLine($"## In the plan but not in the export — {extras.Count + duplicates.Count} row(s)");
        text.AppendLine();

        if (duplicates.Count > 0)
        {
            text.AppendLine("### Looks like the same movement twice");
            text.AppendLine();
            text.AppendLine("| date | account | amount | kept as | duplicate |");
            text.AppendLine("| --- | --- | ---: | --- | --- |");

            foreach (var (account, duplicate) in duplicates.OrderBy(pair => pair.duplicate.Extra.Date))
            {
                text.AppendLine(
                    $"| {duplicate.Extra.Date:yyyy-MM-dd} | {Cell(account.Label)} | {Amount(duplicate.Extra.Amount)} "
                    + $"| {Cell(duplicate.Kept.Description)} | {Cell(duplicate.Extra.Description)} |"
                );
            }

            text.AppendLine();
        }

        if (adjustments.Count > 0)
        {
            text.AppendLine("### Corrections that may be holding a gap shut");
            text.AppendLine();

            foreach (var (account, adjustment) in adjustments.OrderBy(pair => pair.adjustment.Transaction.Date))
            {
                var transaction = adjustment.Transaction;
                text.AppendLine(
                    $"- **{account.Label}** {transaction.Date:yyyy-MM-dd} {Amount(transaction.Amount)} "
                    + $"`{Cell(transaction.Description)}` — {adjustment.Why}"
                );
            }

            text.AppendLine();
        }

        var plain = extras
            .Where(pair => !adjustments.Any(adjustment => adjustment.adjustment.Transaction.Id == pair.transaction.Id))
            .ToList();

        if (plain.Count == 0)
        {
            return;
        }

        text.AppendLine($"### No bank row at all — {plain.Count}");
        text.AppendLine();
        text.AppendLine("| date | account | amount | payee | category | memo |");
        text.AppendLine("| --- | --- | ---: | --- | --- | --- |");

        foreach (var (account, transaction) in plain.OrderBy(pair => pair.transaction.Date))
        {
            text.AppendLine(
                $"| {transaction.Date:yyyy-MM-dd} | {Cell(account.Label)} | {Amount(transaction.Amount)} "
                + $"| {Cell(transaction.PayeeName ?? string.Empty)} | {Cell(transaction.CategoryName ?? "—")} "
                + $"| {Cell(transaction.Memo ?? string.Empty)} |"
            );
        }

        text.AppendLine();
    }

    private static void Categories(StringBuilder text, IReadOnlyList<CategoryProposal> proposals)
    {
        var confident = proposals.Where(proposal => proposal.IsConfident).ToList();
        var unsure = proposals.Where(proposal => !proposal.IsConfident).ToList();

        text.AppendLine("## Categories");
        text.AppendLine();
        text.AppendLine($"- {confident.Count} transaction(s) can be filed from the plan's own history.");
        text.AppendLine($"- {unsure.Count} need a decision; the {Math.Min(unsure.Count, MaxReviewItems)} largest are below.");
        text.AppendLine();

        text.AppendLine("### Needs a decision");
        text.AppendLine();

        if (unsure.Count == 0)
        {
            text.AppendLine("Nothing.");
            text.AppendLine();
            return;
        }

        text.AppendLine("| date | amount | payee | memo | best guess | why not |");
        text.AppendLine("| --- | ---: | --- | --- | --- | --- |");

        foreach (var proposal in unsure.Take(MaxReviewItems))
        {
            var transaction = proposal.Transaction;
            text.AppendLine(
                $"| {transaction.Date:yyyy-MM-dd} | {Amount(transaction.Amount)} | {Cell(transaction.PayeeName ?? string.Empty)} "
                + $"| {Cell(transaction.Memo ?? string.Empty)} | {Cell(proposal.Category.Name)} | {proposal.Why} |"
            );
        }

        if (unsure.Count > MaxReviewItems)
        {
            text.AppendLine();
            text.AppendLine($"… and {unsure.Count - MaxReviewItems} more in `{JsonFileName}`.");
        }

        text.AppendLine();
    }

    private static string Amount(Money? value) => value is null ? "unknown" : value.Value.ToString();

    private static string Signed(Money? value) =>
        value is null ? "unknown"
        : value.Value.IsZero ? Money.Zero.ToString()
        : value.Value.ToString("+0.00;-0.00", CultureInfo.InvariantCulture);

    private static string Cell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal).Replace('\n', ' ').Trim();
}
