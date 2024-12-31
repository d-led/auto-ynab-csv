using AutoYnabCsv.Detectors;
using ConsoleAppFramework;

namespace AutoYnabCsv.App;

internal static class Program
{
    private static void Main(string[] args)
    {
        var app = ConsoleApp.Create();

        app.Add("detect", DetectCommand);

        app.Run(args);
    }

    private static void DetectCommand(string input)
    {
        try
        {
            Console.WriteLine(DetectInputType(input));
        }
        catch (FileNotFoundException)
        {
            SetExitCode(1);
            Console.WriteLine($"File not found: {input}");
        }
    }

    private static void SetExitCode(int code)
    {
        Environment.ExitCode = code;
    }

    private static string DetectInputType(string input)
    {
        var contents = File.ReadAllText(input);
        var detection = DetectFirst.Instance.TryDetect(contents);
        return detection.SourceType;
    }
}