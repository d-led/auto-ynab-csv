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

    /// <summary>Two requests are never back to back: a snapshot then reads as a walk, not a hammer.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(300);

    /// <summary>The share of the hour below which what is left is spread instead of spent at once.</summary>
    private const int ReserveShare = 10;

    private readonly List<DateTimeOffset> _spent;
    private readonly int _perHour;
    private readonly TimeSpan _minimumInterval;
    private readonly string? _ledgerPath;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;

    /// <param name="perHour">The allowance the token has; 200 unless the API changes its mind.</param>
    /// <param name="ledgerPath">Where the hour is remembered; null keeps it in memory only.</param>
    public RequestBudget(
        int perHour = PerHour,
        string? ledgerPath = null,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? wait = null,
        TimeSpan? minimumInterval = null
    )
    {
        _perHour = perHour;
        _ledgerPath = ledgerPath;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _wait = wait ?? ((delay, token) => Task.Delay(delay, token));
        _minimumInterval = minimumInterval ?? DefaultInterval;

        var cutoff = _clock() - Window;
        _spent = [.. ReadLedger().Where(spent => spent > cutoff).OrderBy(spent => spent)];
    }

    /// <summary>Every request this window knows about, oldest first — including other runs'.</summary>
    public IReadOnlyList<DateTimeOffset> SpentTimes => _spent;

    public int Spent => _spent.Count;

    public int Remaining => Math.Max(0, _perHour - Spent);

    /// <summary>
    /// Waits as long as the pace asks for — a floor between requests, or more when the hour is nearly
    /// gone — but never longer than the caller is willing to sit still. A pace longer than that is
    /// refused with the time, because eighteen requests two minutes apart is half an hour of a command
    /// line apparently doing nothing.
    /// </summary>
    public async Task AcquireAsync(
        TimeSpan longestWait,
        Action<TimeSpan>? aboutToWait = null,
        CancellationToken cancellationToken = default
    )
    {
        Forget();

        var now = _clock();
        var untilThereIsRoom = _spent.Count < _perHour ? TimeSpan.Zero : _spent[0] + Window - now;
        var spacing = Spacing(now);
        var delay = untilThereIsRoom > spacing ? untilThereIsRoom : spacing;

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

    /// <summary>How long the next request has to wait; nothing when it may go straight away.</summary>
    public TimeSpan DelayBeforeNext()
    {
        Forget();

        var now = _clock();
        var untilThereIsRoom = _spent.Count < _perHour ? TimeSpan.Zero : _spent[0] + Window - now;
        var spacing = Spacing(now);

        return untilThereIsRoom > spacing ? untilThereIsRoom : spacing;
    }

    /// <summary>
    /// The pace: a floor between any two requests, and — once the hour is nearly gone — the rest of
    /// it spread over the time the window has left, so the last twenty requests are not spent in one
    /// breath and then waited out.
    /// </summary>
    private TimeSpan Spacing(DateTimeOffset now)
    {
        if (_spent.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var sinceLast = _spent[^1] + _minimumInterval - now;
        var floor = sinceLast > TimeSpan.Zero ? sinceLast : TimeSpan.Zero;
        var left = _perHour - _spent.Count;

        if (left > _perHour / ReserveShare)
        {
            return floor;
        }

        // One more slot than is left, so the last request still lands inside the window rather than
        // exactly as the oldest one expires.
        var spread = left > 0 ? (_spent[0] + Window - now) / (left + 1) : TimeSpan.Zero;
        return spread > floor ? spread : floor;
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
