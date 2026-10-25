using System.Net;
using AutoYnabCsv.Ynab;
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Contracts;

namespace Tests.Ynab;

/// <summary>A plan that answers like YNAB without a network: it remembers import ids, so applying
/// the same plan twice is a real test and not a mock of one.</summary>
internal sealed class FakeGateway : IYnabGateway
{
    private readonly List<YnabTransaction> _transactions = [];
    private readonly HashSet<string> _importIds = new(StringComparer.Ordinal);
    private int _next;

    public IReadOnlyList<YnabTransaction> Transactions => _transactions;

    public Task<IReadOnlyList<YnabPlanRef>> PlansAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<YnabPlanRef>>([new YnabPlanRef("plan-1", "Household")]);

    public Task<IReadOnlyList<YnabAccount>> AccountsAsync(string planId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<YnabAccount>>([]);

    public Task<IReadOnlyList<YnabCategory>> CategoriesAsync(string planId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<YnabCategory>>([]);

    public Task<IReadOnlyList<YnabPayee>> PayeesAsync(string planId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<YnabPayee>>([]);

    public Task<IReadOnlyList<YnabTransaction>> TransactionsAsync(
        string planId,
        DateOnly since,
        DateOnly until,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<YnabTransaction>>([.. _transactions]);

    public Task<string> CreateAsync(string planId, PlannedCreate create, CancellationToken cancellationToken = default)
    {
        if (!_importIds.Add(create.ImportId))
        {
            throw new YnabApiException(HttpStatusCode.Conflict, "duplicate import id");
        }

        var id = $"new-{++_next}";
        _transactions.Add(
            new YnabTransaction(
                id,
                create.Date,
                create.Amount,
                create.AccountId,
                create.AccountName,
                PayeeId: null,
                PayeeName: create.PayeeName,
                CategoryId: create.CategoryId,
                CategoryName: null,
                Memo: create.Memo,
                Cleared: true,
                Approved: false,
                TransferAccountId: null,
                ImportId: create.ImportId
            )
        );

        return Task.FromResult(id);
    }

    public Task<YnabTransaction> DeleteAsync(
        string planId,
        string transactionId,
        CancellationToken cancellationToken = default
    )
    {
        var gone = _transactions.SingleOrDefault(transaction => transaction.Id == transactionId)
            ?? throw new YnabApiException(HttpStatusCode.NotFound, "no such transaction");

        _transactions.Remove(gone);

        // YNAB removes both legs of a transfer with one delete, and this is the behaviour that made a
        // repair loop: the leg being removed took a correct row with it.
        if (gone.TransferAccountId is { } other)
        {
            _transactions.RemoveAll(candidate =>
                candidate.AccountId == other && candidate.Date == gone.Date && candidate.Amount == -gone.Amount
            );
        }

        return Task.FromResult(gone);
    }

    public Task CategorizeAsync(
        string planId,
        string transactionId,
        string categoryId,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task MoveAsync(
        string planId,
        string transactionId,
        string accountId,
        CancellationToken cancellationToken = default
    )
    {
        var index = _transactions.FindIndex(transaction => transaction.Id == transactionId);
        if (index < 0)
        {
            throw new YnabApiException(HttpStatusCode.NotFound, "no such transaction");
        }

        _transactions[index] = _transactions[index] with { AccountId = accountId };
        return Task.CompletedTask;
    }

    public Task AssignAsync(
        string planId,
        DateOnly month,
        string categoryId,
        long budgetedMilliunits,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public void Seed(YnabTransaction transaction) => _transactions.Add(transaction);

    /// <summary>
    /// An import id YNAB still holds for a row that is gone: deleting a transaction does not release
    /// the id it was written under.
    /// </summary>
    public FakeGateway WithBurntImportId(string importId)
    {
        _importIds.Add(importId);
        return this;
    }
}
