using AutoYnabCsv.MailIndex;

namespace Tests;

[TestClass]
public class AppDirectoriesTest
{
    [TestMethod]
    public void TheDataAndConfigFoldersAreNamedAfterTheApp()
    {
        Assert.AreEqual("auto-ynab-csv", FolderNameOf(AppDirectories.DataDirectory()));
        Assert.AreEqual("auto-ynab-csv", FolderNameOf(AppDirectories.ConfigDirectory()));
        Assert.IsTrue(Directory.Exists(AppDirectories.DataDirectory()));
    }

    private static string FolderNameOf(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
}
