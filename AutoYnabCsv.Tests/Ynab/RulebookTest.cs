using AutoYnabCsv.Ynab.Contracts;
using AutoYnabCsv.Ynab.Rules;

namespace Tests.Ynab;

[TestClass]
public class RulebookTest
{
    [TestMethod]
    public void APayeeRuleCatchesTheShopWhateverTheCase()
    {
        var proposals = Book(Rule(payeeContains: "edeka", category: "groceries")).Propose([T("t1", "EDEKA SAGT DANKE")]);

        Assert.AreEqual("groceries", proposals.Single().Rule.CategoryName);
    }

    [TestMethod]
    public void APayeeTheRuleNamesIsNotQuotedTheOtherWayRound()
    {
        var proposals = Book(Rule(payeeContains: "edeka", category: "groceries")).Propose([T("t1", "Kaufland")]);

        Assert.AreEqual(0, proposals.Count);
    }

    [TestMethod]
    public void AnAmountRuleNeedsTheAmountItNames()
    {
        var book = Book(Rule(amountMilliunits: -45810, category: "groceries"));

        Assert.AreEqual(1, book.Propose([T("t1", "Kaufland", -45.81m)]).Count);
        Assert.AreEqual(0, book.Propose([T("t2", "Kaufland", -45.82m)]).Count, "an amount rule is exact");
    }

    [TestMethod]
    public void ADateRuleKeepsToItsDays()
    {
        var book = Book(Rule(from: "2026-01-01", to: "2026-01-31", category: "groceries"));

        Assert.AreEqual(1, book.Propose([T("t1", "Shop", date: "2026-01-15")]).Count);
        Assert.AreEqual(0, book.Propose([T("t2", "Shop", date: "2026-02-01")]).Count);
        Assert.AreEqual(0, book.Propose([T("t3", "Shop", date: "2025-12-31")]).Count);
    }

    [TestMethod]
    public void AMemoRuleReadsTheMemoAndNotThePayee()
    {
        var proposals = Book(Rule(memoContains: "abo", category: "subscriptions"))
            .Propose([T("t1", "Spotify", memo: "Abo Oktober"), T("t2", "Spotify", memo: "Einmalig")]);

        Assert.AreEqual("t1", proposals.Single().Transaction.Id);
    }

    [TestMethod]
    public void UncategorizedOnlyLeavesTheAnsweredOnesAlone()
    {
        var book = Book(Rule(payeeContains: "shop", uncategorizedOnly: true, category: "groceries"));

        Assert.AreEqual(1, book.Propose([T("t1", "Shop")]).Count);
        Assert.AreEqual(0, book.Propose([T("t2", "Shop", category: "dining")]).Count);
    }

    [TestMethod]
    public void TheFirstRuleThatMatchesWins()
    {
        var book = Book(Rule(payeeContains: "shop", category: "first"), Rule(payeeContains: "shop", category: "second"));

        Assert.AreEqual("first", book.Propose([T("t1", "Shop")]).Single().Rule.CategoryName);
    }

    [TestMethod]
    public void ARowAlreadyInTheCategoryItWouldGetIsNotProposed()
    {
        var proposals = Book(Rule(payeeContains: "shop", category: "groceries")).Propose([T("t1", "Shop", category: "groceries")]);

        Assert.AreEqual(0, proposals.Count, "a proposal is a change or it is nothing");
    }

    [TestMethod]
    public void ATransferIsNotCategorizedEvenWhenARuleFitsIt()
    {
        var transfer = Sample.T("t1", "2026-10-23", -500m, "Transfer : Emergency", transferAccountId: "acc-b");

        Assert.AreEqual(0, Book(Rule(payeeContains: "Transfer")).Propose([transfer]).Count);
    }

    [TestMethod]
    public void TheProposalsBecomeAPlanThatTouchesNothingElse()
    {
        var proposals = Book(Rule(payeeContains: "shop", category: "groceries")).Propose([T("t1", "Shop")]);

        var plan = Book(Rule(payeeContains: "shop", category: "groceries")).Plan(proposals, "plan-1", DateTimeOffset.UnixEpoch);

        Assert.AreEqual(1, plan.Categories.Count);
        Assert.AreEqual("t1", plan.Categories[0].TransactionId);
        StringAssert.Contains(plan.Categories[0].Why, "payee contains 'shop'");
        Assert.AreEqual(0, plan.Creates.Count + plan.Deletes.Count + plan.Moves.Count, "a rule categorizes and nothing else");
    }

    [TestMethod]
    public void TheRuleIsReadFromAFileAsWrittenByHand()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ayc-rules-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            path,
            """
            {
              // a comment, because this file is written by a person
              "rules": [
                { "match": { "payeeContains": "edeka", "uncategorizedOnly": true }, "categoryId": "cat-1", "categoryName": "Groceries" },
              ]
            }
            """
        );

        var rule = Rulebook.Load(path).Rules.Single();

        Assert.AreEqual("cat-1", rule.CategoryId);
        Assert.AreEqual("edeka", rule.Match.PayeeContains);
        Assert.IsTrue(rule.Match.UncategorizedOnly);
    }

    [TestMethod]
    public void AMissingRulesFileSaysWhichOne()
    {
        var failure = Assert.ThrowsExactly<RulebookUnreadableException>(() => Rulebook.Load("/nowhere/rules.json"));

        StringAssert.Contains(failure.Message, "/nowhere/rules.json");
    }

    [TestMethod]
    public void AFileThatIsNotJsonSaysWhy()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ayc-rules-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "not json at all");

        Assert.ThrowsExactly<RulebookUnreadableException>(() => Rulebook.Load(path));
    }

    [TestMethod]
    public void ARuleThatSaysNothingSaysSoInThePlan()
    {
        var proposed = Book(new CategorizationRule(new RuleMatch(), "cat-1", "Everything")).Propose([T("t1", "Shop")]);

        Assert.AreEqual(1, proposed.Count, "a rule with nothing set matches everything, as it says");

        var plan = Book(new CategorizationRule(new RuleMatch(), "cat-1", "Everything"))
            .Plan(proposed, "plan-1", DateTimeOffset.UnixEpoch);

        StringAssert.Contains(plan.Categories[0].Why, "everything");
    }

    private static Rulebook Book(params CategorizationRule[] rules) => new(rules);

    private static CategorizationRule Rule(
        string? payeeContains = null,
        string? memoContains = null,
        long? amountMilliunits = null,
        string? from = null,
        string? to = null,
        bool uncategorizedOnly = false,
        string category = "groceries"
    ) =>
        new(
            new RuleMatch(
                payeeContains,
                memoContains,
                amountMilliunits,
                from is null ? null : DateOnly.Parse(from),
                to is null ? null : DateOnly.Parse(to),
                uncategorizedOnly
            ),
            $"cat-{category}",
            category
        );

    private static YnabTransaction T(
        string id,
        string payee,
        decimal amount = -10m,
        string date = "2026-10-23",
        string? memo = null,
        string? category = null
    ) =>
        Sample.T(id, date, amount, payee, category is null ? null : new YnabCategory($"cat-{category}", category, "Everyday", false))
        with
        { Memo = memo };
}
