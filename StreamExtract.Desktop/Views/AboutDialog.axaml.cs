using Avalonia.Controls;
using Avalonia.Interactivity;
using StreamExtract.Desktop.ViewModels;

namespace StreamExtract.Desktop.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
        DataContext = new AboutViewModel();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
