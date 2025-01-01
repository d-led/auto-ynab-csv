using System.Net.NetworkInformation;
using AutoYnabCsv.Common;
using Microsoft.VisualBasic;

namespace Tests;

[TestClass]
public class StringHelpersTest
{
    private const string Prefix = "Some.*Prefix-";

    [TestMethod]
    public void SuccessfulReplacementIfPrefixFound()
    {
        const string input = Prefix + "World";
        Assert.AreEqual(
            "Hello, World",
            StringHelpers.ReplaceIfStartsWith(input, Prefix, "Hello, ")
        );
    }

    [TestMethod]
    public void NotReplacedIfNotPrefixed()
    {
        const string input = "No-Prefix";
        Assert.AreEqual(
            input,
            StringHelpers.ReplaceIfStartsWith(input, Prefix, "")
        );
    }

    [TestMethod]
    public void NotReplacedIfNotPrefixInMiddle()
    {
        const string input = "spoiler" + Prefix + "World";
        Assert.AreEqual(
            input,
            StringHelpers.ReplaceIfStartsWith(input, Prefix, "")
        );
    }
}