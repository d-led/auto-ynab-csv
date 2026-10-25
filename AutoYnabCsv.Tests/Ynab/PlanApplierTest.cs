using AutoYnabCsv.Ynab.Apply;

namespace Tests.Ynab;

[TestClass]
public class PlanApplierTest
{
    [TestMethod]
    public async Task ApplyingAPlanPutsItsRowsIntoThePlan()
    {
        var result = await Apply(Plan(Create("2026-10-23", -45.81m)), new FakeGateway());

        Assert.AreEqual(1, result.Created);
        Assert.IsTrue(result.Clean, "an apply that worked reports no problems");
    }

    [TestMethod]
    public async Task ApplyingTheSamePlanTwiceAddsNothingTheSecondTime()
    {
        var gateway = new FakeGateway();
        var plan = Plan(Create("2026-10-23", -45.81m));

        await Apply(plan, gateway);
        var second = await Apply(plan, gateway);

        Assert.AreEqual(0, second.Created, "the deterministic import id already did this");
        Assert.AreEqual(1, second.AlreadyThere);
        Assert.AreEqual(1, gateway.Transactions.Count);
    }

    [TestMethod]
    public async Task ARepeatIsNotReportedAsAProblem()
    {
        var gateway = new FakeGateway();
        var plan = Plan(Create("2026-10-23", -45.81m));

        await Apply(plan, gateway);

        Assert.IsTrue((await Apply(plan, gateway)).Clean);
    }

    [TestMethod]
    public async Task WhatWasDeletedCanBePutBack()
    {
        var gateway = new FakeGateway();
        var gone = MatcherTest.Plan("t1", "2026-09-01", -250m, "Balance adjustment");
        gateway.Seed(gone);
        var plan = Plan(delete: new PlannedDelete("Girokonto", gone, "looks like a correction"));

        await Apply(plan, gateway);
        Assert.AreEqual(0, gateway.Transactions.Count, "the correction is gone");

        await PlanApplier.UndoAsync(_journal, "plan-1", gateway);

        Assert.AreEqual(1, gateway.Transactions.Count, "and back again");
        Assert.AreEqual(-250m, gateway.Transactions[0].Amount);
    }

    /// <summary>One journal per test: a path that changes between writing and reading undoes nothing.</summary>
    private readonly string _journal = Path.Combine(Path.GetTempPath(), $"ynab-journal-{Guid.NewGuid():N}.jsonl");

    private Task<ApplyResult> Apply(RepairPlan plan, FakeGateway gateway) =>
        PlanApplier.ApplyAsync(plan, gateway, _journal);

    private static RepairPlan Plan(PlannedCreate? create = null, PlannedDelete? delete = null) =>
        new(
            PlanId: "plan-1",
            CreatedAt: new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
            Creates: create is null ? [] : [create],
            Deletes: delete is null ? [] : [delete],
            Categories: [],
            Notes: []
        );

    private static PlannedCreate Create(string day, decimal amount) =>
        new(
            AccountId: "acc",
            AccountName: "Girokonto",
            Date: DateOnly.Parse(day),
            Amount: amount,
            PayeeName: "Kaufland",
            Memo: "VISA Debitkartenumsatz",
            ImportId: ImportIds.Of("acc", DateOnly.Parse(day), amount, "Kaufland", "VISA Debitkartenumsatz"),
            CategoryId: null
        );
}
