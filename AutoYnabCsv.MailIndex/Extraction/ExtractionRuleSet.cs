using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AutoYnabCsv.MailIndex.Facts;
using AutoYnabCsv.MailIndex.Mail;

namespace AutoYnabCsv.MailIndex.Extraction;

/// <summary>
/// The ordered rules a message is run through. Evaluation is deterministic: a rule applies when
/// each of its conditions matches; the first rule to fill a field owns it, unless no earlier
/// rule did.
/// </summary>
public sealed class ExtractionRuleSet
{
    private readonly IReadOnlyList<PreparedRule> _rules;

    public ExtractionRuleSet(
        IEnumerable<ExtractionRule> rules,
        VendorNames? vendors = null,
        IEnumerable<string>? opaquePayees = null,
        IEnumerable<string>? transferPhrases = null
    )
    {
        _rules = rules.Select(Prepare).ToArray();
        Vendors = vendors ?? VendorNames.Empty;
        OpaquePayees = [.. opaquePayees ?? []];
        TransferPhrases = [.. transferPhrases ?? []];
        Fingerprint = FingerprintOf(_rules, Vendors);
    }

    public static ExtractionRuleSet Empty { get; } = new([]);

    /// <summary>
    /// An item is one line of a memo, so a title that a shop wrapped over two lines arrives as
    /// one, its whitespace already collapsed.
    /// </summary>
    private static readonly Regex OneLine = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// How long one pattern may chew on one message. A rule with nested quantifiers can backtrack
    /// for hours on the wrong text, and a hang says nothing about which rule did it; a timeout at
    /// least names the message and stops the run from looking dead.
    /// </summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>The names a sender domain is displayed under, as the same file configures them.</summary>
    public VendorNames Vendors { get; }

    /// <summary>
    /// The payees that name nothing worth categorizing from. Only their rows are worth downloading
    /// a body for; every other row is answered by the index or already says what it is.
    /// </summary>
    public IReadOnlyList<string> OpaquePayees { get; }

    /// <summary>
    /// What money moved without anything being bought: a withdrawal, a deposit, a card settlement,
    /// a processor passing its own balance to the bank. No mail explains those, so none is read.
    /// </summary>
    public IReadOnlyList<string> TransferPhrases { get; }

    /// <summary>True when this payee says too little to categorize the row without its mail.</summary>
    public bool NeedsMail(string payee) =>
        OpaquePayees.Any(word => payee.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the row says the money moved rather than that something was bought.</summary>
    public bool IsTransfer(string payee, string memo) =>
        TransferPhrases.Any(word =>
            payee.Contains(word, StringComparison.OrdinalIgnoreCase)
            || memo.Contains(word, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>Changes whenever the rules change, so the index knows when to re-extract.</summary>
    public string Fingerprint { get; }

    public IReadOnlyList<string> RuleIds => _rules.Select(rule => rule.Rule.Id).ToArray();

    /// <summary>Every named pattern the rules carry, across all of them.</summary>
    public int PatternCount => _rules.Sum(rule => rule.Fields.Count);

    public IReadOnlyDictionary<string, string> Extract(MailRecord mail)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<string>();

        foreach (var rule in _rules)
        {
            if (!rule.Applies(mail))
            {
                continue;
            }

            foreach (var field in rule.Fields)
            {
                if (field.IsItems)
                {
                    if (items.Count == 0)
                    {
                        items.AddRange(field.ExtractAll(mail));
                    }

                    continue;
                }

                if (!fields.ContainsKey(field.Spec.Name) && field.TryExtract(mail, out var value))
                {
                    fields[field.Spec.Name] = value;
                }
            }

            if (rule.Rule.Stop)
            {
                break;
            }
        }

        var found = items
            .Select(item => OneLine.Replace(item, " ").Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (found.Length > 0)
        {
            fields[FieldExtraction.ItemsName] = string.Join('\n', found);
        }

        return fields;
    }

    /// <summary>The vendor the first matching rule names, if any.</summary>
    public string? VendorOf(MailRecord mail) =>
        _rules
            .Where(rule => rule.Applies(mail))
            .Select(rule => rule.Rule.Vendor)
            .FirstOrDefault(vendor => !string.IsNullOrWhiteSpace(vendor));

    private static PreparedRule Prepare(ExtractionRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Id))
        {
            throw new ArgumentException("An extraction rule needs an id.");
        }

        return new PreparedRule(
            rule,
            Compile(rule.Sender, rule.Id, "sender"),
            Compile(rule.Subject, rule.Id, "subject"),
            Compile(rule.Body, rule.Id, "body"),
            (rule.Fields ?? [])
                .Select(field => PrepareField(field, rule.Id))
                .ToArray()
        );
    }

    private static PreparedField PrepareField(FieldExtraction field, string ruleId)
    {
        if (string.IsNullOrWhiteSpace(field.Name))
        {
            throw new ArgumentException($"Rule '{ruleId}' has a field without a name.");
        }

        if (string.IsNullOrEmpty(field.Pattern))
        {
            throw new ArgumentException($"Rule '{ruleId}' field '{field.Name}' has no pattern.");
        }

        return new PreparedField(field, CompileOrThrow(field.Pattern, ruleId, field.Name));
    }

    private static Regex? Compile(string? pattern, string ruleId, string what) =>
        string.IsNullOrEmpty(pattern) ? null : CompileOrThrow(pattern, ruleId, what);

    private static Regex CompileOrThrow(string pattern, string ruleId, string what)
    {
        try
        {
            return new Regex(
                pattern,
                RegexOptions.Compiled | RegexOptions.IgnoreCase,
                MatchTimeout
            );
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                $"Rule '{ruleId}' has an invalid {what} pattern: {exception.Message}",
                exception
            );
        }
    }

