using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

[TestClass]
public class EmlxWalkerTest
{
    [TestMethod]
    public void FindsCompleteMessagesAndCountsTheOnesOnlyPartlyDownloaded()
    {
        var root = NewStore();

        try
        {
            WriteMessage(root, "1.emlx");
            WriteMessage(root, "2.partial.emlx");
            WriteMessage(root, "3.partial.emlx");

            var walk = EmlxWalker.Walk(root);

            CollectionAssert.AreEqual(
                new[] { "1.emlx" },
                walk.Complete.Select(file => Path.GetFileName(file.Path)).ToArray()
            );
            Assert.AreEqual(2, walk.Partial);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void OnlyFilesChangedAfterTheCursorAreReturned()
    {
        var root = NewStore();

        try
        {
            var path = WriteMessage(root, "1.emlx");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));

            var walk = EmlxWalker.Walk(root, DateTimeOffset.UtcNow.AddDays(-1));

            Assert.AreEqual(0, walk.Complete.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewStore() =>
        Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            Guid.NewGuid().ToString("N")
        );

    private static string WriteMessage(string root, string name)
    {
        var directory = Path.Combine(root, "INBOX.mbox", "mailbox-id", "Data", "Messages", "1");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, TestEmlx.Build("message"));
        return path;
    }
}
