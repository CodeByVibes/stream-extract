using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using StreamExtract.Desktop.ViewModels;
using StreamExtract.Services;

namespace StreamExtract.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionString = version is null ? "1.0" : $"{version.Major}.{version.Minor}";
        Title = $"StreamExtract v{versionString}";

        AddHandler(DragDrop.DragOverEvent, DragOver, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, Drop, handledEventsToo: true);
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is MainWindowViewModel vm)
        {
            var paths = GetDroppedPaths(e);
            if (paths.Count > 0)
                await vm.ImportFilesAsync(paths);
        }
    }

    private static IReadOnlyList<string> GetDroppedPaths(DragEventArgs e)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddPath(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
                paths.Add(path);
        }

        // 1. Try IStorageItem files (standard Avalonia file abstraction)
        var files = e.DataTransfer.TryGetFiles();
        if (files != null)
        {
            foreach (var file in files)
            {
                var localPath = file.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(localPath))
                {
                    AddPath(localPath);
                }
                else if (file.Path is { IsFile: true } uri)
                {
                    AddPath(uri.LocalPath);
                }
                else if (file.Path is not null)
                {
                    foreach (var parsed in FileDropParser.ParseUriList(file.Path.ToString()))
                        AddPath(parsed);
                }
            }
        }

        // 2. Try text/uri-list platform format (standard for Linux file managers on Wayland/X11)
        try
        {
            var uriFormat = DataFormat.CreateStringPlatformFormat("text/uri-list");
            var uriListText = e.DataTransfer.TryGetValue(uriFormat);
            if (!string.IsNullOrWhiteSpace(uriListText))
            {
                foreach (var p in FileDropParser.ParseUriList(uriListText))
                    AddPath(p);
            }
        }
        catch { }

        try
        {
            var uriBytesFormat = DataFormat.CreateBytesPlatformFormat("text/uri-list");
            var uriBytes = e.DataTransfer.TryGetValue(uriBytesFormat);
            if (uriBytes != null && uriBytes.Length > 0)
            {
                var decoded = System.Text.Encoding.UTF8.GetString(uriBytes);
                foreach (var p in FileDropParser.ParseUriList(decoded))
                    AddPath(p);
            }
        }
        catch { }

        // 3. Try standard plain text (DataFormat.Text)
        var text = e.DataTransfer.TryGetText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (var p in FileDropParser.ParseUriList(text))
                AddPath(p);
        }

        // 4. Fallback: inspect any remaining format containing "uri-list", "file", or "copied-files"
        foreach (var format in e.DataTransfer.Formats)
        {
            if (format.Identifier.Contains("uri-list", StringComparison.OrdinalIgnoreCase) ||
                format.Identifier.Contains("file", StringComparison.OrdinalIgnoreCase) ||
                format.Identifier.Contains("copied-files", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (format is DataFormat<string> sf)
                    {
                        var val = e.DataTransfer.TryGetValue(sf);
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            foreach (var p in FileDropParser.ParseUriList(val))
                                AddPath(p);
                        }
                    }
                    else if (format is DataFormat<byte[]> bf)
                    {
                        var bytes = e.DataTransfer.TryGetValue(bf);
                        if (bytes != null && bytes.Length > 0)
                        {
                            var decoded = System.Text.Encoding.UTF8.GetString(bytes);
                            foreach (var p in FileDropParser.ParseUriList(decoded))
                                AddPath(p);
                        }
                    }
                }
                catch { }
            }
        }

        return paths;
    }
}
