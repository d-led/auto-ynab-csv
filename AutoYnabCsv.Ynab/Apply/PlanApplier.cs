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

        foreach (var create in plan.Creates)
        {
            try
            {
                var id = await gateway.CreateAsync(plan.PlanId, create, cancellationToken);
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
            catch (YnabApiException exception) when (exception.Status == System.Net.HttpStatusCode.Conflict)
            {
                // The import id is already in the plan: this row was applied before, which is the
                // point of a deterministic import id. Nothing to do, nothing to report.
                alreadyThere++;
            }
            catch (YnabApiException exception)
            {
                problems.Add($"create {create.Date:yyyy-MM-dd} {create.Amount:0.00} {create.PayeeName}: {exception.Message}");
            }
        }

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
                        await gateway.CreateAsync(
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
