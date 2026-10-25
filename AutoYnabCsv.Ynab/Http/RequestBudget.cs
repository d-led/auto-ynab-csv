namespace AutoYnabCsv.Ynab.Http;

/// <summary>
/// How much of the hour's allowance is left. YNAB allows 200 requests an hour on a rolling window and
/// sends nothing saying what is left, so the count is kept here — and written to disk, because two
/// runs a minute apart are spending one budget, not two.
/// </summary>
public sealed class RequestBudget
{
    public const int PerHour = 200;

    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly List<DateTimeOffset> _spent;
    private readonly int _perHour;
    private readonly string? _ledgerPath;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;

    /// <param name="perHour">The allowance the token has; 200 unless the API changes its mind.</param>
    /// <param name="ledgerPath">Where the hour is remembered; null keeps it in memory only.</param>
    public RequestBudget(
        int perHour = PerHour,
        string? ledgerPath = null,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? wait = null
    )
    {
        _perHour = perHour;
        _ledgerPath = ledgerPath;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _wait = wait ?? ((delay, token) => Task.Delay(delay, token));

        var cutoff = _clock() - Window;
        _spent = [.. ReadLedger().Where(spent => spent > cutoff).OrderBy(spent => spent)];
    }

    /// <summary>Every request this window knows about, oldest first — including other runs'.</summary>
    public IReadOnlyList<DateTimeOffset> SpentTimes => _spent;

    public int Spent => _spent.Count;

    public int Remaining => Math.Max(0, _perHour - Spent);

    /// <summary>
    /// Waits while the window is full — but only that long. An hour of sitting still is not a wait a
    /// command line should take without being asked, so a longer one is refused and dated instead.
    /// </summary>
    public async Task AcquireAsync(
        TimeSpan longestWait,
        Action<TimeSpan>? aboutToWait = null,
        CancellationToken cancellationToken = default
    )
    {
        var delay = DelayBeforeNext();

        if (delay > longestWait)
        {
            throw new RequestBudgetExhaustedException(delay);
        }

        if (delay > TimeSpan.Zero)
        {
            aboutToWait?.Invoke(delay);
            await _wait(delay, cancellationToken);
        }

        Spend(_clock());
    }

    /// <summary>How long the next request has to wait; nothing when there is room for it.</summary>
    public TimeSpan DelayBeforeNext()
    {
        Forget();
        return _spent.Count < _perHour ? TimeSpan.Zero : _spent[0] + Window - _clock();
    }

    private void Spend(DateTimeOffset at)
    {
        _spent.Add(at);

        if (_ledgerPath is null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_ledgerPath))!);
        File.WriteAllLines(_ledgerPath, _spent.Select(spent => spent.ToString("O")));
    }

    /// <summary>A request older than the window is not spent any more.</summary>
    private void Forget()
    {
        var cutoff = _clock() - Window;
        _spent.RemoveAll(spent => spent <= cutoff);
    }

    /// <summary>
    /// The API refused, which is the only statement it ever makes about the allowance: take it at its
    /// word and count the hour as spent, so nothing else is sent on a budget that is not there.
    /// </summary>
    public void MarkHourSpent()
    {
        var now = _clock();
        while (_spent.Count < _perHour)
        {
            _spent.Add(now);
        }

        Spend(now);
    }

    private IEnumerable<DateTimeOffset> ReadLedger() =>
        _ledgerPath is null || !File.Exists(_ledgerPath)
            ? []
            : File.ReadAllLines(_ledgerPath)
                .Select(line => DateTimeOffset.TryParse(line, out var spent) ? spent : (DateTimeOffset?)null)
                .OfType<DateTimeOffset>();
}
