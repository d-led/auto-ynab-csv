using System.Text;

namespace AutoYnabCsv.Ynab.Settings;

/// <summary>
/// The account-specific settings, kept in one file outside the repository so a command is two folders
/// and not a page of identifiers:
///
/// <code>
/// # a comment; blank lines are ignored too
/// plan=00000000-0000-0000-0000-000000000000
/// Girokonto · 4781=00000000-0000-0000-0000-000000000001
/// account=Girokonto · 3784=00000000-0000-0000-0000-000000000002
/// own-iban=n26=DE00 0000 0000 0000 0000 00
/// expect-balance=n26=-38,59
/// </code>
///
/// A line without a known prefix is an account mapping, so the plain <c>label=id</c> a mapping needs
/// is enough and the prefixed forms are only there to say what a line is. <c>scripts/check.sh</c>
/// reads the same file the same way, so there is one place to type it and one place to change it.
/// </summary>
public sealed record YnabConfig(
    string? Plan,
    IReadOnlyDictionary<string, string> Accounts,
    IReadOnlyDictionary<string, string> OwnIbans,
    IReadOnlyDictionary<string, string> ExpectedBalances
)
{
    public static readonly YnabConfig Empty = new(null, new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>());

    /// <summary>Where the settings live unless the caller says otherwise.</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } configured
                ? configured
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
            "auto-ynab-csv",
            "accounts.txt"
        );

    /// <summary>Reads the settings; a missing file is not an error, it just means nothing is set.</summary>
    public static YnabConfig Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        if (!File.Exists(file))
        {
            return Empty;
        }

        var accounts = new Dictionary<string, string>(StringComparer.Ordinal);
        var ownIbans = new Dictionary<string, string>(StringComparer.Ordinal);
        var balances = new Dictionary<string, string>(StringComparer.Ordinal);
        string? plan = null;

        foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                throw new YnabConfigUnreadableException(file, $"'{trimmed}' is not a label=value line");
            }

            var name = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();

            switch (name)
            {
                case "plan":
                    plan = value;
                    break;
                case "account":
                    PutPair(accounts, value, file);
                    break;
                case "own-iban":
                    PutPair(ownIbans, value, file);
                    break;
                case "expect-balance":
                    PutPair(balances, value, file);
                    break;
                default:
                    accounts[name] = value;
                    break;
            }
        }

        return new YnabConfig(plan, accounts, ownIbans, balances);
    }

    /// <summary>A prefixed setting carries its own <c>label=value</c>, split at the first <c>=</c>.</summary>
    private static void PutPair(Dictionary<string, string> into, string pair, string file)
    {
        var separator = pair.IndexOf('=');
        if (separator <= 0)
        {
            throw new YnabConfigUnreadableException(file, $"'{pair}' is a pair of label=value");
        }

        into[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
    }
}

[Serializable]
public sealed class YnabConfigUnreadableException(string path, string reason)
    : Exception($"Cannot read the settings {path}: {reason}.");
