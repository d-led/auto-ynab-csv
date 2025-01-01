using System.Reflection;
using System.Text;
using AutoYnabCsv.Converters;
using AutoYnabCsv.Detectors;
using AutoYnabCsv.Interactions;
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
        app.Add("convert", ConvertCommand);
        app.Add("version", VersionCommand);

        app.Run(args);
    }

    private static void ConvertToConsole(string path)
    {
        try
        {
            Console.WriteLine(TextConversion.ConvertFile(path));
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

    private static void VersionCommand()
    {
        Console.WriteLine(Assembly.GetEntryAssembly()
        ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion);
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
    
    private static void ConvertCommand(string input)
    {
        var result = AutomaticConversion.Convert(input);
        Console.WriteLine(result.Comment);
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