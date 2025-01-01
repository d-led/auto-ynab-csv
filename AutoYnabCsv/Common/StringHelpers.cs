using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace AutoYnabCsv.Common;

public static class StringHelpers
{
    public static string ReplaceIfStartsWith(string input, string prefix, string replacement)
    {
        return Regex.Replace(input, $"^{Regex.Escape(prefix)}", replacement);
    }
}
