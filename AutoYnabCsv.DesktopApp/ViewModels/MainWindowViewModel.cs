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
}
