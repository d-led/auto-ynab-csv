namespace AutoYnabCsv.Contracts;

public static class KnownSources
{
    public const string NoSource = "none";
    public static readonly Detection None = new(sourceType: NoSource, successful: false);
    public const string N26Source = "n26";
    public static readonly Detection N26 = new(sourceType: N26Source, successful: true);
    public const string DkbGiroSource = "dkb.giro";
    public static readonly Detection DkbGiro = new(sourceType: DkbGiroSource, successful: true);
}
