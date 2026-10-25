using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Apply;

/// <summary>What an apply or an undo actually did, including what it could not do.</summary>
public sealed record ApplyResult(
    int Created,
    int AlreadyThere,
    int Deleted,
    int Categorized,
    int Moved,
    IReadOnlyList<string> Problems
)
{
    public bool Clean => Problems.Count == 0;
}

/// <summary>
/// Applies a repair one action at a time, writing each one down before moving to the next. A
/// failure is reported and the rest still run, because half a repair that is written down is
/// recoverable and a repair that stopped silently in the middle is not.
/// </summary>
public static class PlanApplier
{
    public static async Task<ApplyResult> ApplyAsync(
        RepairPlan plan,
        IYnabGateway gateway,
        string journalPath,
        CancellationToken cancellationToken = default
    )
    {
        var problems = new List<string>();
        var created = 0;
        var alreadyThere = 0;
        var deleted = 0;
        var categorized = 0;
        var moved = 0;

        // A row that belongs in another account is a move, not a delete plus a create: it changes
        // neither the money itself nor the category it was given.
        foreach (var move in plan.Moves)
        {
            try
            {
                await gateway.MoveAsync(plan.PlanId, move.TransactionId, move.ToAccountId, cancellationToken);
                moved++;
                Journal.Append(
                    journalPath,
                    new JournalEntry(
                        At: DateTimeOffset.UtcNow,
                        Kind: JournalEntry.MovedKind,
                        Detail: $"{move.FromLabel} -> {move.ToLabel}: {move.Date:yyyy-MM-dd} {move.Amount} {move.Description}",
                        TransactionId: move.TransactionId,
                        Created: null,
                        Deleted: null,
                        MovedFromAccountId: move.FromAccountId
                    )
                );
            }
            catch (YnabApiException exception)
            {
                problems.Add($"move {move.TransactionId}: {exception.Message}");
            }
        }

        // Removal comes before writing: deleting one leg of a transfer takes the other leg with it, and
        // a row the plan wants back has to be written after that has happened, not before.
        foreach (var delete in plan.Deletes)
        {
            try
            {
                var before = await gateway.DeleteAsync(plan.PlanId, delete.Transaction.Id, cancellationToken);
                deleted++;
                Journal.Append(
                    journalPath,
                    new JournalEntry(
                        At: DateTimeOffset.UtcNow,
                        Kind: JournalEntry.DeletedKind,
                        Detail: $"{delete.AccountName} {before.Date:yyyy-MM-dd} {before.Amount:0.00} {before.Description}",
                        TransactionId: before.Id,
                        Created: null,
                        Deleted: before
                    )
                );
            }
            catch (YnabApiException exception) when (exception.Status == System.Net.HttpStatusCode.NotFound)
            {
                // Already gone — the other leg of a transfer takes both with it. Nothing to report.
                alreadyThere++;
            }
            catch (YnabApiException exception)
            {
                problems.Add($"delete {delete.Transaction.Id}: {exception.Message}");
            }
        }

        foreach (var create in plan.Creates)
        {
            try
            {
                var id = await WriteAsync(plan.PlanId, create, gateway, cancellationToken);

                if (id is null)
                {
                    // The import id is already in the plan and the row with it: this row was
                    // applied before, which is the point of a deterministic import id.
                    alreadyThere++;
                    continue;
                }

                created++;
                Journal.Append(
                    journalPath,
                    new JournalEntry(
                        At: DateTimeOffset.UtcNow,
                        Kind: JournalEntry.CreatedKind,
                        Detail: $"{create.AccountName} {create.Date:yyyy-MM-dd} {create.Amount:0.00} {create.PayeeName}",
                        TransactionId: id,
                        Created: create,
                        Deleted: null
                    )
                );
            }
            catch (YnabApiException exception)
            {
                problems.Add($"create {create.Date:yyyy-MM-dd} {create.Amount:0.00} {create.PayeeName}: {exception.Message}");
            }
        }

        foreach (var change in plan.Categories)
        {
            try
            {
                await gateway.CategorizeAsync(plan.PlanId, change.TransactionId, change.CategoryId, cancellationToken);
                categorized++;
                Journal.Append(
                    journalPath,
                    new JournalEntry(
                        At: DateTimeOffset.UtcNow,
                        Kind: JournalEntry.CategorizedKind,
                        Detail: $"{change.TransactionId} -> {change.CategoryName} ({change.Why})",
                        TransactionId: change.TransactionId,
                        Created: null,
                        Deleted: null
                    )
                );
            }
            catch (YnabApiException exception)
            {
                problems.Add($"categorize {change.TransactionId}: {exception.Message}");
            }
        }

        return new ApplyResult(created, alreadyThere, deleted, categorized, moved, problems);
    }

