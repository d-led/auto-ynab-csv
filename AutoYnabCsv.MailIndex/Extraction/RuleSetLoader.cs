using System.Text.Json;
using AutoYnabCsv.MailIndex.Facts;

namespace AutoYnabCsv.MailIndex.Extraction;

/// <summary>
/// The shipped rules are a <c>patterns.jsonc</c> embedded in the assembly — data, not code, so a
/// user can read them, copy them and override one by id. They are read first; then the same file
/// name in the app directory, the user config directory and the nearest project folder is laid
/// over them, nearest wins.
/// </summary>
public static class RuleSetLoader
{
    public const string FileName = "patterns.jsonc";

    /// <summary>The embedded resource, named so the compiler keeps it findable.</summary>
    public const string ResourceName = "AutoYnabCsv.MailIndex.Extraction.patterns.jsonc";

    public static ExtractionRuleSet Load(string? explicitPath = null) =>
        Merge(FilesToRead(explicitPath));

    /// <summary>
    /// The files that are actually read on top of the shipped rules — empty when only the assembly
    /// is used. The report prints them, so an override is never a mystery.
    /// </summary>
    public static IReadOnlyList<string> Sources(string? explicitPath = null) =>
        FilesToRead(explicitPath).ToArray();

    /// <summary>Only what the tool ships, with nothing of the user's merged in.</summary>
    public static ExtractionRuleSet Shipped()
    {
        var file = Parse(ShippedText(), ResourceName);

        return new ExtractionRuleSet(
            file.Rules,
            file.Vendors,
            file.OpaquePayees,
            file.TransferPhrases
        );
    }

    /// <summary>
    /// The shipped vendor names on their own, for the calls that have no rule set in hand.
    /// </summary>
    public static VendorNames ShippedVendors { get; } = new Lazy<VendorNames>(() => Shipped().Vendors).Value;

    /// <summary>The shipped rules as a file, so <c>mail patterns --write</c> can hand them over.</summary>
    public static string ShippedText()
    {
        using var stream =
            typeof(RuleSetLoader).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is missing from the assembly.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static ExtractionRuleSet Merge(IEnumerable<string> files)
    {
        var shipped = Parse(ShippedText(), ResourceName);
        var rules = shipped.Rules.ToDictionary(rule => rule.Id, StringComparer.OrdinalIgnoreCase);
        var vendors = shipped.Vendors;
        var opaque = shipped.OpaquePayees.ToList();
        var transfers = shipped.TransferPhrases.ToList();

        foreach (var file in files)
        {
            var layered = Parse(File.ReadAllText(file), file);
            foreach (var rule in layered.Rules)
            {
                rules[rule.Id] = rule;
            }

            vendors = vendors.Over(layered.Vendors.Names);
            opaque.AddRange(
                layered.OpaquePayees.Where(word =>
                    !opaque.Contains(word, StringComparer.OrdinalIgnoreCase)
                )
            );
            transfers.AddRange(
                layered.TransferPhrases.Where(word =>
                    !transfers.Contains(word, StringComparer.OrdinalIgnoreCase)
                )
            );
        }

        return new ExtractionRuleSet(rules.Values, vendors, opaque, transfers);
    }

    private static IEnumerable<string> FilesToRead(string? explicitPath)
    {
        if (!string.IsNullOrEmpty(explicitPath))
        {
            yield return explicitPath;
            yield break;
        }

        foreach (var directory in AppDirectories.PatternDirectories())
        {
            var path = Path.Combine(directory, FileName);
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static ParsedFile Parse(string text, string source)
    {
        var file = JsonSerializer.Deserialize<RuleFile>(
            text,
            new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
            }
        );

        return new ParsedFile(
            (file?.Rules ?? []).Select(dto => dto.ToRule(source)).ToArray(),
            new VendorNames(file?.Vendors),
            file?.OpaquePayees ?? [],
            file?.Transfers ?? []
        );
    }

    private sealed record ParsedFile(
        IReadOnlyList<ExtractionRule> Rules,
        VendorNames Vendors,
        IReadOnlyList<string> OpaquePayees,
        IReadOnlyList<string> TransferPhrases
    );

    private sealed class RuleFile
    {
        public int Version { get; set; } = 1;

        public List<RuleDto> Rules { get; set; } = [];

        public Dictionary<string, string> Vendors { get; set; } = [];

        public List<string> OpaquePayees { get; set; } = [];

        public List<string> Transfers { get; set; } = [];
    }

    private sealed class RuleDto
    {
        public string Id { get; set; } = string.Empty;

        public string? Sender { get; set; }

        public string? Subject { get; set; }

        public string? Body { get; set; }

        public string? Vendor { get; set; }

        public bool Stop { get; set; }

        // A list of goods is read from the body, a line at a time, unless the rule says the shop
        // names what was bought in its subject.
        public string? ItemsFrom { get; set; }

        public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public ExtractionRule ToRule(string source)
        {
            if (string.IsNullOrWhiteSpace(Id))
            {
                throw new ArgumentException($"{source} has a rule without an id.");
            }

            var goods = Where(ItemsFrom, $"{source}: rule '{Id}'");

            return new ExtractionRule(
                Id,
                Sender,
                Subject,
                Body,
                Vendor,
                Fields
                    .Select(field =>
                        IsItems(field.Key)
                            ? new FieldExtraction(field.Key, field.Value, goods)
                            : new FieldExtraction(field.Key, field.Value)
                    )
                    .ToArray(),
                Stop
            );
        }

        private static bool IsItems(string name) =>
            string.Equals(name, FieldExtraction.ItemsName, StringComparison.OrdinalIgnoreCase);

        /// <summary>The body unless the rule asks for the subject; nothing else is a message part.</summary>
        private static FieldSource Where(string? itemsFrom, string source) =>
            itemsFrom?.ToLowerInvariant() switch
            {
                null or "body" => FieldSource.Body,
                "subject" => FieldSource.Subject,
                _ => throw new ArgumentException(
                    $"{source} says items come from '{itemsFrom}', which is no part of a "
                        + "message; 'body' and 'subject' are."
                ),
            };
    }
}
