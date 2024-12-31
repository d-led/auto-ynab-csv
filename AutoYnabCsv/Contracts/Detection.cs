using System.Diagnostics.CodeAnalysis;

namespace AutoYnabCsv.Contracts;

public record Detection
{
    public Detection()
    {
    }

    [SetsRequiredMembers]
    public Detection(string sourceType, bool successful)
    {
        SourceType = sourceType;
        Successful = successful;
    }

    public required string SourceType { get; init; }
    public required bool Successful { get; init; }
}
