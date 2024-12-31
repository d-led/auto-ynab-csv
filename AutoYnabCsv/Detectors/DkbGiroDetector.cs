using AutoYnabCsv.Contracts;

namespace AutoYnabCsv.Detectors;

public class DkbGiroDetector : IDetectInput
{
    private static readonly string[] RequiredArtifacts = [
        "\"Zeitraum:\"",
        "\"Kontostand",
        "\"Buchungsdatum\";"
    ];

    public Detection TryDetect(string input)
    {
        var success = RequiredArtifacts.All(input.Contains);
        return success ? KnownSources.DkbGiro : KnownSources.None;
    }
}
