using AutoYnabCsv.Ynab;

namespace Tests.Ynab;

[TestClass]
public class SecretsTest
{
    private const string Token = "secret-token-abc-123";

    [TestMethod]
    public void TheTokenDoesNotSurviveAnErrorMessage()
    {
        var message = Secrets.Redact($"YNAB answered 401 (Unauthorized): token {Token} is not valid", Token);

        Assert.IsFalse(message.Contains(Token, StringComparison.Ordinal), "the token must never be printed");
        StringAssert.Contains(message, Secrets.Mask);
    }

    [TestMethod]
    public void ABearerHeaderIsMaskedEvenWhenTheTokenIsUnknown()
    {
        var message = Secrets.Redact("Authorization: Bearer abc.def-ghi_123 sent", secret: null);

        Assert.IsFalse(message.Contains("abc.def-ghi_123", StringComparison.Ordinal));
        StringAssert.Contains(message, $"Bearer {Secrets.Mask}");
    }

    [TestMethod]
    public void AMessageWithoutASecretIsLeftAlone()
    {
        Assert.AreEqual("no such plan", Secrets.Redact("no such plan", Token));
    }
}
