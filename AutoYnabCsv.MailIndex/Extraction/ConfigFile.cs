using System.Text.Json;

namespace AutoYnabCsv.MailIndex.Extraction;

/// <summary>What a save did: the rules now in force, or why nothing on disk changed.</summary>
public sealed record ConfigSave(bool Saved, string Message);

/// <summary>
/// The patterns file a person edits. A save is a transaction: the text goes beside the real file,
/// is read back through the same loader the reports use, and only text that loads replaces it.
/// Text that does not load is reported and thrown away, so a broken edit can never be the file the
/// next report reads.
/// </summary>
public static class ConfigFile
{
    private const int FingerprintCharacters = 12;

    public static string DefaultPath =>
        Path.Combine(AppDirectories.ConfigDirectory(), RuleSetLoader.FileName);

    /// <summary>The file to edit — the user's own, or the shipped rules as text to start from.</summary>
    public static string Read(string? path = null) =>
        File.Exists(path ?? DefaultPath)
            ? File.ReadAllText(path ?? DefaultPath)
            : RuleSetLoader.ShippedText();

    public static ConfigSave Save(string text, string? path = null)
    {
        var target = path ?? DefaultPath;
        var candidate = target + ".saving";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(candidate, text);
            RuleSetLoader.Load(candidate);
            File.Move(candidate, target, overwrite: true);
        }
        catch (Exception exception) when (IsAboutTheTextOrTheFile(exception))
        {
            File.Delete(candidate);
            return new ConfigSave(false, $"Not saved: {exception.Message}");
        }

        var rules = RuleSetLoader.Load();
        return new ConfigSave(
            true,
            $"Saved. {rules.RuleIds.Count} rules, fingerprint {Short(rules.Fingerprint)}."
        );
    }

    private static string Short(string fingerprint) =>
        fingerprint[..Math.Min(FingerprintCharacters, fingerprint.Length)];

    /// <summary>
    /// The failures a person can cause and fix by editing the text: it is not JSON, a rule lacks an
    /// id, a pattern does not compile — or the file cannot be written where it belongs.
    /// </summary>
    private static bool IsAboutTheTextOrTheFile(Exception exception) =>
        exception
            is JsonException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException;
}
