using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoYnabCsv.DesktopApp.Interactions;
using AutoYnabCsv.Interactions;
using AutoYnabCsv.MailIndex;
using AutoYnabCsv.MailIndex.Extraction;
using AutoYnabCsv.MailIndex.Index;
using AutoYnabCsv.MailIndex.Mail;
using AutoYnabCsv.MailIndex.Web;
using Avalonia.Threading;
using ReactiveUI;

namespace AutoYnabCsv.DesktopApp.ViewModels;

/// <summary>One place the app keeps something: shown as a table row a person can select and copy.</summary>
public sealed record LocationRow(string Name, string Path);

public class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// How often the counter may be rewritten. The sync reports every message, and a first run over
    /// a mailbox has tens of thousands of them; time, not a count, keeps the window answering.
    /// </summary>
    private const int ProgressRedrawMilliseconds = 150;

    /// <summary>A message that took real time is worth a second glance on a later run.</summary>
    private const int SlowMessageMilliseconds = 2_000;

    private string _log = string.Empty;
    private string _mailStatus = "Mail is optional: nothing needs configuring.";
    private string _indexStatus = string.Empty;
    private bool _isIndexing;
    private double _indexProgress;
    private bool _indexProgressUnknown = true;
    private IReadOnlyList<string> _lastDropped = [];
    private IReadOnlyList<LocationRow> _locations = LocationsFor([]);
    private CancellationTokenSource? _indexing;
    private long _lastProgressAt;

    public string Log
    {
        get => _log;
        set => this.RaiseAndSetIfChanged(ref _log, value);
    }

    /// <summary>What happened last: the outcome of a run, or why nothing could be read.</summary>
    public string MailStatus
    {
        get => _mailStatus;
        set => this.RaiseAndSetIfChanged(ref _mailStatus, value);
    }

    /// <summary>How far the running index sync has come, and which message it is on.</summary>
    public string IndexStatus
    {
        get => _indexStatus;
        private set => this.RaiseAndSetIfChanged(ref _indexStatus, value);
    }

    /// <summary>True while the mailbox is being read, which is what Cancel is for.</summary>
    public bool IsIndexing
    {
        get => _isIndexing;
        private set => this.RaiseAndSetIfChanged(ref _isIndexing, value);
    }

    /// <summary>Messages read over messages listed, once the walk knows how many there are.</summary>
    public double IndexProgress
    {
        get => _indexProgress;
        private set => this.RaiseAndSetIfChanged(ref _indexProgress, value);
    }

    /// <summary>True while the store is still being listed, when no fraction can be shown yet.</summary>
    public bool IndexProgressUnknown
    {
        get => _indexProgressUnknown;
        private set => this.RaiseAndSetIfChanged(ref _indexProgressUnknown, value);
    }

    /// <summary>Where this app keeps what it keeps, as table rows a person can select and copy.</summary>
    public IReadOnlyList<LocationRow> Locations
    {
        get => _locations;
        private set => this.RaiseAndSetIfChanged(ref _locations, value);
    }

    /// <summary>
    /// A drop converts each file beside itself, then writes one run: the page, the audit trail and
    /// the YNAB rows, in the folder the files came from.
    /// </summary>
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

    /// <summary>Shows one of the listed places: the file itself if it is a file, else its folder.</summary>
    public void Show(LocationRow location)
    {
        if (File.Exists(location.Path))
            WithSystem.RevealFile(location.Path);
        else
            WithSystem.OpenFolder(location.Path);
    }

    /// <summary>A line in the status bar about something that happened in another window.</summary>
    public void Report(string message)
    {
        if (message.Length > 0)
            MailStatus = message;
    }

    /// <summary>The places the app keeps things, and the run folder once a drop has said where.</summary>
    private static IReadOnlyList<LocationRow> LocationsFor(IReadOnlyList<string> dropped)
    {
        var locations = new List<LocationRow>
        {
            new("index", MailIndexStore.DefaultPath()),
            new("config", Path.Combine(AppDirectories.ConfigDirectory(), RuleSetLoader.FileName)),
        };

        if (dropped.Count > 0)
            locations.Add(new LocationRow("report", ReportLocation.For(dropped)));

        return locations;
    }

    /// <summary>The binary macOS has to be told about: Full Disk Access is granted per executable.</summary>
    public static string MailPermissionTarget => Environment.ProcessPath ?? "this app";

    /// <summary>
    /// Indexes Apple Mail from inside the app, which is the process that needs Full Disk Access.
    /// The run reports every message it touches, and stops at the next one when asked to.
    /// </summary>
    public async Task IndexMailAsync()
    {
        if (IsIndexing)
            return;

        using var cancellation = new CancellationTokenSource();
        _indexing = cancellation;
        IsIndexing = true;
        IndexProgressUnknown = true;
        IndexProgress = 0;
        MailStatus = "Reading the mailbox…";

        try
        {
            var store = MailStoreLocator.FindReadableStores().FirstOrDefault()
                ?? throw new DirectoryNotFoundException("No Apple Mail store found.");

            var rules = RuleSetLoader.Load();
            using var index = MailIndexStore.Open(MailIndexStore.DefaultPath());

            var report = await Task.Run(() =>
                MailSyncService.Sync(
                    index,
                    store.Root,
                    rules,
                    starting: ShowWhatWillBeRead,
                    progress: ShowHowFarItCame,
                    cancellation: cancellation.Token
                )
            );

            MailStatus = report.Describe();
        }
        catch (MailStoreUnavailableException exception)
        {
            MailUnavailable(exception.Message);
        }
        catch (DirectoryNotFoundException exception)
        {
            MailUnavailable(exception.Message);
        }
        finally
        {
            _indexing = null;
            IsIndexing = false;
            IndexStatus = string.Empty;
        }
    }

    /// <summary>
    /// Stops the run at the next message. What has been read stays, and the next run carries on
    /// from there — a rebuild keeps its unfinished flag, so it is not mistaken for a finished one.
    /// </summary>
    public void CancelIndexing()
    {
        if (_indexing is null)
            return;

        _indexing.Cancel();
        MailStatus = "Stopping after this message…";
    }

    /// <summary>Why the sync reads what it reads, said before it starts rather than guessed.</summary>
    private void ShowWhatWillBeRead(SyncStart start) =>
        OnUi(() =>
            MailStatus = start switch
            {
                { Rebuilding: true } => "The rules changed: every message is read again.",
                { Resuming: true } => "Resuming an interrupted rebuild: the whole mailbox again.",
                { Since: { } since } => $"Reading what was written since {since:yyyy-MM-dd}…",
                _ => "Reading the mailbox…",
            }
        );

    /// <summary>
    /// The counter, at a pace a person can read: how far along, how much is in the index, and the
    /// one message that is taking minutes named while it takes them rather than after.
    /// </summary>
    private void ShowHowFarItCame(SyncProgress progress)
    {
        var now = Environment.TickCount64;
        if (now - _lastProgressAt < ProgressRedrawMilliseconds)
            return;

        _lastProgressAt = now;

        OnUi(() =>
        {
            IndexProgressUnknown = progress.Total == 0;
            IndexProgress = progress.Total == 0 ? 0 : (double)progress.Seen / progress.Total;
            IndexStatus =
                $"{progress.Seen:N0} of {progress.Total:N0} read · {progress.Indexed:N0} indexed"
                + $" · {Gigabytes(progress.Bytes)}"
                + (
                    progress.ReadMilliseconds >= SlowMessageMilliseconds
                        ? $" · slow: {Path.GetFileName(progress.Reading)}"
                        : string.Empty
                );
        });
    }

    private static string Gigabytes(long bytes) =>
        $"{bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB";

    /// <summary>The sync runs off the UI thread; everything it says has to come back to it.</summary>
    private static void OnUi(Action update) => Dispatcher.UIThread.Post(update);

    /// <summary>
    /// Says what to do rather than only what failed: the permission is per executable, so the path
    /// of the one that just tried is the thing to add to System Settings.
    /// </summary>
    private void MailUnavailable(string reason) =>
        MailStatus = $"{reason} This program: {MailPermissionTarget}";

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

            if (run.Sync is null)
            {
                MailUnavailable($"Report written without mail: {run.Export.HtmlPath}");
                return;
            }

            MailStatus = run.Export.CsvPaths.Count == 0
                ? $"Report: {run.Export.HtmlPath}"
                : $"Report: {run.Export.HtmlPath} — {run.Export.CsvPaths.Count} YNAB CSV(s) in "
                    + Path.Combine(run.Export.Directory, EnrichedCsvWriter.DirectoryName);
            WithSystem.RevealFile(run.Export.HtmlPath);
        }
        catch (MailStoreUnavailableException exception)
        {
            MailUnavailable(exception.Message);
        }
        catch (IOException exception)
        {
            MailStatus = exception.Message;
        }
    }
}