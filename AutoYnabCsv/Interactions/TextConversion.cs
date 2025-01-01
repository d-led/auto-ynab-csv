using System.Text;
using AutoYnabCsv.Converters;
using AutoYnabCsv.Exporters;

namespace AutoYnabCsv.Interactions
{
    public static class TextConversion
    {
        public static string ConvertFile(string path)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var conversion = DetectAndConvert.Instance.Convert(text);
            return YnabCsvExporter.Export(conversion);
        }
    }
}