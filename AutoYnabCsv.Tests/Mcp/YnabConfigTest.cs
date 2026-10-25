using AutoYnabCsv.Ynab.Settings;

namespace Tests.Mcp;

[TestClass]
public class YnabConfigTest
{
    [TestMethod]
    public void EverySettingInTheFileIsUnderstood()
    {
        var config = Load(
            """
            # the plan to check
            plan=plan-1

            Girokonto · 0002=acc-1
            account=Girokonto · 0004=acc-2
            own-iban=n26=DE00000000000000000026
            expect-balance=n26=-38,59
            """
        );

        Assert.AreEqual("plan-1", config.Plan);
        Assert.AreEqual("acc-1", config.Accounts["Girokonto · 0002"], "a bare line is an account");
        Assert.AreEqual("acc-2", config.Accounts["Girokonto · 0004"]);
        Assert.AreEqual("DE00000000000000000026", config.OwnIbans["n26"]);
        Assert.AreEqual("-38,59", config.ExpectedBalances["n26"]);
    }

    [TestMethod]
    public void AMissingFileIsNoSettingsRatherThanAFailure()
    {
        Assert.AreEqual(YnabConfig.Empty, YnabConfig.Load("/nowhere/accounts.txt"));
    }

    [TestMethod]
    public void ALineWithoutAnEqualsSaysWhichLine()
    {
        var failure = Assert.ThrowsExactly<YnabConfigUnreadableException>(() => Load("plan=plan-1\nnot a setting"));

        StringAssert.Contains(failure.Message, "not a setting");
    }

    [TestMethod]
    public void ALabelWithoutAValueIsRefused()
    {
        var failure = Assert.ThrowsExactly<YnabConfigUnreadableException>(() => Load("account=just-a-label"));

        StringAssert.Contains(failure.Message, "label=value");
    }

    private static YnabConfig Load(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ayc-config-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return YnabConfig.Load(path);
    }
}
