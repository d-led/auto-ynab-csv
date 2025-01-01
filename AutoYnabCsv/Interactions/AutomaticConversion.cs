using System.Text;
using AutoYnabCsv.Common;
using AutoYnabCsv.Contracts;
using AutoYnabCsv.Detectors;

namespace AutoYnabCsv.Interactions;

public record AutomaticConversionResult(
    bool Success,
    string Comment,
    string OriginalFilename,
    string ConvertedFilename,
    string DetectedSourceType
);

public static class AutomaticConversion
{
    public static AutomaticConversionResult Convert(string originalFilename)
    {
        if (!File.Exists(originalFilename))
        {
            return new AutomaticConversionResult(
                false,
                $"{originalFilename} not found",
                originalFilename,
                "",
                KnownSources.NoSource
            );
        }

        var detection = DetectFirst.Instance.TryDetect(File.ReadAllText(originalFilename, Encoding.UTF8));
        if (detection.SourceType == KnownSources.NoSource)
        {
            return new AutomaticConversionResult(
                false,
                $"{originalFilename} isn't in a known format",
                originalFilename,
                "",
                KnownSources.NoSource
            );
        }
        
        var newFilename = StringHelpers.SecondsResolutionUniqueAbsoluteFilename(originalFilename, "ynab");
        var newFilenameForComment = EnvironmentHelpers.ReplaceWithVariableNameIfStartsWithValue(newFilename, "HOME");
        var originalFilenameForComment = EnvironmentHelpers.ReplaceWithVariableNameIfStartsWithValue(
            Path.GetFullPath(originalFilename), "HOME");
        try
        {
            var newText = TextConversion.ConvertFile(originalFilename);
            File.WriteAllText(newFilename, newText, Encoding.UTF8);
            return new AutomaticConversionResult(
                true,
                $@"Converted
  {originalFilenameForComment}
 into
  {newFilenameForComment}",
                originalFilename,
                newFilename,
                detection.SourceType
            );
        }
        catch (Exception ex)
        {
            return new AutomaticConversionResult(
                false,
                $"{originalFilename} not converted due to unexpected exception: {ex.Message}",
                originalFilename,
                "",
                detection.SourceType
            );
        }
    }
}
