using AutoYnabCsv.Mcp;
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Snapshot;

namespace Tests.Ynab;

/// <summary>The server's answers, called the way a client would: with a folder and nothing else.</summary>
[TestClass]
public class YnabToolsTest
{
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