    private static string FingerprintOf(IReadOnlyList<PreparedRule> rules, VendorNames vendors)
    {
        var text = string.Join(
            "\n",
            rules
                .Select(rule =>
                    string.Join(
                        "|",
                        rule.Rule.Id,
                        rule.Rule.Sender,
                        rule.Rule.Subject,
                        rule.Rule.Body,
                        rule.Rule.Vendor,
                        rule.Rule.Stop,
                        string.Join(
                            ",",
                            rule.Fields.Select(field =>
                                $"{field.Spec.Name}={field.Spec.Where}:{field.Spec.Pattern}"
                            )
                        )
                    )
                )
                .Concat(
                    vendors.Names
                        .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(pair => $"{pair.Key}={pair.Value}")
                )
                .Concat([MessageParser.ReadingVersion])
        );

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private sealed record PreparedRule(
        ExtractionRule Rule,
        Regex? Sender,
        Regex? Subject,
        Regex? Body,
        IReadOnlyList<PreparedField> Fields
    )
    {
        public bool Applies(MailRecord mail) =>
            (Sender is null || Sender.IsMatch(mail.SenderAddress))
            && (Subject is null || Subject.IsMatch(mail.Subject))
            && (Body is null || Body.IsMatch(mail.Text));
    }

    private sealed record PreparedField(FieldExtraction Spec, Regex Pattern)
    {
        public bool IsItems =>
            string.Equals(Spec.Name, FieldExtraction.ItemsName, StringComparison.OrdinalIgnoreCase);

        public bool TryExtract(MailRecord mail, out string value)
        {
            var match = Pattern.Match(TextOf(mail));
            value = match.Success ? ValueOf(match) : string.Empty;
            return value.Length > 0;
        }

        public IReadOnlyList<string> ExtractAll(MailRecord mail) =>
            Pattern
                .Matches(PartOf(mail))
                .Select(ValueOf)
                .Where(value => value.Length > 0)
                .ToArray();

        private static string ValueOf(Match match) =>
            (match.Groups.Count > 1 ? match.Groups[1].Value : match.Value).Trim();

        private string PartOf(MailRecord mail) =>
            Spec.Where switch
            {
                FieldSource.Body => mail.Text,
                FieldSource.Subject => mail.Subject,
                _ => TextOf(mail),
            };

        private static string TextOf(MailRecord mail) =>
            string.Join('\n', mail.Subject, mail.Text);
    }
}
