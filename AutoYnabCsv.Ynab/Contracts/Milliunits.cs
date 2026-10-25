namespace AutoYnabCsv.Ynab.Contracts;

/// <summary>
/// YNAB counts thousandths of a currency unit: 1 000 milliunits is one euro, ‑2 990 is ‑2,99 €.
/// This is a type and not a <see cref="long"/> so the API's unit cannot be mistaken for money:
/// it converts to and from <see cref="Money"/> and does nothing else, and it is never printed.
/// </summary>
public readonly record struct Milliunits(long Value)
{
    public const long PerUnit = 1000;

    public static readonly Milliunits Zero = new(0);

    public bool IsZero => Value == 0;

    public static Milliunits FromMoney(Money money) =>
        new((long)decimal.Round(money.Amount * PerUnit, 0, MidpointRounding.AwayFromZero));

    public Money ToMoney() => new(Value / (decimal)PerUnit);

    public static Milliunits operator +(Milliunits left, Milliunits right) => new(left.Value + right.Value);

    public static Milliunits operator -(Milliunits left, Milliunits right) => new(left.Value - right.Value);

    public static Milliunits Sum(IEnumerable<Milliunits> units) =>
        new(units.Aggregate(0L, (total, next) => total + next.Value));
}