    /// <summary>
    /// Writes one row and answers with its id, or with null when the plan already has it. YNAB keeps
    /// the import id of a transaction after the transaction itself is gone, so a row this tool created
    /// and a later repair removed can never be written under that id again — a repair would then
    /// propose the same create on every pass and never converge. Such an id is taken again with a
    /// suffix; an id whose row is still there is left alone.
    /// </summary>
    private static async Task<string?> WriteAsync(
        string planId,
        PlannedCreate create,
        IYnabGateway gateway,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await gateway.CreateAsync(planId, create, cancellationToken);
        }
        catch (YnabApiException exception) when (exception.Status == System.Net.HttpStatusCode.Conflict)
        {
            if (await IsInThePlanAsync(planId, create, gateway, cancellationToken))
            {
                return null;
            }

            return await gateway.CreateAsync(planId, create with { ImportId = TakenAgain(create.ImportId) }, cancellationToken);
        }
    }

    /// <summary>The row is in the plan now, not merely its id on record.</summary>
    private static async Task<bool> IsInThePlanAsync(
        string planId,
        PlannedCreate create,
        IYnabGateway gateway,
        CancellationToken cancellationToken
    )
    {
        var from = new DateOnly(create.Date.Year, create.Date.Month, 1);
        var month = await gateway.TransactionsAsync(planId, from, from.AddMonths(1).AddDays(-1), cancellationToken);

        return month.Any(transaction => string.Equals(transaction.ImportId, create.ImportId, StringComparison.Ordinal));
    }

    /// <summary>An import id YNAB accepts (at most 36 characters) that no row of ours has held before.</summary>
    private static string TakenAgain(string importId) =>
        importId.Length <= 32 ? importId + "-r2" : importId[..32] + "-r2";

    /// <summary>
    /// Puts back what the journal says was taken, newest first: a deleted transaction is recreated
    /// with the fields it had, a created one is removed.
    /// </summary>
    public static async Task<ApplyResult> UndoAsync(
        string journalPath,
        string planId,
        IYnabGateway gateway,
        CancellationToken cancellationToken = default
    )
    {
        var problems = new List<string>();
        var created = 0;
        var deleted = 0;
        var moved = 0;

        foreach (var entry in Journal.Read(journalPath).Reverse())
        {
            try
            {
                switch (entry)
                {
                    case { Kind: JournalEntry.CreatedKind, TransactionId: { } id }:
                        await gateway.DeleteAsync(planId, id, cancellationToken);
                        deleted++;
                        break;

                    case { Kind: JournalEntry.DeletedKind, Deleted: { } gone }:
                        // The import id the row had is the one its own deletion took away, so putting
                        // it back has to cope with that id being held — the same way a create does.
                        await WriteAsync(
                            planId,
                            new PlannedCreate(
                                AccountId: gone.AccountId,
                                AccountName: gone.AccountName,
                                Date: gone.Date,
                                Amount: gone.Amount,
                                PayeeName: gone.PayeeName ?? string.Empty,
                                Memo: gone.Memo ?? string.Empty,
                                ImportId: gone.ImportId ?? string.Empty,
                                CategoryId: gone.CategoryId
                            ),
                            gateway,
                            cancellationToken
                        );
                        created++;
                        break;

                    case { Kind: JournalEntry.MovedKind, TransactionId: { } movedId, MovedFromAccountId: { } back }:
                        await gateway.MoveAsync(planId, movedId, back, cancellationToken);
                        moved++;
                        break;
                }
            }
            catch (YnabApiException exception)
            {
                problems.Add($"undo {entry.Kind} {entry.TransactionId}: {exception.Message}");
            }
        }

        return new ApplyResult(created, 0, deleted, 0, moved, problems);
    }
}
