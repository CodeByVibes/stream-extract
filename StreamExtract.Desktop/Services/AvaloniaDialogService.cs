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

        await ShowAndAwaitCloseAsync(msgWindow, window);
    }

    public async Task ShowAboutDialogAsync()
    {
        var dialog = new AboutDialog();
        await ShowAndAwaitCloseAsync(dialog, _mainWindowProvider());
    }

    /// <summary>
    /// Shows <paramref name="dialog"/> and completes only once it has closed. A modal
    /// <see cref="Window.ShowDialog(Window)"/> already awaits closure; without an owner we must
    /// await the <see cref="Window.Closed"/> event ourselves, because <see cref="Window.Show()"/>
    /// returns as soon as the window is shown.
    /// </summary>
    private static async Task ShowAndAwaitCloseAsync(Window dialog, Window? owner)
    {
        if (owner is not null)
        {
            await dialog.ShowDialog(owner);
            return;
        }

        var closed = AwaitCloseAsync(new WindowClosable(dialog));
        dialog.Show();
        await closed;
    }

    /// <summary>
    /// Wires a completion source to the dialog's <c>Closed</c> event. Kept separate from
    /// <see cref="ShowAndAwaitCloseAsync"/> so the await-on-close contract can be exercised without
    /// a live Avalonia platform. Subscribe before showing the dialog, because it can be closed
    /// before <see cref="Window.Show()"/> returns.
    /// </summary>
    internal static Task AwaitCloseAsync(IClosable closable)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        closable.Closed += (_, _) => closed.TrySetResult();
        return closed.Task;
    }

    /// <summary>
    /// Adapts a <see cref="Window"/> to <see cref="IClosable"/>. An interface keeps
    /// <see cref="AwaitCloseAsync"/> free of the Avalonia platform, which a <see cref="Window"/>
    /// instance requires at construction time.
    /// </summary>
    internal sealed class WindowClosable(Window window) : IClosable
    {
        private EventHandler? _closed;

        public event EventHandler? Closed
        {
            add
            {
                // Subscribe to the window at most once, however many handlers are attached.
                if (_closed is null) window.Closed += OnWindowClosed;
                _closed += value;
            }
            remove => _closed -= value;
        }

        private void OnWindowClosed(object? sender, EventArgs e) => _closed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Minimal view of something whose closure can be observed, allowing the dialog await logic to be
/// unit tested without a live Avalonia application.
/// </summary>
internal interface IClosable
{
    event EventHandler? Closed;
}
