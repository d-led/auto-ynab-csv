using AutoYnabCsv.MailIndex.Mail;

namespace Tests;

[TestClass]
public class HtmlToTextTest
{
    [TestMethod]
    public void HtmlIsReducedToTextWithoutTagsOrScripts()
    {
        var text = HtmlToText.Convert(
            "<html><head><style>.hidden{display:none}</style></head>"
                + "<body><p>Hello &amp; welcome</p><p>Total: 42,99 €</p></body></html>"
        );

        Assert.AreEqual("Hello & welcome\nTotal: 42,99 €", text);
    }

    [TestMethod]
    public void ALineBreaksWhereAReaderSeesOne()
    {
        var text = HtmlToText.Convert(
            "<div>Arriving Tuesday</div><div>Order # 000-0000000-0000001</div>"
                + "<div>Sold by Beispiel Versand<br/>Condition: New</div>"
        );

        Assert.AreEqual(
            "Arriving Tuesday\nOrder # 000-0000000-0000001\nSold by Beispiel Versand\nCondition: New",
            text
        );
    }

    [TestMethod]
    public void AnImageIsReadByWhatItDescribesEvenWhenTheLinkBesideItIsCutShort()
    {
        var text = HtmlToText.Convert(
            "<table><tr><td><a href='https://shop/x'><img src='x.jpg' alt='Beispiel Federn-Set "
                + "12 Größen Edelstahl'></a></td>"
                + "<td><a href='https://shop/x'>Beispiel Federn-Set 12 Größe...</a>"
                + "<div>Sold by Beispiel Versand</div></td></tr></table>"
        );

        Assert.AreEqual(
            "Beispiel Federn-Set 12 Größen Edelstahl\n"
                + "Beispiel Federn-Set 12 Größe...\nSold by Beispiel Versand",
            text
        );
    }

    [TestMethod]
    public void EmptyHtmlYieldsEmptyText()
    {
        Assert.AreEqual(string.Empty, HtmlToText.Convert(string.Empty));
    }
}
