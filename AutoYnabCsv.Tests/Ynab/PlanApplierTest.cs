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
    public async Task ARowWhoseImportIdOutlivedItIsWrittenUnderAnotherOne()
    {
        var create = Create("2026-10-23", -45.81m);
        var gateway = new FakeGateway().WithBurntImportId(create.ImportId);

        var result = await Apply(Plan(create), gateway);

        Assert.AreEqual(1, result.Created, "the id was taken by a row that no longer exists");
        Assert.AreEqual(1, gateway.Transactions.Count);
        Assert.AreEqual(create.ImportId + "-r2", gateway.Transactions[0].ImportId);
    }

    [TestMethod]
    public async Task ARowThatIsStillThereIsNotWrittenASecondTime()
    {
        var gateway = new FakeGateway();
        var plan = Plan(Create("2026-10-23", -45.81m));
        await Apply(plan, gateway);

        var second = await Apply(plan, gateway);

        Assert.AreEqual(1, second.AlreadyThere);
        Assert.AreEqual(1, gateway.Transactions.Count, "a row that exists is not duplicated under a new id");
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

    [TestMethod]
    public async Task AMovePutsTheTransactionInTheOtherAccountAndComesBackAgain()
    {
        var gateway = new FakeGateway();
        var transaction = MatcherTest.Plan("t1", "2025-11-24", -500m, "Transfer : DKB Giro", transferAccountId: "acc-together");
        gateway.Seed(transaction);

        var result = await Apply(
            Plan(move: new PlannedMove("t1", "acc", "acc-together", "DKB Giro", "GiroTogether", transaction.Date, transaction.Amount, "x")),
            gateway
        );

        Assert.AreEqual(1, result.Moved);
        Assert.IsTrue(result.Clean);
        Assert.AreEqual("acc-together", gateway.Transactions.Single().AccountId);

        await PlanApplier.UndoAsync(_journal, "plan-1", gateway);

        Assert.AreEqual("acc", gateway.Transactions.Single().AccountId, "and back where it was");
    }

    private static RepairPlan Plan(PlannedCreate? create = null, PlannedDelete? delete = null, PlannedMove? move = null) =>
        new(
            PlanId: "plan-1",
            CreatedAt: new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero),
            Creates: create is null ? [] : [create],
            Deletes: delete is null ? [] : [delete],
            Categories: [],
            Notes: [],
            Moves: move is null ? [] : [move]
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
