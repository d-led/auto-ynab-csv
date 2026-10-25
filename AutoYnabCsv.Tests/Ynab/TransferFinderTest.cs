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
    private const string Visa = "DE00000000000000000005";

    private static readonly OwnAccounts Own = OwnAccounts.Of(
        [Statement("Girokonto · 0001", Giro), Statement("Girokonto · 0003", Together)]
    );

    private static readonly OwnAccounts OwnWithVisa = OwnAccounts.Of(
        [Statement("Girokonto · 0001", Giro), Statement("Visa Kreditkarte 0005", Visa)]
    );

    [TestMethod]
    public void ARowTheBankHasInAnotherAccountIsOfferedAsAMove()
    {
        var moves = TransferFinder.Reaccountings(
            [new RowOf("Girokonto · 0003", Row(0, "2026-01-14", -300m, Giro))],
            [("Girokonto · 0001", Sample.T("t1", "2026-01-14", -300m, "Some Person"))]
        );

        Assert.AreEqual(1, moves.Count);
        Assert.AreEqual("Girokonto · 0001", moves[0].FromLabel);
        Assert.AreEqual("Girokonto · 0003", moves[0].ToLabel);
    }

    [TestMethod]
    public void ARowThePlanAlreadyHasInThatAccountIsNotMoved()
    {
        var moves = TransferFinder.Reaccountings(
            [new RowOf("Girokonto · 0001", Row(0, "2026-01-14", -300m, Giro))],
            [("Girokonto · 0001", Sample.T("t1", "2026-01-14", -300m, "Some Person"))]
        );

        Assert.AreEqual(0, moves.Count, "it is already where the bank has it");
    }

    [TestMethod]
    public void TheNearestDayIsTheOneThatExplainsTheMove()
    {
        var moves = TransferFinder.Reaccountings(
            [new RowOf("Girokonto · 0003", Row(0, "2026-01-14", -300m, Giro))],
            [
                ("Girokonto · 0001", Sample.T("far", "2026-01-18", -300m, "Some Person")),
                ("Girokonto · 0001", Sample.T("near", "2026-01-14", -300m, "Some Person")),
            ]
        );

        Assert.AreEqual("near", moves.Single().Transaction.Id);
    }

    [TestMethod]
    public void OnePlanRowCannotExplainTwoBankRows()
    {
        var moves = TransferFinder.Reaccountings(
            [
                new RowOf("Girokonto · 0003", Row(0, "2026-01-14", -300m, Giro)),
                new RowOf("Girokonto · 0003", Row(1, "2026-01-15", -300m, Giro)),
            ],
            [("Girokonto · 0001", Sample.T("t1", "2026-01-14", -300m, "Some Person"))]
        );

        Assert.AreEqual(1, moves.Count);
    }

    [TestMethod]
    public void TwoHalvesOfATransferAreOneMovement()
    {
        var transfers = TransferFinder.Pair(
            [
                new RowOf("Girokonto · 0001", Row(0, "2026-01-14", -300m, Together)),
                new RowOf("Girokonto · 0003", Row(0, "2026-01-14", 300m, Giro)),
            ],
            Own
        );

        Assert.AreEqual(1, transfers.Count);
        Assert.IsTrue(transfers[0].ConfirmedByAccountNumber, "both exports name the other side");
        Assert.AreEqual("Girokonto · 0003", transfers[0].ToLabel);
    }

    [TestMethod]
    public void TheAccountNumberDecidesWhichOfTwoEqualAmountsIsThePartner()
    {
        var transfers = TransferFinder.Pair(
            [
                new RowOf("Girokonto · 0001", Row(0, "2026-01-14", -300m, Together)),
                new RowOf("Girokonto · 0001", Row(1, "2026-01-20", -300m, Together)),
                new RowOf("Girokonto · 0003", Row(0, "2026-01-14", 300m, Giro)),
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
            [new RowOf("Girokonto · 0001", Row(0, "2026-01-14", -300m, "DE99999999999999999999"))],
            Own
        );

        Assert.AreEqual(0, transfers.Count);
    }

    [TestMethod]
    public void TwoFeesBookedInTwoAccountsAreTwoFeesAndNotATransfer()
    {
        var transfers = TransferFinder.Pair(
            [
                new RowOf("Girokonto · 0001", Row(0, "2026-07-01", -0.54m, "0000000000")),
                new RowOf("Girokonto · 0003", Row(0, "2026-07-01", 0.54m, "1209820004")),
            ],
            Own
        );

        Assert.AreEqual(0, transfers.Count, "both rows name somebody, and neither names the other");
    }

    [TestMethod]
    public void APaymentToAStrangerIsNotATransferEvenWhenTheOtherSideNamesNobody()
    {
        var transfers = TransferFinder.Pair(
            [
                new RowOf("Girokonto · 0001", Row(0, "2026-01-14", -400m, "DE99999999999999999999")),
                new RowOf("Visa Kreditkarte 0005", Row(0, "2026-01-14", 400m, string.Empty)),
            ],
            Own
        );

        Assert.AreEqual(0, transfers.Count, "one side named a stranger, and that settles it");
    }

    [TestMethod]
    public void ARowThatNamesNobodyStillFindsItsOtherSide()
    {
        var transfers = TransferFinder.Pair(
            [
                new RowOf("Visa Kreditkarte 0005", Row(0, "2026-01-14", 400m, string.Empty)),
                new RowOf("Girokonto · 0001", Row(0, "2026-01-14", -400m, Spaced(Visa))),
            ],
            OwnWithVisa
        );

        Assert.AreEqual(1, transfers.Count, "the card's download prints no number at all");
        Assert.IsTrue(transfers[0].ConfirmedByAccountNumber, "and the bank's side names the card");
    }

    [TestMethod]
    public void AMaskedCardNumberIsNotOneOfYourAccounts()
    {
        Assert.IsNull(Own.LabelOf("4748 •••• •••• 0005"));
        Assert.IsNull(Own.LabelOf("1052770002"), "a short internal number is not an account anybody transfers to");
    }

    [TestMethod]
    public void AnAccountNumberWrittenWithSpacesIsStillRecognised()
    {
        Assert.AreEqual("Girokonto · 0001", Own.LabelOf(Spaced(Giro)));
    }

    [TestMethod]
    public void AnAccountNumberTheExportNeverPrintsCanBeGivenByHand()
    {
        var own = OwnAccounts.Of(
            [Statement("Girokonto · 0001", Giro)],
            new Dictionary<string, string> { [Spaced(N26)] = "n26" }
        );

        Assert.AreEqual("n26", own.LabelOf(N26), "n26's download prints no account number of its own");
    }

    [TestMethod]
    public void ARowSaysWhichOfYourAccountsItMovesMoneyTo()
    {
        var statement = new BankStatement(
            "export.csv",
            "Girokonto · 0001",
            Statement("Girokonto · 0001", Giro).Statement,
            [Row(0, "2026-01-14", -300m, Together), Row(1, "2026-01-15", -6.82m, "DE99999999999999999999")]
        );

        var targets = TransferFinder.TargetsOf(statement, Own);

        Assert.AreEqual("Girokonto · 0003", targets[0]);
        Assert.IsFalse(targets.ContainsKey(1), "a supermarket is not one of your accounts");
    }

    /// <summary>Banks print account numbers in groups of four; the same number without the groups.</summary>
    private static string Spaced(string iban) =>
        $"{iban[..4]} {iban[4..8]} {iban[8..12]} {iban[12..16]} {iban[16..]}";

    [TestMethod]
    public void TwoLegsThatNameEachOtherAreOneMovementOnlyThePlanHas()
    {
        var pairs = TransferFinder.OrphanPairs(
            [
                Plan("w1", -500m, "acc-giro", "acc-together"),
                Plan("w2", 500m, "acc-together", "acc-giro"),
            ],
            accountId => accountId
        );

        Assert.AreEqual(1, pairs.Count);
        Assert.AreEqual("w1", pairs[0].Outgoing.Id);
        Assert.AreEqual("acc-together", pairs[0].ToLabel);
    }

    [TestMethod]
    public void ALegWithoutItsPartnerAtTheOtherEndIsNotAPair()
    {
        var pairs = TransferFinder.OrphanPairs([Plan("w1", -500m, "acc-giro", "acc-together")], accountId => accountId);

        Assert.AreEqual(0, pairs.Count, "money left and arrived nowhere is a different problem");
    }

    [TestMethod]
    public void ATransferBetweenTheAccountsTheBankNamesIsNotAMismatch()
    {
        var wrong = new PlanTransferPair(
            Plan("w1", -500m, "Giro", "Together"),
            Plan("w2", 500m, "Together", "Giro"),
            "Giro",
            "Together"
        );
        var same = new Transfer(Row(0, "2026-01-14", -500m, "x"), Row(1, "2026-01-14", 500m, "x"), "Giro", "Together", 0, true);

        Assert.AreEqual(0, TransferFinder.Repoints([wrong], [same]).Count);
    }

    [TestMethod]
    public void ATransferTheBankMadeElsewhereIsProposedAsItsReplacement()
    {
        var wrong = new PlanTransferPair(
            Plan("w1", -500m, "Giro", "Together"),
            Plan("w2", 500m, "Together", "Giro"),
            "Giro",
            "Together"
        );
        var real = new Transfer(Row(0, "2026-01-14", -500m, "x"), Row(1, "2026-01-14", 500m, "x"), "Giro", "n26", 0, true);

        var repoints = TransferFinder.Repoints([wrong], [real]);

        Assert.AreEqual(1, repoints.Count);
        Assert.AreEqual("n26", repoints[0].Right.ToLabel);
    }

    private static YnabTransaction Plan(string id, decimal amount, string accountId, string? transferAccountId) =>
        new(
            Id: id,
            Date: new DateOnly(2026, 1, 14),
            Amount: amount,
            AccountId: accountId,
            AccountName: accountId,
            PayeeId: null,
            PayeeName: "Transfer",
            CategoryId: null,
            CategoryName: null,
            Memo: null,
            Cleared: true,
            Approved: true,
            TransferAccountId: transferAccountId,
            ImportId: null
        );

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
