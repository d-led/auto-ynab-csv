using AutoYnabCsv.Statements;
using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Reconcile;

/// <summary>One row of an export, as the bank wrote it: signed amount, the parties, the purpose.</summary>
public sealed record BankRow(
    DateOnly Date,
    Money Amount,
    string Payee,
    string Memo,
    string SourceFile,
    int Index,
    string CounterpartyIban = ""
)
{
    public bool IsZero => Amount.IsZero;

    public bool IsInflow => Amount.Sign > 0;

    public string Describe() =>
        string.IsNullOrWhiteSpace(Memo) ? Payee : $"{Payee} — {Memo}";
}

/// <summary>
/// An export and the account it belongs to. The rows are the bank's own statement, so they are the
/// truth the plan is held against; the balance line is what the account has to add up to.
/// </summary>
public sealed record BankStatement(
    string SourceFile,
    string Label,
    ExportStatement Statement,
    IReadOnlyList<BankRow> Rows
)
{
    /// <summary>
    /// The bank writes plain decimals; the plan speaks <see cref="Money"/>. This is the one seam
    /// where the two meet, so the conversion lives here and nowhere else.
    /// </summary>
    public Money? ClosingBalance => Statement.ClosingBalance is { } closing ? closing : null;

    public DateOnly? ClosingDate => Statement.BalanceDate;

    public DateOnly? LastRowDate => Rows.Count == 0 ? null : Rows.Max(row => row.Date);
}
