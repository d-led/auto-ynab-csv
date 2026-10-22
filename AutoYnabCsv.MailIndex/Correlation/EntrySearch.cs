using System.Globalization;
using AutoYnabCsv.MailIndex.Mail;

namespace AutoYnabCsv.MailIndex.Correlation;

/// <summary>
/// One entry the second pass is looking for: the day it happened, what it cost, and the pattern
/// the mailbox is being asked about. A whole run can take a while, so it says what it is doing
/// instead of leaving a blank line.
/// </summary>
public sealed record EntrySearch(DateOnly Day, string Payee, decimal Amount, MailQuery Query)
{
    public string Describe() =>
        $"{Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}  "
        + $"{Amount.ToString("0.00", CultureInfo.InvariantCulture)}  "
        + $"{Payee}  →  {Query.Describe()}";
}
