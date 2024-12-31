namespace AutoYnabCsv.Contracts;

public static class KnownSources
{
    public static readonly Detection None = new(sourceType: "none", successful: false);
    public static readonly Detection N26 = new(sourceType: "n26", successful: true);
    public static readonly Detection DkbGiro = new(sourceType: "dkb.giro", successful: true);
}
