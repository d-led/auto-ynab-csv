using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace AutoYnabCsv.Common;

public static class StringHelpers
{
    public static string ReplaceIfStartsWith(string input, string prefix, string replacement)
    {
        return Regex.Replace(input, $"^{Regex.Escape(prefix)}", replacement);
    }

    public static string SecondsResolutionUniqueAbsoluteFilename(string path, string prefix)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var fullPath = Path.GetFullPath(path);
        var dirName = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var fileName = Path.GetFileName(fullPath);
        var extension = Path.GetExtension(fullPath);
        return Path.Combine(
            dirName,
            $"{fileName}.{prefix}-{timestamp}{extension}"    
        );
    }
}
