using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Contracts;

namespace Tests.Ynab;

[TestClass]
public class PlanCsvTest
{
    [TestMethod]
    public void OneFilePerAccountHoldsTheRowsToAddInTheShapeYnabReads()
    {
        var plan = Plan(
            Create("DKB Giro", "2026-01-14", -218.00m),
            Create("n26", "2026-02-19", 150.00m)
        );

        var files = PlanCsv.Write(plan, NewFolder());

        Assert.AreEqual(2, files.Count);
        var giro = File.ReadAllLines(files.Single(file => file.EndsWith("DKB-Giro.csv", StringComparison.Ordinal)));
        CollectionAssert.AreEqual(
            new[] { "Date,Payee,Memo,Inflow,Outflow", "2026-01-14,Some Person,memo,0,218.00" },
            giro
        );
    }

    [TestMethod]
    public void MoneyInGoesInTheInflowColumnAndNothingIsWrittenTwice()
    {
        var plan = Plan(Create("n26", "2026-02-19", 150.00m));

        var rows = File.ReadAllLines(PlanCsv.Write(plan, NewFolder()).Single());

        Assert.AreEqual("2026-02-19,Some Person,memo,150.00,0", rows[1]);
    }

    [TestMethod]
    public void AnAccountNameThatCannotBeAFileNameIsMadeIntoSomethingThatCan()
    {
        var plan = Plan(Create("Girokonto · 4781", "2026-01-14", -1m));

        var file = PlanCsv.Write(plan, NewFolder()).Single();

        StringAssert.EndsWith(file, "Girokonto-·-4781.csv");
    }

    [TestMethod]
    public void APromiseToAddNothingWritesNoFile()
    {
        Assert.AreEqual(0, PlanCsv.Write(Plan(), NewFolder()).Count);
    }

    private static RepairPlan Plan(params PlannedCreate[] creates) =>
        new("plan-1", DateTimeOffset.UnixEpoch, creates, [], [], [], []);

    private static PlannedCreate Create(string account, string date, decimal amount) =>
        new("acc-" + account, account, DateOnly.Parse(date), amount, "Some Person", "memo", "ayc-x", null);

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ayc-csv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
