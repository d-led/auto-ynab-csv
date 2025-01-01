using System.Net.NetworkInformation;
using AutoYnabCsv.Common;
using Microsoft.VisualBasic;

namespace Tests;

[TestClass]
public class StringHelpersTest
{
    [TestMethod]
    public void SuccessfulReplacementIfPrefixFound()
    {
        const string prefix = "Some.*Prefix-";
        const string input = prefix+"World";
        Assert.AreEqual(
            "Hello, World",
            StringHelpers.ReplaceIfStartsWith(input, prefix, "Hello, ")
        );
    }
    
    [TestMethod]
    public void NotReplacedIfNotPrefixed()
    {
        const string input = "No-Prefix";
        Assert.AreEqual(
            input,
            StringHelpers.ReplaceIfStartsWith(input, "Some.*Prefix-", "")
        );
    }
}