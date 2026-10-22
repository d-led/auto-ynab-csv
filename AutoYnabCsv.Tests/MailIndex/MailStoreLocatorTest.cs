using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

[TestClass]
public class MailStoreLocatorTest
{
    [TestMethod]
    public void TheNewestVersionedStoreComesFirst()
    {
        WithMailRoot(
            root =>
            {
                Directory.CreateDirectory(Path.Combine(root, "V9"));
                Directory.CreateDirectory(Path.Combine(root, "V11"));
                Directory.CreateDirectory(Path.Combine(root, "SomethingElse"));
            },
            root =>
            {
                var stores = MailStoreLocator.FindReadableStores(root);

                CollectionAssert.AreEqual(
                    new[] { 11, 9 },
                    stores.Select(store => store.Version).ToArray()
                );
            }
        );
    }

    [TestMethod]
    public void AStoreThatIsNotThereYieldsNoStores()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"no-mail-{Guid.NewGuid():N}");

        Assert.AreEqual(0, MailStoreLocator.FindStores(missing).Count);
        Assert.AreEqual(0, MailStoreLocator.FindReadableStores(missing).Count);
    }

    private static void WithMailRoot(Action<string> prepare, Action<string> use)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "auto-ynab-csv-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);

        try
        {
            prepare(root);
            use(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
