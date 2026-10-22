using System.IO;
using AutoYnabCsv.DesktopApp.Interactions;
using AutoYnabCsv.MailIndex.Extraction;
using ReactiveUI;

namespace AutoYnabCsv.DesktopApp.ViewModels;

/// <summary>
/// The patterns file as text you can edit. Nothing reaches the disk until the text loads: the save
/// is a transaction, and the status line says what the loader made of it.
/// </summary>
public class ConfigViewModel : ViewModelBase
{
    private readonly string _filePath = ConfigFile.DefaultPath;
    private string _text = ConfigFile.Read();
    private string _status = string.Empty;

    public string FilePath => _filePath;

    public string Text
    {
        get => _text;
        set => this.RaiseAndSetIfChanged(ref _text, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    /// <summary>What the last save said, for the window that opened this one; empty when none was made.</summary>
    public string Outcome { get; private set; } = string.Empty;

    public void Save()
    {
        var save = ConfigFile.Save(Text, _filePath);

        Status = save.Message;
        Outcome = save.Saved ? save.Message : string.Empty;
    }

    public void Revert()
    {
        Text = ConfigFile.Read(_filePath);
        Status = "Reverted to the file on disk.";
    }

    public void Reveal() => WithSystem.OpenFolder(Path.GetDirectoryName(_filePath)!);
}
