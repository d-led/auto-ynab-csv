namespace AutoYnabCsv.MailIndex.Web;

/// <summary>One row the site can search: a bank entry or a mail.</summary>
public sealed record SiteDocument(
    string Id,
    string Kind,
    string Date,
    string Title,
    string Vendor,
    string Merchant,
    string Orders,
    string Amounts,
    string Fields,
    string Text,
    string Source,
    IReadOnlyList<string> RelatedIds,
    string MailUrl
);
