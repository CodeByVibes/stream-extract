using Avalonia.Controls;
using Avalonia.Platform.Storage;
using StreamExtract.Desktop.Views;

namespace StreamExtract.Desktop.Services;

public class AvaloniaDialogService : IDialogService
{
    private readonly Func<Window?> _mainWindowProvider;

    public AvaloniaDialogService(Func<Window?> mainWindowProvider)
    {
        _mainWindowProvider = mainWindowProvider;
    }

    public async Task<IReadOnlyList<string>> ShowOpenFileDialogAsync(string title, bool allowMultiple)
    {
        var window = _mainWindowProvider();
        if (window is null) return [];

        var topLevel = TopLevel.GetTopLevel(window);
        if (topLevel?.StorageProvider is null) return [];

        var mediaFilter = new FilePickerFileType("Media files (*.mkv, *.mp4, ...)")
        {
            Patterns = ["*.mkv", "*.mka", "*.mp4", "*.m4v", "*.m4a", "*.m4b"]
        };
        var allFilter = new FilePickerFileType("All files (*.*)")
        {
            Patterns = ["*.*"]
        };

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = [mediaFilter, allFilter]
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
        return files.Select(f => f.Path.LocalPath).ToList();
    }

    public async Task<string?> ShowOpenFolderDialogAsync(string title)
    {
        var window = _mainWindowProvider();
        if (window is null) return null;

        var topLevel = TopLevel.GetTopLevel(window);
        if (topLevel?.StorageProvider is null) return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var window = _mainWindowProvider();
        var msgWindow = new Window
        {
            Title = title,
            Width = 360,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Grid
            {
                Margin = new Avalonia.Thickness(20),
                RowDefinitions = new RowDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    },
                    new Button
                    {
                        Content = "OK",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        [Grid.RowProperty] = 1,
                        Width = 80
                    }
                }
            }
        };

        var okButton = (Button)((Grid)msgWindow.Content).Children[1];
        okButton.Click += (_, _) => msgWindow.Close();

        if (window is not null)
            await msgWindow.ShowDialog(window);
        else
            msgWindow.Show();
    }

    public async Task ShowAboutDialogAsync()
    {
        var window = _mainWindowProvider();
        var dialog = new AboutDialog();
        if (window is not null)
            await dialog.ShowDialog(window);
        else
            dialog.Show();
    }
}
