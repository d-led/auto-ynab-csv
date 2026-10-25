using System.Text.Json;

namespace AutoYnabCsv.Ynab.Apply;

/// <summary>The plan as a file, so a repair can be read, edited, declined or applied later.</summary>
public static class PlanStore
{
    public const string FileName = "plan.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Save(RepairPlan plan, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(plan, Options));
        return path;
    }

    public static RepairPlan Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new PlanUnreadableException(path);
        }

        return JsonSerializer.Deserialize<RepairPlan>(File.ReadAllText(path), Options)
            ?? throw new PlanUnreadableException(path);
    }
}

[Serializable]
public sealed class PlanUnreadableException(string path)
    : Exception($"Cannot read the repair plan {path}. Run `ynab reconcile` first.");
