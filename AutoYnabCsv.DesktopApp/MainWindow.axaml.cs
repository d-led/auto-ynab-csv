using System.IO;
using System.Linq;
using AutoYnabCsv.DesktopApp.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using ReactiveUI;

namespace AutoYnabCsv.DesktopApp;

public partial class MainWindow : Window, IViewFor<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel();
        DataContext = ViewModel;
        AddHandler(DragDrop.DropEvent, ((_, args) =>
        {
            var files = (args.Data.GetFiles() ?? [])
                .Where(file => file.Path.IsFile)
                .Select(file => file.Path.LocalPath)
                .Where(file => Path.GetExtension(file).ToLowerInvariant() == ".csv")
                .ToList();
            
            if (files.Count == 0)
                return;
            
            ViewModel.ProcessDropOf(files);
        }));
    }

    object? IViewFor.ViewModel
    {
        get => ViewModel;
        set => ViewModel = (MainWindowViewModel?)value;
    }

    public MainWindowViewModel? ViewModel { get; set; }
}