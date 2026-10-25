using System.Text;
using AutoYnabCsv.Common;
using AutoYnabCsv.Contracts;
using AutoYnabCsv.Detectors;
using AutoYnabCsv.Statements;

namespace AutoYnabCsv.Converters;

/// <summary>
/// The accounts that own the exports sitting next to the one being converted. A bank's download is
/// converted in the folder the others came to, so the folder already knows which account numbers are
/// yours — no setting to remember, and no chance of it disagreeing with the files.
/// </summary>
public static class NearbyAccounts
{
    public static OwnAccountBook Of(string exportPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(exportPath));
        if (folder is null || !Directory.Exists(folder))
        {
            return OwnAccountBook.None;
        }

        return new OwnAccountBook(
            ConvertedFiles
                .ExportsIn(folder)
                .Select(path => (Iban: IbanOf(path), Name: AccountLabel.Of(path)))
                .Where(account => account.Iban.Length > 0)
        );
    }

    /// <summary>The account a file is about, as its own header prints it; empty when it prints none.</summary>
    private static string IbanOf(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var detection = DetectFirst.Instance.TryDetect(text);

        return detection.Successful
            ? ExportStatementReader.Read(text, detection.SourceType).AccountIdentifier
            : string.Empty;
    }
}
