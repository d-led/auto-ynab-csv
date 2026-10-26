using AutoYnabCsv.Mcp;
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Snapshot;

namespace Tests.Ynab;

/// <summary>The server's answers, called the way a client would: with a folder and nothing else.</summary>
[TestClass]
public class YnabToolsTest
{
    [TestMethod]
    public void TheCheckNamesTheBuildThatIsAnswering()
    {
        var answer = YnabTools.Check(settingsFile: NoSettings());

        StringAssert.Contains(answer, "auto-ynab-csv-mcp", "a stale process should be visible here");
        StringAssert.Contains(answer, "built ");
    }

    [TestMethod]
    [DoNotParallelize] // the environment is the process's, not the test's
    public void TheCheckSaysTheTokenIsSetWithoutRepeatingItBack()
    {
        const string token = "token-that-must-not-be-echoed";
        var before = Environment.GetEnvironmentVariable("YNAB_TOKEN");
        Environment.SetEnvironmentVariable("YNAB_TOKEN", token);

        try
        {
            var answer = YnabTools.Check(settingsFile: NoSettings());

            StringAssert.Contains(answer, "YNAB_TOKEN is set");
            StringAssert.Contains(answer, "these tools never need one");
            Assert.IsFalse(answer.Contains(token, StringComparison.Ordinal), "the value stays in the environment");
        }
        finally
        {
            Environment.SetEnvironmentVariable("YNAB_TOKEN", before);
        }
    }

    [TestMethod]
    [DoNotParallelize] // the environment is the process's, not the test's
    public void AShellWithoutTheTokenIsSaidPlainly()
    {
        var before = Environment.GetEnvironmentVariable("YNAB_TOKEN");
        Environment.SetEnvironmentVariable("YNAB_TOKEN", null);

        try
        {
            StringAssert.Contains(YnabTools.Check(settingsFile: NoSettings()), "YNAB_TOKEN is NOT set");
        }
        finally
        {
            Environment.SetEnvironmentVariable("YNAB_TOKEN", before);
        }
    }

    [TestMethod]
    public void ASnapshotThatIsThereIsReportedWithWhatItHolds()
    {
        var folder = TempFolder();
        SnapshotStore.Save(Sample.Snapshot(Sample.T("t1", "2026-10-23", -19.71m, "PayPal")), folder);

        var answer = YnabTools.Check(folder, NoSettings());

        StringAssert.Contains(answer, "Household");
        StringAssert.Contains(answer, "account(s)");
        StringAssert.Contains(answer, "1 transaction(s)");
    }

    [TestMethod]
    public void AFolderWithoutASnapshotSaysWhichCommandWritesOne()
    {
        var answer = YnabTools.Check(TempFolder(), NoSettings());

        StringAssert.Contains(answer, "no snapshot.json");
        StringAssert.Contains(answer, "ynab snapshot", "and that it is the command that costs requests");
    }

    [TestMethod]
    public void TheSettingsAreCountedRatherThanQuoted()
    {
        var file = Path.Combine(TempFolder(), "accounts.txt");
        File.WriteAllLines(file, ["plan=00000000-0000-0000-0000-000000000000", "Giro · 1=00000000-0000-0000-0000-000000000001"]);

        var answer = YnabTools.Check(settingsFile: file);

        StringAssert.Contains(answer, "plan set");
        StringAssert.Contains(answer, "1 account(s)");
        Assert.IsFalse(answer.Contains("00000000-0000-0000-0000-000000000001"), "ids are not the point of a check");
    }

    [TestMethod]
    public void TheCheckSaysOutLoudThatItCostNothing()
    {
        StringAssert.Contains(YnabTools.Check(settingsFile: NoSettings()), "No request was made");
    }

    /// <summary>An empty settings file, so the check never reads the one a person is actually using.</summary>
    private static string NoSettings()
    {
        var file = Path.Combine(TempFolder(), "accounts.txt");
        File.WriteAllText(file, string.Empty);
        return file;
    }

    [TestMethod]
    public void TheAccountsOfASnapshotAreListedWithTheirBalances()
    {
        var folder = TempFolder();
        SnapshotStore.Save(Sample.Snapshot(Sample.T("t1", "2026-10-23", -19.71m, "PayPal")), folder);

        var answer = YnabTools.Accounts(folder);

        StringAssert.Contains(answer, "Household");
        StringAssert.Contains(answer, "Girokonto · 0001");
        StringAssert.Contains(answer, "On budget: 0.00");
    }

