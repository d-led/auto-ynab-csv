using System.Text;
using AutoYnabCsv.Converters;
using AutoYnabCsv.Detectors;
using AutoYnabCsv.Exporters;
using ConsoleAppFramework;

namespace AutoYnabCsv.App;

internal static class Program
{
    private static void Main(string[] args)
    {
        if (args.Length == 1 && File.Exists(args[0]))
        {
            ConvertToConsole(args[0]);
            return;
        }

        var app = ConsoleApp.Create();

        app.Add("detect", DetectCommand);

        app.Run(args);
    }

    private static void ConvertToConsole(string path)
    {
        try
        {
            Console.WriteLine(ConvertFile(path));
        }
        catch (FormatNotSupportedException)
        {
            SetExitCode(1);
            Console.WriteLine($"Format not supported: {path}");
        }
        catch (NoConverterFoundException)
        {
            SetExitCode(1);
            Console.WriteLine($"No converter found for: {path}");
        }
    }

    private static string ConvertFile(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var conversion = DetectAndConvert.Instance.Convert(text);
        return YnabCsvExporter.Export(conversion);
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
        var contents = File.ReadAllText(input, Encoding.UTF8);
        var detection = DetectFirst.Instance.TryDetect(contents);
        return detection.SourceType;
    }
}