using AutoYnabCsv.Ynab.Apply;
using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Apply;

/// <summary>
/// The files a plan wrote, imported through the API with the account chosen here: the same rows the
/// web form would take, but with the account named by the tool instead of dropped on by hand — and
/// with every choice shown before anything is created.
///
/// Nothing is written until two answers say so, and <c>c</c> cancels the whole run, not the row it
/// was typed at. A full run prints the command it amounts to, so the next one can skip the questions.
/// </summary>
public static class ImportSession
{
    /// <summary>How a row of the review reads: the file, the account it will land in, and what it adds up to.</summary>
    public static string ReviewRow(int number, ImportFile file, string account) =>
        $"{number,2}. {account,-28} {file.Rows.Count,4} row(s)  {file.Total.Amount,12:N2}  {Path.GetFileName(file.Path)}";

    /// <summary>
    /// The account each file is for: the one the file is named after when the plan knows that name,
    /// and nothing otherwise — a guess here puts money in the wrong account.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Propose(
        IReadOnlyList<ImportFile> files,
        IReadOnlyList<YnabAccount> accounts
    ) =>
        files
            .GroupBy(file => file.AccountName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                    accounts
                        .FirstOrDefault(account =>
                            string.Equals(account.Name, group.Key, StringComparison.OrdinalIgnoreCase)
                        )
                        ?.Id,
                StringComparer.Ordinal
            );

    /// <summary>What the review says before asking anything.</summary>
    public static string Review(
        IReadOnlyList<ImportFile> files,
        IReadOnlyDictionary<string, string?> chosen,
        IReadOnlyList<YnabAccount> accounts
    )
    {
        var lines = files.Select(
            (file, index) =>
                ReviewRow(
                    index + 1,
                    file,
                    chosen[file.AccountName] is { } id
                        ? accounts.First(account => account.Id == id).Name
                        : "? no such account in the plan"
                )
        );

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Runs the review and the two confirmations, then creates the rows. Answers are read from
    /// <paramref name="ask"/>, so the whole conversation can be replayed without a console.
    /// </summary>
    public static async Task<int> RunAsync(
        IReadOnlyList<ImportFile> files,
        IReadOnlyList<YnabAccount> accounts,
        IReadOnlyDictionary<string, string> given,
        Func<string, string?> ask,
        Action<string> say,
        Func<IReadOnlyDictionary<string, string>, Task<int>> create,
        string folder
    )
    {
        var chosen = new Dictionary<string, string?>(Propose(files, accounts), StringComparer.Ordinal);
        foreach (var (name, id) in given)
        {
            chosen[name] = id;
        }

        say(Review(files, chosen, accounts));

        if (chosen.Any(choice => choice.Value is null))
        {
            return await ChooseAsync(files, accounts, chosen, ask, say, create, folder);
        }

        return await ConfirmAsync(files, accounts, chosen, ask, say, create, folder);
    }

    private static async Task<int> ChooseAsync(
        IReadOnlyList<ImportFile> files,
        IReadOnlyList<YnabAccount> accounts,
        Dictionary<string, string?> chosen,
        Func<string, string?> ask,
        Action<string> say,
        Func<IReadOnlyDictionary<string, string>, Task<int>> create,
        string folder
    )
    {
        say(
            "Which account for the ones the plan does not name? "
                + "Type the number of the account, `c` to cancel everything."
        );
        say(
            string.Join(
                Environment.NewLine,
                accounts.Select(
                    (account, index) => $"{index + 1,2}. {account.Name} ({account.Type})"
                )
            )
        );

        foreach (var name in chosen.Where(choice => choice.Value is null).Select(choice => choice.Key).ToArray())
        {
            var answer = ask($"{name}: ");

            if (answer is null || answer.Trim() is "c" or "C")
            {
                say("Cancelled. Nothing was created.");
                return 0;
            }

            if (!int.TryParse(answer.Trim(), out var number) || number < 1 || number > accounts.Count)
            {
                say($"'{answer}' is not one of the accounts. Cancelled; nothing was created.");
                return 1;
            }

            chosen[name] = accounts[number - 1].Id;
        }

        return await ConfirmAsync(files, accounts, chosen, ask, say, create, folder);
    }

    private static async Task<int> ConfirmAsync(
        IReadOnlyList<ImportFile> files,
        IReadOnlyList<YnabAccount> accounts,
        IReadOnlyDictionary<string, string?> chosen,
        Func<string, string?> ask,
        Action<string> say,
        Func<IReadOnlyDictionary<string, string>, Task<int>> create,
        string folder
    )
    {
        var ids = chosen.ToDictionary(choice => choice.Key, choice => choice.Value!, StringComparer.Ordinal);
        var rows = files.Sum(file => file.Rows.Count);
        var total = files.Aggregate(Money.Zero, (sum, file) => sum + file.Total);

        say(Review(files, chosen, accounts));
        say($"Import {rows} row(s), {total.Amount:N2} in all? [y/N]");

        if (!Yes(ask("> ")))
        {
            say("Nothing was created.");
            return 0;
        }

        say($"They arrive unapproved, one request each ({rows}). Type `import` to create them.");
        if (ask("> ")?.Trim() is not "import")
        {
            say("Nothing was created.");
            return 0;
        }

        var exit = await create(ids);

        say(exit == 0 ? "Imported." : "Some rows were not created; `ynab undo --apply` reverses what was.");
        say($"Next time, without the questions: {CsvImport.CommandFor(ids, files, folder)}");

        return exit;
    }

    private static bool Yes(string? answer) =>
        answer?.Trim().ToLowerInvariant() is "y" or "yes";
}
