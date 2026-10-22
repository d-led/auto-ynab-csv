using System.Text;
using AutoYnabCsv.Common;
using AutoYnabCsv.Converters;
using AutoYnabCsv.Detectors;
using AutoYnabCsv.MailIndex.Correlation;

namespace AutoYnabCsv.MailIndex.Web;

/// <summary>An export that converted, with the source it was recognised as.</summary>
public sealed record BankSource(
    string File,
    string Detected,
    IReadOnlyList<CorrelationEntry> Entries
);

public sealed record SkippedInput(string File, string Reason);

/// <summary>
/// Reads a set of bank exports exactly as the converter does — same detection, same reader,
/// same encoding — and keeps the detection result for the report's audit trail.
/// </summary>
public static class BankSourceReader
{
    public static (IReadOnlyList<BankSource> Sources, IReadOnlyList<SkippedInput> Skipped) Read(
        IEnumerable<string> inputs
    )
    {
        var sources = new List<BankSource>();
        var skipped = new List<SkippedInput>();
        var index = 0;

        foreach (var path in inputs.SelectMany(Expand).ToArray())
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var detection = DetectFirst.Instance.TryDetect(text);

            if (!detection.Successful)
            {
                skipped.Add(new SkippedInput(path, "format not supported"));
                continue;
            }

            try
            {
                var entries = DetectAndConvert
                    .Instance.Convert(text)
                    .Select(
                        entry => new CorrelationEntry(
                            index++,
                            entry.Date,
                            entry.Payee,
                            entry.Inflow > 0 ? entry.Inflow : entry.Outflow,
                            entry.Memo,
                            entry.Inflow > 0
                        )
                    )
                    .ToArray();

                sources.Add(new BankSource(path, detection.SourceType, entries));
            }
            catch (NoConverterFoundException)
            {
                skipped.Add(new SkippedInput(path, "no converter found"));
            }
        }

        return (sources, skipped);
    }

    public static IEnumerable<string> Expand(string input) =>
        Directory.Exists(input) ? ConvertedFiles.ExportsIn(input) : [input];
}
