using System.Text.RegularExpressions;
using AutoYnabCsv.Common;

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

    [TestMethod]
    public void TestSecondsResolutionUniqueAbsoluteFilename()
    {
        const string originalFilename = "test.csv";
        const string prefix = "new";
        var filename1 = StringHelpers.SecondsResolutionUniqueAbsoluteFilename(originalFilename, prefix);
        Thread.Sleep(1010);
        var filename2 = StringHelpers.SecondsResolutionUniqueAbsoluteFilename(originalFilename, prefix);
        Assert.AreNotEqual(filename1, filename2);
        Assert.IsTrue(filename1.Contains(prefix));
        Assert.IsTrue(filename1.Contains(Path.GetFileNameWithoutExtension(originalFilename)));
        Assert.AreEqual(Path.GetExtension(filename1), Path.GetExtension(originalFilename));
        Assert.IsTrue(Path.IsPathFullyQualified(filename1));
        
        Assert.AreEqual(1, Regex.Count(filename1, Regex.Escape(Path.GetExtension(originalFilename))));
    }
}