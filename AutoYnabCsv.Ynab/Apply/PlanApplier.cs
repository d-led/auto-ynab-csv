using System.Security.Cryptography;
using AutoYnabCsv.Contracts;
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

        var present = await PresentAsync(plan, gateway, cancellationToken);

        foreach (var create in plan.Creates)
        {
            try
            {
                if (present.Contains(KeyOf(create)))
                {
                    // Reading before writing, because a create that succeeds is never refused, and a
                    // second apply would then write the row again under the id it was given last time.
                    alreadyThere++;
                    continue;
                }

                var id = await WriteAsync(plan.PlanId, create, gateway, cancellationToken);

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
    /// What is already in the plan, for the months the plan touches: one read per month rather than one
    /// per row, and the only reliable question to ask — an import id says what a row was written under,
    /// not whether the row is there.
    /// </summary>
    private static async Task<HashSet<string>> PresentAsync(
        RepairPlan plan,
        IYnabGateway gateway,
        CancellationToken cancellationToken
    )
    {
        var present = new HashSet<string>(StringComparer.Ordinal);

        foreach (var month in plan.Creates.Select(create => new DateOnly(create.Date.Year, create.Date.Month, 1)).Distinct())
        {
            var transactions = await gateway.TransactionsAsync(
                plan.PlanId,
                month,
                month.AddMonths(1).AddDays(-1),
                cancellationToken
            );

            foreach (var transaction in transactions)
            {
                present.Add(KeyOf(transaction.AccountId, transaction.Date, transaction.Amount));
            }
        }

        return present;
    }

    private static string KeyOf(PlannedCreate create) => KeyOf(create.AccountId, create.Date, create.Amount);

    /// <summary>
    /// What the bank says happened: where, when and how much. Not the payee — YNAB writes its own name
    /// for a transfer, and a row of ours is written without the prefix this tool puts in front of one.
    /// </summary>
    private static string KeyOf(string accountId, DateOnly date, Money amount) =>
        $"{accountId}|{date:yyyy-MM-dd}|{amount.Amount:0.00}";

    /// <summary>
    /// Writes one row and answers with its id, or with null when the plan already has it. YNAB keeps
    /// the import id of a transaction after the transaction itself is gone, so a row this tool created
    /// and a later repair removed can never be written under that id again — a repair would then
    /// propose the same create on every pass and never converge. Such an id is taken again with a
    /// suffix; an id whose row is still there is left alone.
    /// </summary>
    private static async Task<string> WriteAsync(
        string planId,
        PlannedCreate create,
        IYnabGateway gateway,
        CancellationToken cancellationToken
    )
    {
        // The API refuses a payee that starts with one of its own internal names, and it says so only
        // after the row is sent. A row that is not a transfer therefore names the far side plainly.
        var sent = create.TransferAccountId is null
            ? create with { PayeeName = TransferNaming.WithoutPrefix(create.PayeeName) }
            : create;

        try
        {
            return await gateway.CreateAsync(planId, sent, cancellationToken);
        }
        catch (YnabApiException exception) when (exception.Status == System.Net.HttpStatusCode.Conflict)
        {
            return await gateway.CreateAsync(planId, sent with { ImportId = TakenAgain(sent.ImportId) }, cancellationToken);
        }
    }

    /// <summary>
    /// An import id YNAB accepts (at most 36 characters) that no row of ours has held before. It has
    /// to be different on every attempt: the id of a deleted row stays taken, and a second attempt
    /// under the same derived id would be refused exactly like the first.
    /// </summary>
    private static string TakenAgain(string importId)
    {
        var suffix = $"-{RandomNumberGenerator.GetHexString(6).ToLowerInvariant()}";
        var room = 36 - suffix.Length;

        return (importId.Length <= room ? importId : importId[..room]) + suffix;
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
