using System.Text;
using AutoYnabCsv.Contracts;
using AutoYnabCsv.Converters;
using AutoYnabCsv.Exporters;

namespace AutoYnabCsv.Interactions
{
    public static class TextConversion
    {
        /// <summary>
        /// Converts one export. Your own accounts are read from the exports next to it, so a movement
        /// between two of them is written as the transfer it is.
        /// </summary>
        public static string ConvertFile(string path, OwnAccountBook? own = null)
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            var conversion = new DetectAndConvert(own ?? NearbyAccounts.Of(path)).Convert(text);
            return YnabCsvExporter.Export(conversion);
        }
    }
}