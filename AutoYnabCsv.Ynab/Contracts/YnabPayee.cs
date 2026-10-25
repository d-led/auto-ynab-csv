namespace AutoYnabCsv.Ynab.Contracts;

/// <summary>A payee, or a transfer payee — the latter named after the account on the far side.</summary>
public sealed record YnabPayee(string Id, string Name, string? TransferAccountId)
{
    public bool IsTransfer => TransferAccountId is not null;
}
