using AutoYnabCsv.Statements;
using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Reconcile;

namespace Tests.Ynab;

[TestClass]
public class TransferFinderTest
{
    private const string Giro = "DE00000000000000000001";
    private const string Together = "DE00000000000000000004";
    private const string N26 = "DE00000000000000000026";

    private static readonly OwnAccounts Own = OwnAccounts.Of(
        [Statement("Girokonto · 9004", Giro), Statement("Girokonto · 3784", Together)]
    );

    [TestMethod]
    public void TwoHalvesOfATransferAreOneMovement()
    {
        var transfers = TransferFinder.Pair(
            [
                new RowOf("Girokonto · 9004", Row(0, "2026-01-14", -300m, Together)),
                new RowOf("Girokonto · 3784", Row(0, "2026-01-14", 300m, Giro)),
            ],
            Own
        );

        Assert.AreEqual(1, transfers.Count);
        Assert.IsTrue(transfers[0].ConfirmedByAccountNumber, "both exports name the other side");
        Assert.AreEqual("Girokonto · 3784", transfers[0].ToLabel);
    }

    [TestMethod]
    public void TheAccountNumberDecidesWhichOfTwoEqualAmountsIsThePartner()
    {
        var transfers = TransferFinder.Pair(
            [
                new RowOf("Girokonto · 9004", Row(0, "2026-01-14", -300m, Together)),
                new RowOf("Girokonto · 9004", Row(1, "2026-01-20", -300m, Together)),
                new RowOf("Girokonto · 3784", Row(0, "2026-01-14", 300m, Giro)),
            ],
            Own
        );

        Assert.AreEqual(1, transfers.Count);
        Assert.AreEqual(new DateOnly(2026, 1, 14), transfers[0].Outgoing.Date, "the day the export names, not the next one");
    }

    [TestMethod]
    public void APaymentToSomebodyElseHasNoSecondHalf()
    {
        var transfers = TransferFinder.Pair(
            [new RowOf("Girokonto · 9004", Row(0, "2026-01-14", -300m, "DE99999999999999999999"))],
            Own
        );

        Assert.AreEqual(0, transfers.Count);
    }

    [TestMethod]
    public void AMaskedCardNumberIsNotOneOfYourAccounts()
    {
        Assert.IsNull(Own.LabelOf("4748 •••• •••• 9158"));
        Assert.IsNull(Own.LabelOf("1052774781"), "a short internal number is not an account anybody transfers to");
    }

    [TestMethod]
    public void AnAccountNumberWrittenWithSpacesIsStillRecognised()
    {
        Assert.AreEqual("Girokonto · 9004", Own.LabelOf(Spaced(Giro)));
    }

    [TestMethod]
    public void AnAccountNumberTheExportNeverPrintsCanBeGivenByHand()
    {
        var own = OwnAccounts.Of(
            [Statement("Girokonto · 9004", Giro)],
            new Dictionary<string, string> { [Spaced(N26)] = "n26" }
        );

        Assert.AreEqual("n26", own.LabelOf(N26), "n26's download prints no account number of its own");
    }

    [TestMethod]
    public void ARowSaysWhichOfYourAccountsItMovesMoneyTo()
    {
        var statement = new BankStatement(
            "export.csv",
            "Girokonto · 9004",
            Statement("Girokonto · 9004", Giro).Statement,
            [Row(0, "2026-01-14", -300m, Together), Row(1, "2026-01-15", -6.82m, "DE99999999999999999999")]
        );

        var targets = TransferFinder.TargetsOf(statement, Own);

        Assert.AreEqual("Girokonto · 3784", targets[0]);
        Assert.IsFalse(targets.ContainsKey(1), "a supermarket is not one of your accounts");
    }

    /// <summary>Banks print account numbers in groups of four; the same number without the groups.</summary>
    private static string Spaced(string iban) =>
        $"{iban[..4]} {iban[4..8]} {iban[8..12]} {iban[12..16]} {iban[16..]}";

    private static BankStatement Statement(string label, string iban) =>
        new(
            "export.csv",
            label,
            new ExportStatement(label, iban, null, null, null, null),
            []
        );

    private static BankRow Row(int index, string day, decimal amount, string counterpartyIban) =>
        new(DateOnly.Parse(day), amount, "Payee", "memo", "export.csv", index, counterpartyIban);
}
