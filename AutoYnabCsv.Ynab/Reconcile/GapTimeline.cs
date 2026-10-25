using AutoYnabCsv.Ynab.Contracts;

namespace AutoYnabCsv.Ynab.Reconcile;

/// <summary>What each side said the account held at the end of a day.</summary>
public sealed record BalancePoint(DateOnly Day, Money Bank, Money Plan)
{
    public Money Difference => Plan - Bank;
}

/// <summary>
/// Where two ledgers stop agreeing. A single balance difference says that something is wrong; the
/// days on which the difference is not zero say when it went wrong, which is the difference between
/// a number to explain and a period to look at.
/// </summary>
public sealed record GapReport(
    IReadOnlyList<BalancePoint> Points,
    DateOnly? DivergesFrom,
    DateOnly? DivergesTo,
    Money WorstDifference
)
{
    public bool LedgersAgree => DivergesFrom is null;

    public int Days => DivergesFrom is null ? 0 : DivergesTo!.Value.DayNumber - DivergesFrom.Value.DayNumber + 1;
}

/// <summary>
/// Walks the two ledgers backwards from the day the export was written. The difference on a day
/// only changes on days where the two sides moved by different amounts, so the days on which it is
/// not zero are exactly the days that cannot be explained by both.
/// </summary>
public static class GapTimeline
{
    public static GapReport Of(
        IReadOnlyList<BankRow> rows,
        IReadOnlyList<YnabTransaction> transactions,
        DateOnly cutoff,
        Money exportBalance,
        Money planBalanceAtCutoff
    )
    {
        var bankByDay = rows.Where(row => row.Date <= cutoff).GroupBy(row => row.Date)
            .ToDictionary(day => day.Key, day => Money.Sum(day.Select(row => row.Amount)));
        var planByDay = transactions.Where(transaction => transaction.Date <= cutoff)
            .GroupBy(transaction => transaction.Date)
            .ToDictionary(day => day.Key, day => Money.Sum(day.Select(transaction => transaction.Amount)));

        // What each side held at the end of a day is the balance at the cut-off minus everything that
        // moved after that day, so the walk is one pass backwards with two running sums.
        var days = bankByDay.Keys.Union(planByDay.Keys).Where(day => day <= cutoff).OrderByDescending(day => day).ToList();
        var points = new List<BalancePoint>();
        var bankMovedSince = Money.Zero;
        var planMovedSince = Money.Zero;

        foreach (var day in days)
        {
            points.Add(new BalancePoint(day, exportBalance - bankMovedSince, planBalanceAtCutoff - planMovedSince));
            bankMovedSince += bankByDay.GetValueOrDefault(day, Money.Zero);
            planMovedSince += planByDay.GetValueOrDefault(day, Money.Zero);
        }

        if (!bankByDay.ContainsKey(cutoff) && !planByDay.ContainsKey(cutoff))
        {
            points.Add(new BalancePoint(cutoff, exportBalance, planBalanceAtCutoff));
        }

        var diverging = points.Where(point => !point.Difference.IsZero).ToList();

        return new GapReport(
            Points: [.. points.OrderBy(point => point.Day)],
            DivergesFrom: diverging.Count == 0 ? null : diverging.Min(point => point.Day),
            DivergesTo: diverging.Count == 0 ? null : diverging.Max(point => point.Day),
            WorstDifference: points.Count == 0 ? Money.Zero : points.Max(point => point.Difference.Abs())
        );
    }
}
