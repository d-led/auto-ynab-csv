using AutoYnabCsv.MailIndex.Correlation;
using AutoYnabCsv.MailIndex.Enrichment;

namespace Tests;

[TestClass]
public class TransferDetectorTest
{
    private static readonly DateOnly Day = new(2024, 9, 30);

    [TestMethod]
    public void AnOutflowMirroredInAnotherFileIsATransfer()
    {
        var pairs = TransferDetector.Find(
            [
                ("giro.csv", Entry(0, Day, 2000m, inflow: false)),
                ("tagesgeld.csv", Entry(1, Day, 2000m, inflow: true)),
            ]
        );

        Assert.AreEqual(1, pairs.Count);
        Assert.AreEqual(0, pairs[0].DayDistance);
        Assert.AreEqual(2000m, pairs[0].Outgoing.Amount);
    }

    [TestMethod]
    public void AMirrorInsideOneFileIsNotATransferBetweenAccounts()
    {
        var pairs = TransferDetector.Find(
            [
                ("giro.csv", Entry(0, Day, 2000m, inflow: false)),
                ("giro.csv", Entry(1, Day, 2000m, inflow: true)),
            ]
        );

        Assert.AreEqual(0, pairs.Count);
    }

    [TestMethod]
    public void DifferentAmountsAreNotTransfers()
    {
        var pairs = TransferDetector.Find(
            [
                ("giro.csv", Entry(0, Day, 2000m, inflow: false)),
                ("tagesgeld.csv", Entry(1, Day, 1999m, inflow: true)),
            ]
        );

        Assert.AreEqual(0, pairs.Count);
    }

    [TestMethod]
    public void AMirrorBeyondTheWindowIsNotATransfer()
    {
        var pairs = TransferDetector.Find(
            [
                ("giro.csv", Entry(0, Day, 2000m, inflow: false)),
                ("tagesgeld.csv", Entry(1, Day.AddDays(2), 2000m, inflow: true)),
            ],
            dayWindow: 1
        );

        Assert.AreEqual(0, pairs.Count);
    }

    [TestMethod]
    public void ACardSettlementPairsEvenWhenTheDirectDebitLandsDaysLater()
    {
        // 2026-08-21 stands on the card account, 2026-08-25 on the current account: the same
        // 214,94 four days apart, and each row says it is settling a card.
        var pairs = TransferDetector.Find(
            [
                (
                    "giro.csv",
                    Entry(
                        0,
                        Day.AddDays(4),
                        214.94m,
                        inflow: false,
                        payee: "Deutsche Kreditbank Berlin",
                        memo: "KREDITKARTENABRECHNUNG VISA -ABR."
                    )
                ),
                (
                    "visa.csv",
                    Entry(
                        1,
                        Day,
                        214.94m,
                        inflow: true,
                        payee: "Ausgleich Kreditkarte gem",
                        memo: "Lastschrift"
                    )
                ),
            ]
        );

        Assert.AreEqual(1, pairs.Count);
        Assert.AreEqual(4, pairs[0].DayDistance);
    }

    [TestMethod]
    public void ASettlementTheOtherSideDoesNotNameIsNoWiderThanAnyOtherMove()
    {
        var pairs = TransferDetector.Find(
            [
                (
                    "giro.csv",
                    Entry(
                        0,
                        Day.AddDays(4),
                        214.94m,
                        inflow: false,
                        memo: "KREDITKARTENABRECHNUNG VISA -ABR."
                    )
                ),
                ("visa.csv", Entry(1, Day, 214.94m, inflow: true, payee: "Some Shop")),
            ]
        );

        Assert.AreEqual(0, pairs.Count);
    }

    [TestMethod]
    public void ASettlementSixDaysApartIsNotTheSameMove()
    {
        var pairs = TransferDetector.Find(
            [
                (
                    "giro.csv",
                    Entry(
                        0,
                        Day.AddDays(6),
                        214.94m,
                        inflow: false,
                        memo: "KREDITKARTENABRECHNUNG VISA -ABR."
                    )
                ),
                (
                    "visa.csv",
                    Entry(1, Day, 214.94m, inflow: true, payee: "Ausgleich Kreditkarte gem")
                ),
            ]
        );

        Assert.AreEqual(0, pairs.Count);
    }

    private static CorrelationEntry Entry(
        int index,
        DateOnly date,
        decimal amount,
        bool inflow,
        string payee = "Some Person",
        string memo = "Umbuchung"
    ) => new(index, date, payee, amount, memo, inflow);
}
