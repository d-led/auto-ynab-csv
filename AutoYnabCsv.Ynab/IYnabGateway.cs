using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab;

/// <summary>A plan, as the API lists it.</summary>
public sealed record YnabPlanRef(string Id, string Name);

/// <summary>
/// Everything the tool needs from YNAB, and nothing else. The reconciliation is pure and answers
/// from a snapshot; only applying a repair and reading a snapshot need this, so the decisions can be
/// tested without a network and a token can be wrong without breaking anything else.
/// </summary>
public interface IYnabGateway
{
    Task<IReadOnlyList<YnabPlanRef>> PlansAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<YnabAccount>> AccountsAsync(string planId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<YnabCategory>> CategoriesAsync(string planId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<YnabPayee>> PayeesAsync(string planId, CancellationToken cancellationToken = default);

    /// <summary>Every transaction from <paramref name="since"/> to <paramref name="until"/>, both ends included.</summary>
    Task<IReadOnlyList<YnabTransaction>> TransactionsAsync(
        string planId,
        DateOnly since,
        DateOnly until,
        CancellationToken cancellationToken = default
    );

    /// <summary>Creates one transaction and returns its id; a repeated import id returns the first one.</summary>
    Task<string> CreateAsync(string planId, Apply.PlannedCreate create, CancellationToken cancellationToken = default);

    /// <summary>Deletes one transaction and returns what it was, so it can be put back.</summary>
    Task<YnabTransaction> DeleteAsync(string planId, string transactionId, CancellationToken cancellationToken = default);

    Task CategorizeAsync(
        string planId,
        string transactionId,
        string categoryId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Puts a transaction into another account, keeping everything else about it.</summary>
    Task MoveAsync(
        string planId,
        string transactionId,
        string accountId,
        CancellationToken cancellationToken = default
    );

    Task AssignAsync(
        string planId,
        DateOnly month,
        string categoryId,
        long budgetedMilliunits,
        CancellationToken cancellationToken = default
    );
}
