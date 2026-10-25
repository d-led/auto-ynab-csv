using System.Text.Json;
using System.Text.Json.Serialization;
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Rules;

/// <summary>
/// What a rule looks for. Every part that is set has to hold; a part left out says nothing, so a rule
/// with nothing set matches everything and one that names only a payee does not care about amounts.
/// </summary>
public sealed record RuleMatch(
    string? PayeeContains = null,
    string? MemoContains = null,
    long? AmountMilliunits = null,
    DateOnly? From = null,
    DateOnly? To = null,
    bool UncategorizedOnly = false
)
{
    public bool Matches(YnabTransaction transaction) =>
        Says(PayeeContains, transaction.PayeeName)
        && Says(MemoContains, transaction.Memo)
        && (AmountMilliunits is null || transaction.Amount.ToMilliunits().Value == AmountMilliunits)
        && (From is null || transaction.Date >= From)
        && (To is null || transaction.Date <= To)
        && (!UncategorizedOnly || transaction.CategoryId is null);

    /// <summary>A part that is set has to be found in the text, ignoring case; a part left out is silent.</summary>
    private static bool Says(string? contains, string? text) =>
        string.IsNullOrWhiteSpace(contains)
        || (text ?? string.Empty).Contains(contains.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>A category a rule gives to every transaction it matches.</summary>
public sealed record CategorizationRule(RuleMatch Match, string CategoryId, string CategoryName);

/// <summary>One transaction the rules would touch, and the rule that would touch it.</summary>
public sealed record RuleProposal(YnabTransaction Transaction, CategorizationRule Rule)
{
    public bool Changes => Transaction.CategoryId != Rule.CategoryId;

    public string Why => $"rule: {Rule.CategoryName}";
}

/// <summary>
/// Categorization as a file rather than as an argument: a list of rules read in order, the first match
/// winning, applied to a snapshot offline. The result is a plan — the same plan an apply already knows
/// how to write and undo — so a rule never changes anything by itself.
/// </summary>
public sealed record Rulebook(IReadOnlyList<CategorizationRule> Rules)
{
    public static readonly Rulebook Empty = new([]);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>What the rules would do to these transactions: one proposal each, first rule wins.</summary>
    public IReadOnlyList<RuleProposal> Propose(IEnumerable<YnabTransaction> transactions) =>
        [
            .. transactions
                .Where(transaction => !transaction.IsTransfer)
                .Select(transaction => (Transaction: transaction, Rule: Rules.FirstOrDefault(rule => rule.Match.Matches(transaction))))
                .Where(candidate => candidate.Rule is not null)
                .Select(candidate => new RuleProposal(candidate.Transaction, candidate.Rule!))
                .Where(proposal => proposal.Changes)
        ];

    /// <summary>The proposals as the part of a repair plan that changes categories, and nothing else.</summary>
    public RepairPlan Plan(IReadOnlyList<RuleProposal> proposals, string planId, DateTimeOffset at) =>
        new(
            PlanId: planId,
            CreatedAt: at,
            Creates: [],
            Deletes: [],
            Categories:
            [
                .. proposals.Select(proposal => new PlannedCategory(
                    proposal.Transaction.Id,
                    proposal.Rule.CategoryId,
                    proposal.Rule.CategoryName,
                    $"{proposal.Rule.CategoryName}: {Describe(proposal.Rule.Match)}"
                ))
            ],
            Notes: [$"{proposals.Count} transaction(s) categorized by {Rules.Count} rule(s); nothing else is touched"],
            Moves: []
        );

    public static Rulebook Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new RulebookUnreadableException(path, "no rules file here");
        }

        try
        {
            return JsonSerializer.Deserialize<Rulebook>(File.ReadAllText(path), Json)
                ?? throw new RulebookUnreadableException(path, "the file parsed to nothing");
        }
        catch (JsonException exception)
        {
            throw new RulebookUnreadableException(path, exception.Message);
        }
    }

    /// <summary>The rule as a person would say it, so a proposal can explain itself.</summary>
    private static string Describe(RuleMatch match) =>
        string.Join(
            ", ",
            new[]
            {
                match.PayeeContains is null ? null : $"payee contains '{match.PayeeContains}'",
                match.MemoContains is null ? null : $"memo contains '{match.MemoContains}'",
                match.AmountMilliunits is null ? null : $"amount is {match.AmountMilliunits}",
                match.From is null ? null : $"on or after {match.From:yyyy-MM-dd}",
                match.To is null ? null : $"on or before {match.To:yyyy-MM-dd}",
                match.UncategorizedOnly ? "uncategorized" : null,
            }.OfType<string>()
        ) is var described && described.Length > 0
            ? described
            : "everything";
}

[Serializable]
public sealed class RulebookUnreadableException(string path, string reason)
    : Exception($"Cannot read the rules {path}: {reason}.");
