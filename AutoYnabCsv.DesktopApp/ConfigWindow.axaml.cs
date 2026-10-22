using AutoYnabCsv.DesktopApp.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AutoYnabCsv.DesktopApp;

public partial class ConfigWindow : Window
{
    public ConfigWindow()
    {
        InitializeComponent();
        ViewModel = new ConfigViewModel();
        DataContext = ViewModel;
    }

    public ConfigViewModel ViewModel { get; }

    private void OnSave(object? sender, RoutedEventArgs e) => ViewModel.Save();

    private void OnRevert(object? sender, RoutedEventArgs e) => ViewModel.Revert();

    private void OnShowFolder(object? sender, RoutedEventArgs e) => ViewModel.Reveal();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
