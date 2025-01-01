using AutoYnabCsv.Contracts;

namespace AutoYnabCsv.Detectors;

public class N26CsvDetector : IDetectInput
{
    private static readonly string[] NecessaryColumns = [
        "Value Date",
        "Original Amount",
        "Original Currency"
    ];

    public Detection TryDetect(string input)
    {
        var headers = input
            .Split(["\r\n", "\r", "\n"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault("")
            .Split(',')
            .Select(x => x.Trim(['"']));

        var success = NecessaryColumns.All(necessaryColumn => headers.Contains(necessaryColumn));

        return success ? KnownSources.N26 : KnownSources.None;
    }
}
