using System.Globalization;
using AutoYnabCsv.Contracts;
using CsvHelper;

namespace AutoYnabCsv.Exporters;

public static class YnabCsvExporter
{
    public static string Export(IEnumerable<YnabImportEntry> entries)
    {
        using var writer = new StringWriter();
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(entries);
        return writer.ToString();
    }
}
