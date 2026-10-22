using System.Globalization;

namespace AutoYnabCsv.MailIndex.Web;

/// <summary>Where the mail index stood when a report was written.</summary>
public sealed record MailAudit(
    string IndexPath,
    string MailboxRoot,
    int? MailboxVersion,
    int Messages,
    DateTimeOffset? IndexedFrom,
    DateTimeOffset? IndexedThrough,
    DateTimeOffset? LastSyncedAt,
    string RulesFingerprint
);

/// <summary>One export that contributed to a report.</summary>
public sealed record BankAudit(string File, string Detected, int Entries);

/// <summary>
/// The record of a report run: what was read, what was skipped, where the mail index and the
/// mailbox were, and how far the index reached. Written next to the page as <c>audit.json</c>
/// and shown in the page itself.
/// </summary>
public sealed record SiteAudit(
    DateTimeOffset GeneratedAt,
    string ToolVersion,
    MailAudit Mail,
    IReadOnlyList<BankAudit> Inputs,
    IReadOnlyList<SkippedInput> Skipped
)
{
    public int Entries => Inputs.Sum(input => input.Entries);

    public string Summary =>
        $"{Inputs.Count} file(s), {Entries} entries"
        + (Skipped.Count > 0 ? $", {Skipped.Count} skipped" : string.Empty)
        + $"; {MailCoverage}";

    private string MailCoverage =>
        Mail.Messages == 0
            ? "no mail indexed yet"
            : $"{Mail.Messages} indexed mails covering "
                + $"{Day(Mail.IndexedFrom)} … {Day(Mail.IndexedThrough)}";

    private static string Day(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown";
}
