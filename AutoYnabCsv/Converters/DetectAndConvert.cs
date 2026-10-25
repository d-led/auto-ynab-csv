using AutoYnabCsv.Contracts;
using AutoYnabCsv.Detectors;

namespace AutoYnabCsv.Converters;

public class DetectAndConvert(OwnAccountBook? own = null) : IConvertInput
{
    public static readonly IConvertInput Instance = new DetectAndConvert();

    private readonly OwnAccountBook _own = own ?? OwnAccountBook.None;

    public IEnumerable<YnabImportEntry> Convert(string input)
    {
        var detection = DetectFirst.Instance.TryDetect(input);
        if (!detection.Successful)
        {
            throw new FormatNotSupportedException();
        }

        var conversion = ChooseConverter(detection.SourceType);
        return conversion.Convert(input);
    }

    private IConvertInput ChooseConverter(string detectionSourceType)
    {
        IConvertInput? converter = detectionSourceType switch
        {
            KnownSources.N26Source => new N26Converter(_own),
            KnownSources.DkbGiroSource => new DkbGiroConverter(_own),
            KnownSources.DkbVisaSource => new DkbVisaConverter(),
            _ => null
        };
        if (converter == null)
        {
            throw new NoConverterFoundException();
        }
        return converter;
    }
}

[Serializable]
public class NoConverterFoundException : Exception;

[Serializable]
public class FormatNotSupportedException : Exception;
