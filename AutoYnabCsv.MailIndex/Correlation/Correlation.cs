using AutoYnabCsv.MailIndex.Facts;

namespace AutoYnabCsv.MailIndex.Correlation;

/// <summary>A bank entry reduced to what matching needs, with its row number for reporting.</summary>
public sealed record CorrelationEntry(
    int Index,
    DateOnly Date,
    string Payee,
    decimal Amount,
    string Memo = "",
    bool IsInflow = false
)
{
    /// <summary>True when the row itself says this word, in its payee or in its memo.</summary>
    public bool Says(string word) =>
        Payee.Contains(word, StringComparison.OrdinalIgnoreCase)
        || Memo.Contains(word, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The two ways a mail can be the evidence for a row. Neither is a guess: a reference is quoted by
/// both, and an amount is only evidence when the counterparty the row names is named by the mail.
/// </summary>
public enum EvidenceKind
{
    /// <summary>The row and the mail quote the same number — exact, and nothing else has to agree.</summary>
    Reference,

    /// <summary>The same amount, days apart, and the mail names the counterparty the row names.</summary>
    AmountAndCounterparty,
}

/// <summary>Why a mail explains a row, in the words the report prints.</summary>
public sealed record Evidence(EvidenceKind Kind, string What)
{
    /// <summary>True when the two quote the same number, which no coincidence produces.</summary>
    public bool IsExact => Kind == EvidenceKind.Reference;

    public string Describe() =>
        Kind == EvidenceKind.Reference ? $"reference {What}" : $"amount {What}";
}

/// <summary>One bank entry and one mail that carries the evidence for it.</summary>
public sealed record Match(
    CorrelationEntry Entry,
    MailFacts Mail,
    int DayDistance,
    Evidence Evidence
)
{
    public bool IsExact => Evidence.IsExact;
}

/// <summary>The order the evidence is judged in, so two runs and two callers agree.</summary>
public static class MatchOrder
{
    /// <summary>An exact reference first, then the closest day, then the message path.</summary>
    public static IOrderedEnumerable<Match> ByEvidence(this IEnumerable<Match> matches) =>
        matches
            .OrderByDescending(match => match.IsExact)
            .ThenBy(match => match.DayDistance)
            .ThenBy(match => match.Mail.Mail.Path, StringComparer.Ordinal);
}
