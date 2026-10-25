using System.Text.Json;
using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Snapshot;

/// <summary>
/// The snapshot on disk. Reading the plan costs requests from a 200-per-hour budget, so it is read
/// once into a file and every question afterwards is asked offline; a second look costs nothing and
/// cannot see the plan change underneath an argument about a balance.
/// </summary>
public static class SnapshotStore
{
    public const string FileName = "snapshot.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Save(YnabSnapshot snapshot, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, Options));
        return path;
    }

    public static YnabSnapshot Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            throw new SnapshotUnreadableException(path, "no snapshot here");
        }

        return JsonSerializer.Deserialize<YnabSnapshot>(File.ReadAllText(path), Options)
            ?? throw new SnapshotUnreadableException(path, "the file parsed to nothing");
    }
}

[Serializable]
public sealed class SnapshotUnreadableException(string path, string reason)
    : Exception($"Cannot read the snapshot {path}: {reason}. Run `ynab snapshot` first.");
