namespace Tests;

public static class TestHelpers
{
    private const string SamplesDir = "../../../../data/samples/";

    public static string SampleTextOf(string sample)
    {
        var input = File.ReadAllText(Path.Join(SamplesDir, sample));
        return input;
    }
}