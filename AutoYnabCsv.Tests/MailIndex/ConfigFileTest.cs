using AutoYnabCsv.MailIndex.Extraction;

namespace Tests;

/// <summary>
/// A config save either replaces the file with text that loads, or leaves the file exactly as it
/// was. Both halves are the behaviour: a half-saved rules file would change every later report.
/// </summary>
[TestClass]
public class ConfigFileTest
{
    [TestMethod]
    public void TextThatLoadsReplacesTheFileAndSaysWhatIsInForce()
    {
        var path = WriteFile("""{ "version": 1, "rules": [] }""");

        try
        {
            var save = ConfigFile.Save(
                """
                {
                  "version": 1,
                  "rules": [
                    { "id": "custom.invoice", "vendor": "Utility", "fields": { "invoiceNumber": "(\\d+)" } }
                  ]
                }
                """,
                path
            );

            Assert.IsTrue(save.Saved, save.Message);
            StringAssert.Contains(save.Message, "fingerprint");
            CollectionAssert.Contains(
                RuleSetLoader.Load(path).RuleIds.ToList(),
                "custom.invoice"
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void APatternThatDoesNotCompileIsRefusedAndTheFileKeepsItsOldText()
    {
        var before = """{ "version": 1, "rules": [] }""";
        var path = WriteFile(before);

        try
        {
            var save = ConfigFile.Save(
                """
                {
                  "version": 1,
                  "rules": [
                    { "id": "custom.broken", "fields": { "orderNumber": "(unclosed" } }
                  ]
                }
                """,
                path
            );

            Assert.IsFalse(save.Saved);
            StringAssert.Contains(save.Message, "custom.broken");
            Assert.AreEqual(before, File.ReadAllText(path), "the old file is untouched");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void TextThatIsNotJsonIsRefusedToo()
    {
        var before = """{ "version": 1, "rules": [] }""";
        var path = WriteFile(before);

        try
        {
            var save = ConfigFile.Save("{ this is not json", path);

            Assert.IsFalse(save.Saved);
            Assert.AreEqual(before, File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ARefusedSaveLeavesNothingBehindThatALoaderWouldRead()
    {
        var path = WriteFile("""{ "version": 1, "rules": [] }""");

        try
        {
            ConfigFile.Save("""{ "rules": [ { "id": "" } ] }""", path);

            Assert.IsFalse(File.Exists(path + ".saving"), "the half-written file is cleaned up");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AnAbsentConfigIsTheShippedRulesToStartEditingFrom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.jsonc");

        StringAssert.Contains(ConfigFile.Read(path), "amazon.order");
    }

    private static string WriteFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"patterns-{Guid.NewGuid():N}.jsonc");
        File.WriteAllText(path, content);
        return path;
    }
}
