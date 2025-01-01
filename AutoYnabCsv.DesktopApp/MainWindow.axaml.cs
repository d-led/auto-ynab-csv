using AutoYnabCsv.DesktopApp.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;

namespace AutoYnabCsv.DesktopApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainWindowViewModel();
        DataContext = vm;
        AddHandler(DragDrop.DropEvent, vm.OnDrop);
    }
}