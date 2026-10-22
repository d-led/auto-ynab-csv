using System.Text;
using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

[TestClass]
public class EmlxReaderTest
{
    private static readonly DateTimeOffset Received = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [TestMethod]
    public void WellFormedFileYieldsItsMessageAndReceiptDate()
    {
        var message = EmlxReader.Read(TestEmlx.Build("hello", 1_700_000_000), "1.emlx");

        Assert.AreEqual("hello", Encoding.UTF8.GetString(message.RawMessage));
        Assert.AreEqual(Received, message.ReceivedAt);
    }

    [TestMethod]
    public void MissingReceiptDateIsNotGuessed()
    {
        var message = EmlxReader.Read(TestEmlx.Build("hello"), "1.emlx");

        Assert.IsNull(message.ReceivedAt);
    }

    [TestMethod]
    public void ACountThatDoesNotFitFallsBackToTheTrailer()
    {
        var declared = TestEmlx.Build("hello", 1_700_000_000);

        foreach (var wrong in new[] { "4", "9" })
        {
            var bytes = ReplaceLengthLine(declared, wrong);
            var message = EmlxReader.Read(bytes, "odd.emlx");

            Assert.AreEqual("hello", Encoding.UTF8.GetString(message.RawMessage));
            Assert.AreEqual(Received, message.ReceivedAt);
        }
    }

    private static byte[] ReplaceLengthLine(byte[] file, string length)
    {
        var text = Encoding.ASCII.GetString(file);
        var newline = text.IndexOf('\n');

        return Encoding.UTF8.GetBytes(length + text[newline..]);
    }

    [TestMethod]
    public void LengthLineThatExceedsTheFileIsRejected()
    {
        var bytes = Encoding.ASCII.GetBytes("9999\nshort");

        var exception = Assert.ThrowsExactly<EmlxFormatException>(() =>
            EmlxReader.Read(bytes, "bad.emlx")
        );

        StringAssert.Contains(exception.Message, "declares 9999 bytes");
    }

    [TestMethod]
    public void AFileWithoutALengthLineIsRejected()
    {
        var bytes = Encoding.ASCII.GetBytes("not an emlx");

        Assert.ThrowsExactly<EmlxFormatException>(() => EmlxReader.Read(bytes, "plain.txt"));
    }
}
