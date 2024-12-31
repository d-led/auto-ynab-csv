namespace AutoYnabCsv.Contracts;

public interface IConvertInput
{
    IEnumerable<YnabImportEntry> Convert(string input);
}