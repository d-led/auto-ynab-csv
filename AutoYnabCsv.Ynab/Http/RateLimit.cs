namespace AutoYnabCsv.Ynab.Http;

/// <summary>The hour's allowance is spent and the room is not worth waiting for.</summary>
[Serializable]
public sealed class RequestBudgetExhaustedException(TimeSpan until, string? detail = null)
    : Exception(
        $"{(string.IsNullOrWhiteSpace(detail) ? "The hour's requests are spent." : detail)} "
            + $"{(string.IsNullOrWhiteSpace(detail) ? "Nothing was sent" : "This one was refused")}; "
            + $"the next request is possible in {until.TotalMinutes:0.#} minute(s)."
    )
{
    public TimeSpan Until { get; } = until;
}
