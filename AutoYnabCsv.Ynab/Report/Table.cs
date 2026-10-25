namespace AutoYnabCsv.Ynab.Report;

/// <summary>One cell of a markdown table: no pipes, no line endings, and short enough to read.</summary>
public static class Table
{
    public static string Cell(string value, int width)
    {
        var flat = value.Replace("|", "/").ReplaceLineEndings(" ").Trim();

        return flat.Length <= width ? flat : flat[..(width - 1)] + "…";
    }

    public static string Amount(decimal amount) => amount.ToString("0.00");
}
