using System.Text;
using AutoYnabCsv.Ynab.Apply;

namespace AutoYnabCsv.Ynab.Report;

/// <summary>
/// A repair plan as tables: what would be added, moved and removed. Reading a plan should not need a
/// JSON viewer, and the rows that change the most money are the ones worth looking at first.
/// </summary>
public static class PlanSummary
{
    public const int LargestShown = 15;

    public static string Markdown(RepairPlan plan, int largest = LargestShown)
    {
        var text = new StringBuilder();

        text.AppendLine(
            $"# Repair plan — {plan.Creates.Count} to add, {plan.Moves.Count} to move, "
                + $"{plan.Deletes.Count} to remove, {plan.Categories.Count} to categorize"
        );
        text.AppendLine();

        foreach (var note in plan.Notes)
        {
            text.AppendLine($"- {note}");
        }

        if (plan.Notes.Count > 0)
        {
            text.AppendLine();
        }

        Creates(text, plan);
        Moves(text, plan);
        Deletes(text, plan, largest);

        return text.ToString();
    }

    private static void Creates(StringBuilder text, RepairPlan plan)
    {
        if (plan.Creates.Count == 0)
        {
            return;
        }

        text.AppendLine($"## Add — in the bank, not in the plan ({plan.Creates.Count})");
        text.AppendLine();
        text.AppendLine("| date | account | amount | payee | memo | transfer to |");
        text.AppendLine("| --- | --- | ---: | --- | --- | --- |");

        foreach (var create in plan.Creates.OrderBy(create => create.Date))
        {
            text.AppendLine(
                $"| {create.Date:yyyy-MM-dd} | {Cell(create.AccountName, 20)} | {Amount(create.Amount.Amount)} "
                    + $"| {Cell(create.PayeeName, 30)} | {Cell(create.Memo, 40)} "
                    + $"| {(create.TransferAccountId is null ? string.Empty : "yes")} |"
            );
        }

        text.AppendLine();
    }

    private static void Moves(StringBuilder text, RepairPlan plan)
    {
        if (plan.Moves.Count == 0)
        {
            return;
        }

        text.AppendLine($"## Move — right amount, wrong account ({plan.Moves.Count})");
        text.AppendLine();
        text.AppendLine("| date | amount | from | to | memo |");
        text.AppendLine("| --- | ---: | --- | --- | --- |");

        foreach (var move in plan.Moves.OrderBy(move => move.Date))
        {
            text.AppendLine(
                $"| {move.Date:yyyy-MM-dd} | {Amount(move.Amount.Amount)} | {Cell(move.FromLabel, 18)} "
                    + $"| {Cell(move.ToLabel, 18)} | {Cell(move.Description, 40)} |"
            );
        }

        text.AppendLine();
    }

    private static void Deletes(StringBuilder text, RepairPlan plan, int largest)
    {
        if (plan.Deletes.Count == 0)
        {
            return;
        }

        var shown = plan.Deletes.OrderByDescending(delete => Math.Abs(delete.Transaction.Amount.Amount)).Take(largest).ToList();

        text.AppendLine(
            shown.Count == plan.Deletes.Count
                ? $"## Remove — in the plan, not in the bank ({plan.Deletes.Count})"
                : $"## Remove — in the plan, not in the bank (largest {shown.Count} of {plan.Deletes.Count})"
        );
        text.AppendLine();
        text.AppendLine("| date | account | amount | payee | memo | why |");
        text.AppendLine("| --- | --- | ---: | --- | --- | --- |");

        foreach (var delete in shown)
        {
            var transaction = delete.Transaction;
            text.AppendLine(
                $"| {transaction.Date:yyyy-MM-dd} | {Cell(delete.AccountName, 18)} | {Amount(transaction.Amount.Amount)} "
                    + $"| {Cell(transaction.PayeeName ?? string.Empty, 28)} | {Cell(transaction.Memo ?? string.Empty, 40)} "
                    + $"| {Cell(delete.Why, 40)} |"
            );
        }

        text.AppendLine();
    }

    private static string Amount(decimal amount) => Table.Amount(amount);

    private static string Cell(string value, int width) => Table.Cell(value, width);
}
