using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Snapshot;

/// <summary>
/// Reads the plan once and completely. Transactions are asked for a month at a time: the answers
/// stay small, a failure costs one month instead of everything, and no request can quietly stop at
/// an API default instead of the first day of the export.
/// </summary>
public static class YnabSnapshotService
{
    public static async Task<YnabSnapshot> ReadAsync(
        IYnabGateway gateway,
        string planId,
        DateOnly since,
        Action<string>? say = null,
        CancellationToken cancellationToken = default
    )
    {
        var plan = (await gateway.PlansAsync(cancellationToken)).FirstOrDefault(candidate => candidate.Id == planId)
            ?? throw new PlanNotFoundException(planId);

        say?.Invoke($"plan: {plan.Name}");

        var accounts = await gateway.AccountsAsync(planId, cancellationToken);
        say?.Invoke($"accounts: {accounts.Count}");

        var categories = await gateway.CategoriesAsync(planId, cancellationToken);
        var payees = await gateway.PayeesAsync(planId, cancellationToken);

        var transactions = new List<YnabTransaction>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (var from = since; from <= today; from = from.AddMonths(1))
        {
            var until = from.AddMonths(1).AddDays(-1);
            if (until > today)
            {
                until = today;
            }

            var batch = await gateway.TransactionsAsync(planId, from, until, cancellationToken);
            transactions.AddRange(batch);
            say?.Invoke($"transactions {from:yyyy-MM}: {batch.Count}");
        }

        return new YnabSnapshot(
            PlanId: planId,
            PlanName: plan.Name,
            CurrencyFormat: string.Empty,
            FetchedAt: DateTimeOffset.UtcNow,
            Accounts: accounts,
            Categories: categories,
            Payees: payees,
            Transactions: [.. transactions.GroupBy(transaction => transaction.Id).Select(group => group.First())]
        );
    }
}

[Serializable]
public sealed class PlanNotFoundException(string planId)
    : Exception($"No plan with id {planId} is visible to this token.");
