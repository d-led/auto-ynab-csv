using AutoYnabCsv.Contracts;

namespace AutoYnabCsv.Detectors;

public class DetectFirst(IEnumerable<IDetectInput> detectors) : IDetectInput
{
    private static readonly IDetectInput[] KnownDetectors = {
        new N26CsvDetector(),
        new DkbGiroDetector()
    };

    public static readonly IDetectInput Instance = new DetectFirst(KnownDetectors); 
    
    public Detection TryDetect(string input)
    {
        foreach (var detector in detectors)
        {
            var detection = detector.TryDetect(input);
            if (detection.Successful)
            {
                return detection;
            }
        }

        return KnownSources.None;
    }
}
