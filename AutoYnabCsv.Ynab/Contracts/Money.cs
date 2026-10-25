using System.Globalization;

namespace AutoYnabCsv.Ynab.Contracts;

/// <summary>
/// An amount of money in the plan's currency, to the cent. It exists so that a milliunit, a
/// <see cref="decimal"/> and a double cannot be added to an amount by accident: the only way in is a
/// decimal (how money is written and printed), and the only way out to YNAB's units is explicit.
/// </summary>
public readonly record struct Money(decimal Amount) : IComparable<Money>, IFormattable
{
    public static readonly Money Zero = new(0m);

    public static implicit operator Money(decimal amount) => new(amount);

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public int Sign => Math.Sign(Amount);

    public Money Abs() => new(decimal.Abs(Amount));

    public Milliunits ToMilliunits() => Milliunits.FromMoney(this);

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);

    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);

    public static Money operator -(Money value) => new(-value.Amount);

    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;

    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;

    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;

    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;

    public static Money Sum(IEnumerable<Money> amounts) =>
        new(amounts.Aggregate(0m, (total, next) => total + next.Amount));

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    /// <summary>Two places and an invariant point, the way every report and every export writes it.</summary>
    public string ToString(string? format = null, IFormatProvider? formatProvider = null) =>
        Amount.ToString(string.IsNullOrEmpty(format) ? "0.00" : format, formatProvider ?? CultureInfo.InvariantCulture);

    public override string ToString() => ToString("0.00", CultureInfo.InvariantCulture);
}
