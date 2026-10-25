using AutoYnabCsv.Statements;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class AccountMappingTest
{
    private static readonly IReadOnlyDictionary<string, string> NoOverrides = new Dictionary<string, string>();

    [TestMethod]
    public void AnAccountChosenByHandIsTheOneUsed()
    {
        var mapping = Resolve(
            Statement("Girokonto · 0001", closing: 100m),
            [Account("acc-1", "Something Else", balance: 0m)],
            new Dictionary<string, string> { ["Girokonto · 0001"] = "acc-1" }
        );

        Assert.AreEqual("acc-1", mapping.Assignments.Single().Account.Id);
        StringAssert.Contains(mapping.Assignments.Single().Evidence, "by hand");
    }

    [TestMethod]
    public void AnAccountNamedAfterTheExportIsFoundWithoutBeingTold()
    {
        var mapping = Resolve(Statement("Girokonto · 0001", closing: null), [Account("acc-1", "Girokonto · 0001", 0m)], NoOverrides);

        Assert.AreEqual("acc-1", mapping.Assignments.Single().Account.Id);
    }

    [TestMethod]
    public void TwoAccountsThatShareTheNameDecideNothing()
    {
        var mapping = Resolve(
            Statement("Girokonto · 0001", closing: null),
            [Account("acc-1", "Girokonto · 0001 a", 10m), Account("acc-2", "Girokonto · 0001 b", 20m)],
            NoOverrides
        );

        Assert.AreEqual(0, mapping.Assignments.Count, "a name that fits twice is not evidence");
        Assert.AreEqual(1, mapping.Unresolved.Count);
    }

    [TestMethod]
    public void AnAccountNumberTheExportPrintsFindsTheAccountItEndsWith()
    {
        var mapping = Resolve(Statement("Umsatzliste", closing: null, iban: "DE00000000000000000001"), [Account("acc-1", "DKB 0001", 0m)], NoOverrides);

        Assert.AreEqual("acc-1", mapping.Assignments.Single().Account.Id);
        StringAssert.Contains(mapping.Assignments.Single().Evidence, "0001");
    }

    [TestMethod]
    public void AnAccountHoldingTheOnlyMatchingBalanceIsTheOne()
    {
        var mapping = Resolve(
            Statement("Umsatzliste", closing: 42.00m, iban: ""),
            [Account("acc-1", "Wallet", 0m), Account("acc-2", "Second", 42.00m)],
            NoOverrides
        );

        Assert.AreEqual("acc-2", mapping.Assignments.Single().Account.Id);
    }

    [TestMethod]
    public void AnAccountAlreadySpokenForIsNotGivenToASecondExport()
    {
        var mapping = Resolve(
            [Statement("Umsatzliste", closing: 50m, iban: ""), Statement("Umsatzliste zwei", closing: 50m, iban: "")],
            [Account("acc-1", "A", 50m)],
            NoOverrides
        );

        Assert.AreEqual(1, mapping.Assignments.Count);
        Assert.AreEqual(1, mapping.Unresolved.Count, "one account cannot be two exports");
    }

    private static AccountMapping Resolve(
        BankStatement statement,
        IReadOnlyList<YnabAccount> accounts,
        IReadOnlyDictionary<string, string> overrides
    ) => AccountMappingResolver.Resolve([statement], accounts, overrides);

    private static AccountMapping Resolve(
        IReadOnlyList<BankStatement> statements,
        IReadOnlyList<YnabAccount> accounts,
        IReadOnlyDictionary<string, string> overrides
    ) => AccountMappingResolver.Resolve(statements, accounts, overrides);

    private static BankStatement Statement(string label, decimal? closing, string iban = "DE00000000000000000000") =>
        new(
            SourceFile: "export.csv",
            Label: label,
            Statement: new ExportStatement("Girokonto", iban, null, null, closing is null ? null : new DateOnly(2026, 10, 23), closing),
            Rows: []
        );

    private static YnabAccount Account(string id, string name, decimal balance) =>
        new(id, name, "checking", OnBudget: true, Closed: false, balance, Money.Zero, Money.Zero);
}
