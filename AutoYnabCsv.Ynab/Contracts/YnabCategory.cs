namespace AutoYnabCsv.Ynab.Contracts;

/// <summary>
/// A budget category. YNAB's own machinery shows up here too — "Inflow: Ready to Assign" and
/// "Uncategorized" — and is marked internal, so it is never offered as a place to file spending.
/// </summary>
public sealed record YnabCategory(string Id, string Name, string GroupName, bool Internal)
{
    public const string ReadyToAssignName = "Inflow: Ready to Assign";

    public const string UncategorizedName = "Uncategorized";

    public bool IsReadyToAssign =>
        string.Equals(Name, ReadyToAssignName, StringComparison.OrdinalIgnoreCase);

    public bool IsUncategorized =>
        string.Equals(Name, UncategorizedName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(GroupName, "Internal Master Category", StringComparison.OrdinalIgnoreCase);
}
