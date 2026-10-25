using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Reconcile;

/// <summary>
/// The account numbers the exports are themselves about. A row whose other side is one of these is
/// money moving between two of your own accounts — a transfer — and not a payment to somebody whose
/// name happens to look similar. Only a statement's own identifier counts: a masked card number is
/// not an account anything can be transferred to.
/// </summary>
public sealed class OwnAccounts
{
    private readonly Dictionary<string, string> _labelOf;

    private OwnAccounts(Dictionary<string, string> labelOf) => _labelOf = labelOf;

    public static OwnAccounts Of(
        IReadOnlyList<BankStatement> statements,
        IReadOnlyDictionary<string, string>? ownedByHand = null
    )
    {
        var labelOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var statement in statements.Where(statement => Iban.LooksLike(statement.Statement.AccountIdentifier)))
        {
            labelOf[Iban.Normalize(statement.Statement.AccountIdentifier)] = statement.Label;
        }

        // An export can be about an account whose number it never prints — n26's download is one —
        // so the number can be given by hand instead of the transfer being invisible.
        foreach (var (iban, label) in ownedByHand ?? new Dictionary<string, string>())
        {
            labelOf[Iban.Normalize(iban)] = label;
        }

        return new OwnAccounts(labelOf);
    }

    public static OwnAccounts Empty => new([]);

    public string? LabelOf(string? iban) =>
        Iban.LooksLike(iban) && _labelOf.TryGetValue(Iban.Normalize(iban), out var label) ? label : null;

    public bool IsOurs(string? iban) => LabelOf(iban) is not null;
}

/// <summary>Account numbers arrive with spaces, dots and mixed case; only the characters matter.</summary>
internal static class Iban
{
    public static string Normalize(string? value) =>
        new string([.. (value ?? string.Empty).Where(char.IsLetterOrDigit)]).ToUpperInvariant();

    public static bool LooksLike(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length >= 15
            && char.IsLetter(normalized[0])
            && char.IsLetter(normalized[1])
            && char.IsDigit(normalized[2])
            && char.IsDigit(normalized[3]);
    }
}

/// <summary>One side of a movement between two of your own accounts.</summary>
public sealed record RowOf(string Label, BankRow Row);

/// <summary>
/// A movement out of one exported account and into another. Seen once it is one problem; seen twice
/// it is two, which is how a plan ends up with money leaving an account and never arriving.
/// </summary>
public sealed record Transfer(
    BankRow Outgoing,
    BankRow Incoming,
    string FromLabel,
    string ToLabel,
    int DayDistance,
    bool ConfirmedByAccountNumber
);

/// <summary>
/// The plan's own two legs of a transfer that the export does not show at all: two entries that name
/// each other, opposite amounts on the same day. Money that left one account and arrived nowhere.
/// </summary>
public sealed record PlanTransferPair(
    YnabTransaction Outgoing,
    YnabTransaction Incoming,
    string FromLabel,
    string ToLabel
);

/// <summary>
/// A transfer the plan recorded between one pair of accounts and the bank made between another.
/// Replacing the wrong pair with the right transfer is what closes two accounts at once.
/// </summary>
public sealed record TransferRepoint(PlanTransferPair Wrong, Transfer Right);

/// <summary>
/// Finds the two halves of a transfer. The amount has to be the same and the days close, because a
/// card settles days after the purchase and a weekend shifts a posting; where the exports print the
/// other side's account number, that decides which of several equal amounts is the real partner.
/// </summary>
public static class TransferFinder
{
    public const int DayWindow = 5;

    /// <summary>The account a row moves money to, by row index — only where the export says so.</summary>
    public static IReadOnlyDictionary<int, string> TargetsOf(BankStatement statement, OwnAccounts own)
    {
        var targets = new Dictionary<int, string>();

        foreach (var row in statement.Rows)
        {
            if (own.LabelOf(row.CounterpartyIban) is { } label
                && !string.Equals(label, statement.Label, StringComparison.Ordinal))
            {
                targets[row.Index] = label;
            }
        }

        return targets;
    }

