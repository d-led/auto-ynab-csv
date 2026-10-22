using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoYnabCsv.DesktopApp.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
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

    private async void OnEditConfig(object? sender, RoutedEventArgs e)
    {
        var dialog = new ConfigWindow();
        await dialog.ShowDialog(this);

        ViewModel?.Report(dialog.ViewModel.Outcome);
    }

    private void OnShowLocation(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LocationRow location })
        {
            ViewModel?.Show(location);
        }
    }

    private async void OnIndexMail(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.IndexMailAsync();
        }
    }

    private void OnCancelIndexing(object? sender, RoutedEventArgs e) => ViewModel?.CancelIndexing();

    private async void OnExportReport(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.ExportReportAsync();
        }
    }
}