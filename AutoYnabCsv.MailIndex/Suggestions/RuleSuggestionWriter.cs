using System.Text;
using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Facts;

namespace AutoYnabCsv.MailIndex.Suggestions;

/// <summary>
/// Writes the correlations as draft rule packs. They are proposals for a human to review;
/// the rule loader reads packs of exactly this shape.
/// </summary>
public static class RuleSuggestionWriter
{
    public static string Write(IEnumerable<Match> correlations)
    {
        var found = correlations.ToArray();
        var text = new StringBuilder();

        text.AppendLine("# Draft rules proposed by `auto-ynab-csv mail correlate`.");
        text.AppendLine("# Review, trim, then move next to your rules to use them.");
        text.AppendLine("schemaVersion = 1");
        text.AppendLine();

        foreach (var rule in ExtractRules(found))
        {
            text.AppendLine("[[extract]]");
            text.AppendLine($"id = {Quote(rule.Id)}");
            text.AppendLine($"sender = {Quote(rule.Domain)}");
            text.AppendLine($"vendor = {Quote(rule.Vendor)}");
            text.AppendLine();
        }

        foreach (var alias in AliasRules(found))
        {
            text.AppendLine("[[alias]]");
            text.AppendLine($"id = {Quote(alias.Id)}");
            text.AppendLine($"payeeContains = {Quote(alias.Payee)}");
            text.AppendLine($"vendor = {Quote(alias.Vendor)}");
            text.AppendLine();
        }

        return text.ToString();
    }

    private static IEnumerable<(string Id, string Domain, string Vendor)> ExtractRules(
        IReadOnlyList<Match> correlations
    ) =>
        correlations
            .Select(correlation => new
            {
                Domain = VendorNames.DomainOf(correlation.Mail.Mail.SenderAddress),
                correlation.Mail.Vendor,
            })
            .Where(observed => observed.Domain.Length > 0 && observed.Vendor.Length > 0)
            .Distinct()
            .OrderBy(observed => observed.Vendor, StringComparer.OrdinalIgnoreCase)
            .ThenBy(observed => observed.Domain, StringComparer.OrdinalIgnoreCase)
            .Select(observed => (
                $"observed.{Slug(observed.Domain)}",
                observed.Domain,
                observed.Vendor
            ));

    private static IEnumerable<(string Id, string Payee, string Vendor)> AliasRules(
        IReadOnlyList<Match> correlations
    ) =>
        correlations
            .Where(correlation =>
                correlation.Mail.Vendor.Length > 0
                && !correlation.Entry.Payee.Contains(
                    correlation.Mail.Vendor,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Select(correlation => new
            {
                correlation.Entry.Payee,
                correlation.Mail.Vendor,
            })
            .Distinct()
            .OrderBy(observed => observed.Payee, StringComparer.OrdinalIgnoreCase)
            .ThenBy(observed => observed.Vendor, StringComparer.OrdinalIgnoreCase)
            .Select(observed => (
                $"alias.{Slug(observed.Payee)}.{Slug(observed.Vendor)}",
                observed.Payee,
                observed.Vendor
            ));

    private static string Slug(string text)
    {
        var slug = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                slug.Append(char.ToLowerInvariant(character));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString().Trim('-');
    }

    private static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