    public static IReadOnlyList<Transfer> Pair(IReadOnlyList<RowOf> rows, OwnAccounts own, int dayWindow = DayWindow)
    {
        var taken = new HashSet<(string Label, int Index)>();
        var transfers = new List<Transfer>();

        foreach (var outgoing in rows.Where(candidate => candidate.Row.Amount.IsNegative))
        {
            var match = rows
                .Where(candidate => !string.Equals(candidate.Label, outgoing.Label, StringComparison.Ordinal))
                .Where(candidate => candidate.Row.Amount == -outgoing.Row.Amount)
                .Where(candidate => Math.Abs(candidate.Row.Date.DayNumber - outgoing.Row.Date.DayNumber) <= dayWindow)
                .Where(candidate => !taken.Contains((candidate.Label, candidate.Row.Index)))
                .OrderByDescending(candidate => Confirmed(outgoing, candidate, own))
                .ThenBy(candidate => Math.Abs(candidate.Row.Date.DayNumber - outgoing.Row.Date.DayNumber))
                .FirstOrDefault();

            if (match is null)
            {
                continue;
            }

            taken.Add((outgoing.Label, outgoing.Row.Index));
            taken.Add((match.Label, match.Row.Index));

            transfers.Add(
                new Transfer(
                    Outgoing: outgoing.Row,
                    Incoming: match.Row,
                    FromLabel: outgoing.Label,
                    ToLabel: match.Label,
                    DayDistance: Math.Abs(match.Row.Date.DayNumber - outgoing.Row.Date.DayNumber),
                    ConfirmedByAccountNumber: Confirmed(outgoing, match, own)
                )
            );
        }

        return transfers;
    }

    /// <summary>The export names the account the money went to, and that is where it turned up.</summary>
    private static bool Confirmed(RowOf outgoing, RowOf candidate, OwnAccounts own) =>
        own.LabelOf(outgoing.Row.CounterpartyIban) is { } label
        && string.Equals(label, candidate.Label, StringComparison.Ordinal);

    /// <summary>
    /// The plan's transfers the export does not know about, paired leg with leg. The two halves need
    /// not name each other — a transfer edited by hand can name anything — so what pairs them is that
    /// both are transfers, the amounts are equal and opposite, the days are close and the accounts
    /// are different.
    /// </summary>
    public static IReadOnlyList<PlanTransferPair> OrphanPairs(
        IReadOnlyList<YnabTransaction> onlyInPlan,
        Func<string, string> labelOfAccount,
        int dayWindow = DayWindow
    )
    {
        var transfers = onlyInPlan.Where(candidate => candidate.IsTransfer).ToList();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var pairs = new List<PlanTransferPair>();

        foreach (var outgoing in transfers.Where(candidate => candidate.Amount.IsNegative).OrderBy(candidate => candidate.Date))
        {
            var incoming = transfers
                .Where(candidate => !taken.Contains(candidate.Id))
                .Where(candidate => candidate.Id != outgoing.Id)
                .Where(candidate => candidate.Amount == -outgoing.Amount)
                .Where(candidate => !string.Equals(candidate.AccountId, outgoing.AccountId, StringComparison.Ordinal))
                .Where(candidate => Math.Abs(candidate.Date.DayNumber - outgoing.Date.DayNumber) <= dayWindow)
                .OrderBy(candidate => Math.Abs(candidate.Date.DayNumber - outgoing.Date.DayNumber))
                .FirstOrDefault();

            if (incoming is null)
            {
                continue;
            }

            taken.Add(outgoing.Id);
            taken.Add(incoming.Id);

            pairs.Add(
                new PlanTransferPair(
                    Outgoing: outgoing,
                    Incoming: incoming,
                    FromLabel: labelOfAccount(outgoing.AccountId),
                    ToLabel: labelOfAccount(incoming.AccountId)
                )
            );
        }

        return pairs;
    }

    /// <summary>
    /// A transfer only the plan has, next to one only the bank has, with the same amount on a day
    /// close by, between different accounts: the plan recorded the movement against the wrong
    /// account. One that matches account for account is not a mismatch and is left alone.
    /// </summary>
    public static IReadOnlyList<TransferRepoint> Repoints(
        IReadOnlyList<PlanTransferPair> onlyInPlan,
        IReadOnlyList<Transfer> onlyInBank,
        int dayWindow = DayWindow
    )
    {
        var taken = new HashSet<BankRow>();
        var repoints = new List<TransferRepoint>();

        foreach (var wrong in onlyInPlan)
        {
            var right = onlyInBank.FirstOrDefault(candidate =>
                !taken.Contains(candidate.Outgoing)
                && candidate.Outgoing.Amount == wrong.Outgoing.Amount
                && Math.Abs(candidate.Outgoing.Date.DayNumber - wrong.Outgoing.Date.DayNumber) <= dayWindow
                && DifferentAccounts(candidate, wrong)
            );

            if (right is null)
            {
                continue;
            }

            taken.Add(right.Outgoing);
            repoints.Add(new TransferRepoint(wrong, right));
        }

        return repoints;
    }

    private static bool DifferentAccounts(Transfer bank, PlanTransferPair plan) =>
        !string.Equals(bank.FromLabel, plan.FromLabel, StringComparison.Ordinal)
        || !string.Equals(bank.ToLabel, plan.ToLabel, StringComparison.Ordinal);
}
