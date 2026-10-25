using System.Net;

namespace AutoYnabCsv.Ynab;

/// <summary>What YNAB said when it said no. One type, so an apply can report a refused action and
/// carry on with the rest instead of stopping half-way through a repair.</summary>
[Serializable]
public sealed class YnabApiException(HttpStatusCode status, string detail)
    : Exception($"YNAB answered {(int)status} ({status}): {detail}")
{
    public HttpStatusCode Status { get; } = status;
}
