using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Reconcile;

/// <summary>An export and the plan account it belongs to, with the reason it was chosen.</summary>
public sealed record AccountAssignment(BankStatement Statement, YnabAccount Account, string Evidence);

/// <summary>
/// Which plan account each exported statement is about. The export carries no account id that the
/// plan knows, so the pairing is argued rather than guessed, and every pairing says what argued for
/// it: a name, the last four digits of an IBAN, or a balance nobody else holds. What cannot be
/// argued stays unresolved instead of being attached to the closest-looking account.
/// </summary>
public sealed record AccountMapping(
    IReadOnlyList<AccountAssignment> Assignments,
    IReadOnlyList<BankStatement> Unresolved
)
{
    public static readonly AccountMapping Empty = new([], []);
}

public static class AccountMappingResolver
{
    private static readonly Money BalanceTolerance = 1.00m;

    public static AccountMapping Resolve(
        IReadOnlyList<BankStatement> statements,
        IReadOnlyList<YnabAccount> accounts,
        IReadOnlyDictionary<string, string> overrides
    )
    {
        var byLabel = new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var assignments = new List<AccountAssignment>();
        var unresolved = new List<BankStatement>();

        foreach (var statement in statements)
        {
            var chosen = ByOverride(statement, accounts, byLabel)
                ?? ByName(statement, accounts, claimed)
                ?? ByIban(statement, accounts, claimed)
                ?? ByBalance(statement, accounts, claimed);

            if (chosen is null)
            {
                unresolved.Add(statement);
                continue;
            }

            claimed.Add(chosen.Account.Id);
            assignments.Add(chosen);
        }

        return new AccountMapping(assignments, unresolved);
    }

    private static AccountAssignment? ByOverride(
        BankStatement statement,
        IReadOnlyList<YnabAccount> accounts,
        IReadOnlyDictionary<string, string> overrides
    ) =>
        overrides.TryGetValue(statement.Label, out var id)
        && accounts.FirstOrDefault(account => account.Id == id) is { } account
            ? new AccountAssignment(statement, account, "chosen by hand")
            : null;

    /// <summary>One account is named for the other, and it is the only one that is.</summary>
    private static AccountAssignment? ByName(
        BankStatement statement,
        IReadOnlyList<YnabAccount> accounts,
        IReadOnlySet<string> claimed
    )
    {
        var label = NameMatch.Normalize(statement.Label);
        var candidates = accounts
            .Where(account => !claimed.Contains(account.Id))
            .Where(account => Named(account, label))
            .ToList();

        return candidates.Count == 1
            ? new AccountAssignment(statement, candidates[0], $"the name ('{candidates[0].Name}')")
            : null;
    }

    private static bool Named(YnabAccount account, string label)
    {
        var name = NameMatch.Normalize(account.Name);
        return name.Length > 0
            && label.Length > 0
            && (name.Contains(label, StringComparison.Ordinal) || label.Contains(name, StringComparison.Ordinal));
    }

    /// <summary>The export prints an IBAN; an account named after its last digits is the one.</summary>
    private static AccountAssignment? ByIban(
        BankStatement statement,
        IReadOnlyList<YnabAccount> accounts,
        IReadOnlySet<string> claimed
    )
    {
        var iban = statement.Statement.AccountIdentifier;
        if (iban.Length < 4)
        {
            return null;
        }

        var lastFour = iban[^4..];
        var candidates = accounts
            .Where(account => !claimed.Contains(account.Id))
            .Where(account => account.Name.Contains(lastFour, StringComparison.Ordinal))
            .ToList();

        return candidates.Count == 1
            ? new AccountAssignment(statement, candidates[0], $"the account number {lastFour}")
            : null;
    }

    /// <summary>The export declares a balance; one account holds it and nobody else does.</summary>
    private static AccountAssignment? ByBalance(
        BankStatement statement,
        IReadOnlyList<YnabAccount> accounts,
        IReadOnlySet<string> claimed
    )
    {
        if (statement.ClosingBalance is not { } closing)
        {
            return null;
        }

        var candidates = accounts
            .Where(account => !claimed.Contains(account.Id))
            .Where(account => (account.Balance - closing).Abs() <= BalanceTolerance)
            .ToList();

        return candidates.Count == 1
            ? new AccountAssignment(statement, candidates[0], $"the only account holding {closing:0.00}")
            : null;
    }
}
