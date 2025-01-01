namespace AutoYnabCsv.Common;

public static class EnvironmentHelpers
{
    public static string ReplaceWithVariableNameIfStartsWithValue(string input, string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value)
            ? input
            : StringHelpers.ReplaceIfStartsWith(input, value, $"${name}");
    }
}
