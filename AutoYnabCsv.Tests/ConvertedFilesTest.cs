using AutoYnabCsv.Common;

namespace Tests;

[TestClass]
public class ConvertedFilesTest
{
    [TestMethod]
    [DataRow("/exports/a.ynab-20261019182302.csv")]
    [DataRow("/exports/a.ynab.csv")]
    public void WhatWeWroteIsNotAnExport(string path)
    {
        Assert.IsTrue(ConvertedFiles.IsConverted(path));
    }

    [TestMethod]
    [DataRow("/exports/a.csv")]
    [DataRow("/exports/n26-00000000.csv")]
    [DataRow("/exports/Umsatzliste_Visa Kreditkarte_0000.csv")]
    public void ARealExportIsAnExport(string path)
    {
        Assert.IsFalse(ConvertedFiles.IsConverted(path));
    }

    [TestMethod]
    public void AFolderYieldsOnlyTheExports()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(folder);

        try
        {
            File.WriteAllText(Path.Combine(folder, "bank.csv"), "a");
            File.WriteAllText(Path.Combine(folder, "bank.ynab-20261019182302.csv"), "b");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "c");

            CollectionAssert.AreEqual(
                new[] { Path.Combine(folder, "bank.csv") },
                ConvertedFiles.ExportsIn(folder).ToArray()
            );
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
