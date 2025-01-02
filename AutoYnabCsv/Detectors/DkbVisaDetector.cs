using AutoYnabCsv.Contracts;

namespace AutoYnabCsv.Detectors;

public class DkbVisaDetector : IDetectInput
{
    private static readonly string[] RequiredArtifacts = [
        "\"Karte\";",
        "\"Saldo",
        "\"Belegdatum\";"
    ];

    public Detection TryDetect(string input)
    {
        var success = RequiredArtifacts.All(input.Contains);
        return success ? KnownSources.DkbVisa : KnownSources.None;
    }
}
