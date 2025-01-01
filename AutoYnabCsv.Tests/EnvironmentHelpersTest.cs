using AutoYnabCsv.Common;

namespace Tests;

[TestClass]
public class EnvironmentHelpersTest
{
    private const string Key = "TEST_VARIABLE_KEY";
    private const string Prefix = "some-.*-prefix-";
    private const string Postfix = "value";

    [TestMethod]
    public void SuccessfulReplacementIfValueNotEmpty()
    {
        Environment.SetEnvironmentVariable(Key, Prefix);
        Assert.AreEqual(
            $"${Key}{Postfix}",
            EnvironmentHelpers.ReplaceWithVariableNameIfStartsWithValue(Prefix + Postfix, Key)
        );
    }

    [TestMethod]
    public void NoReplacementIfDoesntStartWithPrefix()
    {
        Environment.SetEnvironmentVariable(Key, Prefix);
        const string input = $"spoiler-${Prefix}{Postfix}";

        Assert.AreEqual(
            input,
            EnvironmentHelpers.ReplaceWithVariableNameIfStartsWithValue(input, Key)
        );
    }
}