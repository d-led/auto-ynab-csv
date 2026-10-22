using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoYnabCsv.DesktopApp.Interactions;
using AutoYnabCsv.Interactions;
using AutoYnabCsv.MailIndex;
using AutoYnabCsv.MailIndex.Extraction;
using AutoYnabCsv.MailIndex.Index;
using AutoYnabCsv.MailIndex.Mail;
using AutoYnabCsv.MailIndex.Web;
using ReactiveUI;

namespace AutoYnabCsv.DesktopApp.ViewModels;

/// <summary>One place the app keeps something: shown as a table row and copyable.</summary>
public sealed record LocationRow(string Name, string Path);

public class MainWindowViewModel : ViewModelBase
{
    private string _log = string.Empty;
    private string _mailStatus = "Mail is optional: nothing needs configuring.";
    private IReadOnlyList<string> _lastDropped = [];
    private IReadOnlyList<LocationRow> _locations = LocationsFor([]);

    public string Log
    {
        get => _log;
        set => this.RaiseAndSetIfChanged(ref _log, value);
    }

    public string MailStatus
    {
        get => _mailStatus;
        set => this.RaiseAndSetIfChanged(ref _mailStatus, value);
    }

    /// <summary>Everything the app keeps, as table rows so each path can be read and copied.</summary>
    public IReadOnlyList<LocationRow> Locations
    {
        get => _locations;
        private set => this.RaiseAndSetIfChanged(ref _locations, value);
    }

    public async Task ProcessDropOf(IEnumerable<string> files)
    {
        _lastDropped = files.ToArray();
        Locations = LocationsFor(_lastDropped);

        string lastSuccessfulOutput = string.Empty;
        foreach (var file in files)
        {
            var result = AutomaticConversion.Convert(file);
            Log += result.Comment + "\n\n";

            if (result.Success)
                lastSuccessfulOutput = result.ConvertedFilename;
        }

        if (lastSuccessfulOutput != string.Empty)
            WithSystem.RevealFile(lastSuccessfulOutput);

        await WriteReportAsync();
    }

    public void RevealIndex() =>
        WithSystem.OpenFolder(Path.GetDirectoryName(MailIndexStore.DefaultPath())!);

    public void RevealConfig() => WithSystem.OpenFolder(AppDirectories.ConfigDirectory());

    public void RevealExport() => WithSystem.OpenFolder(ReportLocation.For(_lastDropped));

    /// <summary>The places the app reads and writes, with the report beside the dropped files.</summary>
    private static IReadOnlyList<LocationRow> LocationsFor(IReadOnlyList<string> dropped) =>
    [
        new("index", MailIndexStore.DefaultPath()),
        new("config", Path.Combine(AppDirectories.ConfigDirectory(), RuleSetLoader.FileName)),
        new("report", ReportLocation.For(dropped)),
    ];

    /// <summary>
    /// Indexes Apple Mail from inside the app, which is the process that needs Full Disk Access.
    /// </summary>
    public async Task IndexMailAsync()
    {
        MailStatus = "Indexing mail…";

        try
        {
            var store = MailStoreLocator.FindReadableStores().FirstOrDefault()
                ?? throw new DirectoryNotFoundException("No Apple Mail store found.");

            var rules = RuleSetLoader.Load();
            using var index = MailIndexStore.Open(MailIndexStore.DefaultPath());

            var report = await Task.Run(() => MailSyncService.Sync(index, store.Root, rules));
            MailStatus = report.Describe();
        }
        catch (MailStoreUnavailableException exception)
        {
            MailStatus = exception.Message;
        }
        catch (DirectoryNotFoundException exception)
        {
            MailStatus = exception.Message;
        }
    }

    /// <summary>
    /// Writes the timestamped run for the files last dropped: the audit trail, the searchable
    /// page, the transfer diagram, and the rows to import — all in one folder.
    /// </summary>
    public Task ExportReportAsync() => WriteReportAsync();

    private async Task WriteReportAsync()
    {
        if (_lastDropped.Count == 0)
        {
            MailStatus = "Drop the CSV files first.";
            return;
        }

        MailStatus = "Writing the report…";

        try
        {
            var rules = RuleSetLoader.Load();
            using var index = MailIndexStore.Open(MailIndexStore.DefaultPath());

            var context = ReportContext.For(
                index,
                MailIndexStore.DefaultPath(),
                rules,
                typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString() ?? "dev"
            );

            var run = await Task.Run(() =>
                ReportService.Write(context, _lastDropped, ReportLocation.For(_lastDropped))
            );

            MailStatus = run.Export.CsvPaths.Count == 0
                ? $"Report: {run.Export.HtmlPath}"
                : $"Report: {run.Export.HtmlPath} — {run.Export.CsvPaths.Count} YNAB CSV(s) in "
                    + Path.Combine(run.Export.Directory, EnrichedCsvWriter.DirectoryName);
            WithSystem.RevealFile(run.Export.HtmlPath);
        }
        catch (MailStoreUnavailableException exception)
        {
            MailStatus = exception.Message;
        }
        catch (IOException exception)
        {
            MailStatus = exception.Message;
        }
    }
}