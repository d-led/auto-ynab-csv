using AutoYnabCsv.DesktopApp.ViewModels;
using Avalonia.Controls;

namespace AutoYnabCsv.DesktopApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}