using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

[TestClass]
public class MessageParserTest
{
    private static readonly DateTimeOffset Received = new(2024, 12, 25, 10, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void AMessageIsReducedToSubjectSenderTextAndReceiptDate()
    {
        var raw = TestEmlx.Build(
            TestEmlx.Message(
                "Ihre Bestellung 302-1234567-8901234",
                "ship-confirm@amazon.de",
                "Gesamtsumme: EUR 42,99",
                Received,
                "<302.1234567@amazon.de>"
            ),
            Received.ToUnixTimeSeconds()
        );

        var record = MessageParser.Parse(EmlxReader.Read(raw, "1.emlx"));

        Assert.AreEqual("ship-confirm@amazon.de", record.SenderAddress);
        Assert.AreEqual("Ihre Bestellung 302-1234567-8901234", record.Subject);
        StringAssert.Contains(record.Text, "Gesamtsumme: EUR 42,99");
        Assert.AreEqual(Received, record.ReceivedAt);
        Assert.AreEqual("302.1234567@amazon.de", record.MessageId);
    }
}
