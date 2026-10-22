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

    private static CorrelationEntry Entry(int index, DateOnly date, decimal amount, bool inflow) =>
        new(index, date, "Some Person", amount, "Umbuchung", inflow);
}
