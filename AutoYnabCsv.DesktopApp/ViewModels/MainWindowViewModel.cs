using System.Collections.Generic;
using AutoYnabCsv.Interactions;
using ReactiveUI;

namespace AutoYnabCsv.DesktopApp.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private string _log = string.Empty;

    public string Log
    {
        get => _log;
        set => this.RaiseAndSetIfChanged(ref _log, value);
    }

    public void ProcessDropOf(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var result = AutomaticConversion.Convert(file);
            Log += result.Comment + "\n";
        }
    }
}