    [TestMethod]
    public void AFolderWithNoRecognisedExportSaysSoInsteadOfAnsweringNothing()
    {
        StringAssert.Contains(YnabTools.Exports(TempFolder()), "Nothing was read");
    }

    [TestMethod]
    public void AnExportIsAnsweredWithWhatItCoversAndWhatItDeclares()
    {
        var folder = TempFolder();
        ExportFixture.Write(folder, "19-10-2026_Umsatzliste_Girokonto_DE00000000000000000002.csv", ExportFixture.DkbGiro());

        var answer = YnabTools.Exports(folder);

        StringAssert.Contains(answer, "1 export(s)");
        StringAssert.Contains(answer, "2024-12-01 … 2024-12-31");
        StringAssert.Contains(answer, "1234.50", "the balance the export declares");
        StringAssert.Contains(answer, "| 2 |", "and how many rows it has");
    }

    [TestMethod]
    public void AnExportWithoutItsPeriodIsNotRecognisedAtAll()
    {
        var folder = TempFolder();
        var bare = string.Join(
            '\n',
            ExportFixture
                .DkbGiro()
                .Split('\n')
                .Where(line => !line.StartsWith("\"Zeitraum:\"", StringComparison.Ordinal))
        );
        ExportFixture.Write(folder, "19-10-2026_Umsatzliste_Girokonto_DE00000000000000000002.csv", bare);

        StringAssert.Contains(YnabTools.Exports(folder), "Nothing was read", "an export the reader cannot date is not half-read");
    }

    [TestMethod]
    public void TheReportIsAnsweredOfflineFromASnapshotAndTheExports()
    {
        var folder = TempFolder();
        var snapshot = TempFolder();
        SnapshotStore.Save(Sample.Snapshot(), snapshot);
        ExportFixture.Write(folder, "19-10-2026_Umsatzliste_Girokonto_DE00000000000000000002.csv", ExportFixture.DkbGiro());

        var answer = YnabTools.Reconcile(
            snapshot,
            folder,
            accounts: ["Girokonto · 0002=acc-giro"],
            expectedBalances: ["Girokonto · 0002=1.234,50"]
        );

        StringAssert.Contains(answer, "1 account(s) paired with an export");
        StringAssert.Contains(answer, "1234.50", "the balance that was given by hand");
        StringAssert.Contains(answer, "Girokonto · 0002", "the account the export was mapped to");
    }

    [TestMethod]
    public void ABalanceThatIsNotAnAmountSaysWhichOne()
    {
        var folder = TempFolder();
        var snapshot = TempFolder();
        SnapshotStore.Save(Sample.Snapshot(), snapshot);
        ExportFixture.Write(folder, "19-10-2026_Umsatzliste_Girokonto_DE00000000000000000002.csv", ExportFixture.DkbGiro());

        var failure = Assert.ThrowsExactly<ArgumentException>(() =>
            YnabTools.Reconcile(snapshot, folder, expectedBalances: ["Girokonto · 0002=not money"])
        );

        StringAssert.Contains(failure.Message, "not money");
    }

    [TestMethod]
    public void APlanFileIsAnsweredAsTables()
    {
        var path = Path.Combine(TempFolder(), "plan.json");
        PlanStore.Save(
            new RepairPlan(
                "plan-1",
                new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
                Creates:
                [
                    new PlannedCreate("acc-giro", "Girokonto · 0001", new DateOnly(2026, 10, 23), -19.71m, "PayPal", "memo", "ayc-1", null)
                ],
                Deletes: [],
                Categories: [],
                Notes: [],
                Moves: []
            ),
            path
        );

        StringAssert.Contains(YnabTools.Plan(path), "## Add — in the bank, not in the plan (1)");
    }

    [TestMethod]
    public void AJournalFileIsAnsweredAsACount()
    {
        var path = Path.Combine(TempFolder(), "journal.jsonl");
        Journal.Append(path, new JournalEntry(DateTimeOffset.UnixEpoch, JournalEntry.CreatedKind, "one row", null, null, null));

        StringAssert.Contains(YnabTools.JournalSummaryOf(path), "1 created");
    }

    private static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ayc-mcp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
