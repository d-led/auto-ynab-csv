using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoYnabCsv.DesktopApp.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using ReactiveUI;

namespace AutoYnabCsv.DesktopApp;

public partial class MainWindow : Window, IViewFor<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel();
        DataContext = ViewModel;
        AddHandler(DragDrop.DropEvent, async (_, args) =>
        {
            if (args is not DragEventArgs dragEventArgs)
                return;

            var files = (dragEventArgs.DataTransfer.TryGetFiles() ?? [])
                .Where(file => file.Path.IsFile)
                .Select(file => file.Path.LocalPath)
                .Where(file => Path.GetExtension(file).ToLowerInvariant() == ".csv")
                .ToList();

            if (files.Count == 0)
                return;

            if (ViewModel is { } viewModel)
            {
                await viewModel.ProcessDropOf(files);
            }
        });
    }

    object? IViewFor.ViewModel
    {
        get => ViewModel;
        set => ViewModel = (MainWindowViewModel?)value;
    }

    public MainWindowViewModel? ViewModel { get; set; }

    private void OnShowIndex(object? sender, RoutedEventArgs e) => ViewModel?.RevealIndex();

    private void OnShowConfig(object? sender, RoutedEventArgs e) => ViewModel?.RevealConfig();

    private void OnShowExport(object? sender, RoutedEventArgs e) => ViewModel?.RevealExport();

    private async void OnIndexMail(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.IndexMailAsync();
        }
    }

    private async void OnExportReport(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.ExportReportAsync();
        }
    }

    private async void OnCopyPath(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LocationRow row })
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(row.Path);
        }
    }
}