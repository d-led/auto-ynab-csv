
using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Contracts;

namespace Tests.Ynab;

/// <summary>
/// Importing is the one command that creates money rows, so what is tested here is the gate in front
/// of it: nothing is created until two answers say so, and a cancel cancels everything.
/// </summary>
[TestClass]
public sealed class YnabImportTest
{
    private static readonly YnabAccount[] Accounts =
    [
        Account("a-emergency", "Giro Emergency"),
        Account("a-xiu", "Xiu Giro – 3259"),
    ];

    private static YnabAccount Account(string id, string name) =>
        new(id, name, "checking", true, false, Money.Zero, Money.Zero, Money.Zero);

    [TestMethod]
    public void AFileNamedAfterAnAccountNeedsNoQuestion()
    {
        var files = new[] { File("Giro Emergency", ("2026-10-01", "DKB AG", "Abrechnung", -0.01m)) };

        var proposed = ImportSession.Propose(files, Accounts);

        Assert.AreEqual("a-emergency", proposed["Giro Emergency"]);
    }

    [TestMethod]
    public void AFileThePlanCannotPlaceIsADashAndAMissingPicture()
    {
        var files = new[]
        {
            File("Giro Emergency", ("2026-10-01", "DKB AG", "", -0.01m)),
            File("N26", ("2026-10-01", "VOI", "", -2.5m)),
        };

        var proposed = ImportSession.Propose(files, Accounts);
        var review = ImportSession.Review(files, proposed, Accounts);

        Assert.IsNull(proposed["N26"], "an account the plan does not know is never guessed");
        StringAssert.Contains(review, "no such account in the plan");
        StringAssert.Contains(review, "Giro Emergency");
    }

    [TestMethod]
    public async Task CancelCreatesNothingAtAll()
    {
        var created = 0;

        var exit = await ImportSession.RunAsync(
            [File("N26", ("2026-10-01", "VOI", "", -2.5m))],
            Accounts,
            new Dictionary<string, string>(),
            ask: _ => "c",
            say: _ => { },
            create: _ =>
            {
                created++;
                return Task.FromResult(0);
            },
            folder: "import"
        );

        Assert.AreEqual(0, exit, "a cancel is not a failure");
        Assert.AreEqual(0, created, "and it creates nothing");
    }

    [TestMethod]
    public async Task NothingIsCreatedWithoutBothConfirmations()
    {
        var created = 0;

        async Task<int> CountThem(IReadOnlyDictionary<string, string> _)
        {
            created++;
            return await Task.FromResult(0);
        }

        var onlyTheFirstYes = await ImportSession.RunAsync(
            [File("Giro Emergency", ("2026-10-01", "DKB AG", "", -0.01m))],
            Accounts,
            new Dictionary<string, string>(),
            ask: _ => "y",
            say: _ => { },
            create: CountThem,
            folder: "import"
        );

        Assert.AreEqual(0, onlyTheFirstYes);
        Assert.AreEqual(0, created, "one yes is not enough");
    }

    [TestMethod]
    public async Task BothAnswersGivenTheRowsAreCreatedOnce()
    {
        var answers = new Queue<string>(["y", "import"]);
        var created = new List<IReadOnlyDictionary<string, string>>();

        var exit = await ImportSession.RunAsync(
            [File("Giro Emergency", ("2026-10-01", "DKB AG", "Abrechnung", -0.01m))],
            Accounts,
            new Dictionary<string, string>(),
            ask: _ => answers.Dequeue(),
            say: _ => { },
            create: ids =>
            {
                created.Add(ids);
                return Task.FromResult(0);
            },
            folder: "import"
        );

        Assert.AreEqual(0, exit);
        Assert.AreEqual(1, created.Count, "created exactly once");
        Assert.AreEqual("a-emergency", created[0]["Giro Emergency"], "in the account that was reviewed");
    }

    [TestMethod]
    public void TheRowKeepsTheSameImportIdSoASecondRunAddsNothing()
    {
        var row = new ImportRow(new DateOnly(2026, 10, 1), "DKB AG", "Abrechnung", -0.01m);

        var first = CsvImport.ImportIdOf("Giro Emergency", row);
        var again = CsvImport.ImportIdOf("Giro Emergency", row);

        Assert.AreEqual(first, again, "YNAB recognises the row it already has");
        Assert.IsTrue(first.Length <= CsvImport.MaxImportIdLength, $"{first} is too long for YNAB");
    }

    [TestMethod]
    public void APlanWrittenForImportIsReadBackWithItsAccountAndAmount()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"import-{Guid.NewGuid():N}");
        var plan = CsvImport.Plan(
            "plan-1",
            new Dictionary<string, string> { ["Giro Emergency"] = "a-emergency" },
            [File("Giro Emergency", ("2026-10-01", "DKB AG", "Abrechnung", -0.01m), ("2026-10-02", "Xiu", "Deposit", 300m))]
        );
        PlanCsv.Write(plan, folder);

        var read = CsvImport.Read(folder).Single();

        Assert.AreEqual("Giro Emergency", read.AccountName);
        Assert.AreEqual(2, read.Rows.Count);
        Assert.AreEqual(299.99m, read.Total.Amount, "the file adds up to what the plan said");
    }

    [TestMethod]
    public void TheCommandItPrintsIsTheOneThatAsksNoQuestions()
    {
        var files = new[] { File("Giro Emergency", ("2026-10-01", "DKB AG", "", -0.01m)) };

        var command = CsvImport.CommandFor(
            new Dictionary<string, string> { ["Giro Emergency"] = "a-emergency" },
            files,
            "/tmp/import"
        );

        StringAssert.Contains(command, "ynab import --csv /tmp/import");
        StringAssert.Contains(
            command,
            "--account \"Giro Emergency\"=a-emergency",
            "an account name with a space is quoted, or the shell reads it as two arguments"
        );
        StringAssert.Contains(command, "--apply --yes");
    }

    private static ImportFile File(string account, params (string Date, string Payee, string Memo, decimal Amount)[] rows) =>
        new(
            $"/tmp/import/{account}.csv",
            account,
            [.. rows.Select(row => new ImportRow(DateOnly.Parse(row.Date), row.Payee, row.Memo, row.Amount))]
        );
}
