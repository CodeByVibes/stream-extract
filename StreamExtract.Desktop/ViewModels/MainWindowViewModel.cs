using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StreamExtract.Desktop.Services;
using StreamExtract.Models;
using StreamExtract.Plugins;
using StreamExtract.Services;

namespace StreamExtract.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly PluginRegistry _pluginRegistry;
    private readonly IDialogService _dialogService;
    private readonly List<ImportedFile> _importedFiles = [];
    private CancellationTokenSource? _activeCts;

    [ObservableProperty]
    private string _selectedOutputDirectory = string.Empty;

    [ObservableProperty]
    private bool _useSourceDirectory = true;

    [ObservableProperty]
    private bool _isExtracting;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private int _progressPercentage;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string? _updateDownloadUrl;

    [ObservableProperty]
    private string _logOutput = string.Empty;

    public ObservableCollection<FileNodeViewModel> FileNodes { get; } = [];

    public bool CanExtract => FileNodes.Count > 0 && !IsExtracting;

    public MainWindowViewModel(INativeToolResolver toolResolver, IDialogService dialogService)
    {
        _dialogService = dialogService;
        _pluginRegistry = new PluginRegistry();
        _pluginRegistry.Register(new MkvExtractorPlugin(toolResolver));
        _pluginRegistry.Register(new Mp4ExtractorPlugin(toolResolver));

        FileNodes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanExtract));
    }

    partial void OnIsExtractingChanged(bool value) => OnPropertyChanged(nameof(CanExtract));

    partial void OnUseSourceDirectoryChanged(bool value)
    {
        if (value && _importedFiles.Count > 0)
        {
            var first = _importedFiles[0].Info.FilePath;
            SelectedOutputDirectory = Path.GetDirectoryName(Path.GetFullPath(first)) ?? string.Empty;
        }
    }

    [RelayCommand]
    public async Task OpenFilesAsync()
    {
        var files = await _dialogService.ShowOpenFileDialogAsync(
            "Select Media Files",
            allowMultiple: true);

        if (files.Count > 0)
        {
            await ImportFilesAsync(files);
        }
    }

    [RelayCommand]
    public async Task BrowseOutputDirectoryAsync()
    {
        var folder = await _dialogService.ShowOpenFolderDialogAsync("Select Output Directory");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            SelectedOutputDirectory = folder;
            UseSourceDirectory = false;
        }
    }

    [RelayCommand]
    public async Task ExtractAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedOutputDirectory))
        {
            await _dialogService.ShowMessageAsync("Warning", "Please set an output folder before extracting.");
            return;
        }

        if (_importedFiles.Count == 0) return;

        var requests = SnapshotExtractRequests();
        if (requests.Count == 0)
        {
            AppendLog("Nothing to extract: no items selected or no valid output directory.");
            return;
        }

        IsExtracting = true;
        StatusText = "Extracting...";
        ProgressPercentage = 0;
        _activeCts = new CancellationTokenSource();
        var ct = _activeCts.Token;

        int succeeded = 0, failed = 0;
        var total = requests.Count;

        try
        {
            foreach (var (imported, request) in requests)
            {
                if (ct.IsCancellationRequested) break;

                if (!Directory.Exists(request.OutputDirectory))
                {
                    try { Directory.CreateDirectory(request.OutputDirectory); }
                    catch (Exception ex)
                    {
                        AppendLog($"Failed to create directory: {ex.Message}");
                        failed++;
                        continue;
                    }
                }

                AppendLog($"Starting: {imported.Info.FileName}");
                try
                {
                    var progress = new Progress<ExtractionProgress>(p =>
                    {
                        ProgressPercentage = p.Percentage;
                        if (!p.IsComplete && !string.IsNullOrWhiteSpace(p.StatusText))
                            StatusText = p.StatusText;
                    });

                    var outcome = await imported.Plugin.ExtractAsync(request, progress, ct);
                    if (outcome.Succeeded)
                    {
                        succeeded++;
                    }
                    else
                    {
                        failed++;
                        foreach (var failure in outcome.Failures)
                            AppendLog($"  - {failure}");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    failed++;
                    AppendLog($"Error extracting {imported.Info.FileName}: {ex.Message}");
                }
            }

            if (ct.IsCancellationRequested)
            {
                AppendLog("\nExtraction cancelled.");
                StatusText = "Cancelled";
            }
            else if (failed > 0)
            {
                AppendLog($"\nFailed: {failed} of {total} files extracted.");
                StatusText = "Extraction completed with errors";
            }
            else
            {
                AppendLog($"\nDone ({succeeded} of {total} files extracted successfully).");
                StatusText = "Done";
            }
        }
        finally
        {
            IsExtracting = false;
            ProgressPercentage = 0;
            _activeCts?.Dispose();
            _activeCts = null;
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        _activeCts?.Cancel();
    }

    [RelayCommand]
    public async Task ShowAboutAsync()
    {
        await _dialogService.ShowAboutDialogAsync();
    }

    [RelayCommand]
    public void OpenUpdateLink()
    {
        var url = UpdateDownloadUrl ?? "https://cudacoder.com";
        BrowserLauncher.TryOpen(url, out _);
    }

    public async Task ImportFilesAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        StatusText = "Importing files...";
        var pathList = filePaths.Where(File.Exists).ToList();
        if (pathList.Count == 0)
        {
            StatusText = "Ready";
            return;
        }

        FileNodes.Clear();
        _importedFiles.Clear();

        if (UseSourceDirectory && pathList.Count > 0)
        {
            SelectedOutputDirectory = Path.GetDirectoryName(Path.GetFullPath(pathList[0])) ?? string.Empty;
        }

        foreach (var path in pathList)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var plugin = _pluginRegistry.GetPlugin(path);
                if (plugin is null)
                {
                    AppendLog($"Unsupported file type: {Path.GetFileName(path)}");
                    continue;
                }

                var info = await plugin.AnalyzeFileAsync(path, ct);
                var imported = new ImportedFile(path, plugin, info);
                _importedFiles.Add(imported);

                var rootNode = CreateFileNode(imported);
                FileNodes.Add(rootNode);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                AppendLog($"Error importing {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        StatusText = _importedFiles.Count > 0
            ? $"Imported {_importedFiles.Count} file{(_importedFiles.Count == 1 ? "" : "s")}"
            : "Ready";
        OnPropertyChanged(nameof(CanExtract));
    }

    private static FileNodeViewModel CreateFileNode(ImportedFile imported)
    {
        var info = imported.Info;
        var root = new FileNodeViewModel(Path.GetFileName(info.FilePath), "video_root", imported);

        foreach (var t in info.Tracks)
        {
            var label = $"Track{t.Id + 1} [TID {t.Id}][{t.Type}][{t.Codec}][{t.TrackName}][{t.Language}]";
            if (t.Type == TrackType.Video && t.Properties.TryGetValue("PixelDimensions", out var d))
                label += $"[{d}]";
            if (t.Properties.TryGetValue("Duration", out var dur) && dur.Length > 0)
                label += $"[{dur}]";

            var icon = t.Type switch
            {
                TrackType.Video => "video",
                TrackType.Audio => "audio",
                TrackType.Subtitle => "subtitle",
                _ => "video"
            };

            root.AddChild(new FileNodeViewModel(label, icon, (int)t.Id));
        }

        if (info.Attachments.Count > 0)
            root.AddChild(new FileNodeViewModel($"Attachments: {info.Attachments.Count}", "paperclip", TreeSelectionKind.Attachments));
        if (info.Chapters.Count > 0)
            root.AddChild(new FileNodeViewModel($"Chapters: {info.Chapters[0].Name}", "clock", TreeSelectionKind.Chapters));
        if ((info.Features & ExtractorFeatures.Tags) != 0)
            root.AddChild(new FileNodeViewModel("Tags", "tags", TreeSelectionKind.Tags));
        if ((info.Features & ExtractorFeatures.CueSheets) != 0)
        {
            root.AddChild(new FileNodeViewModel("CUE sheet", "list", TreeSelectionKind.CueSheet));
            if (info.Tracks.Count > 0)
                root.AddChild(new FileNodeViewModel("Cues for selected tracks", "list", TreeSelectionKind.CuesForSelectedTracks));
        }
        if ((info.Features & ExtractorFeatures.Timestamps) != 0 && info.Tracks.Count > 0)
            root.AddChild(new FileNodeViewModel("Timestamps for selected tracks", "stopwatch", TreeSelectionKind.Timestamps));

        return root;
    }

    private List<(ImportedFile File, ExtractRequest Request)> SnapshotExtractRequests()
    {
        var outputDir = SelectedOutputDirectory.Trim();
        var result = new List<(ImportedFile, ExtractRequest)>();

        foreach (var fileNode in FileNodes)
        {
            if (fileNode.Tag is not ImportedFile imported) continue;
            var selection = FileNodeViewModel.BuildFileSelection(fileNode);
            if (selection is null) continue;
            var request = ExtractionRequestBuilder.TryBuild(imported, outputDir, selection);
            if (request is null) continue;
            result.Add((imported, request));
        }

        return result;
    }

    public async Task CheckUpdateAsync()
    {
        try
        {
            var checker = UpdateChecker.CreateCustom(
                "https://cudacoder.com/version_stream_extract.php",
                ParseCudacoderUpdate);

            var update = await checker.CheckAsync();
            if (update is null) return;

            UpdateDownloadUrl = update.DownloadUrl;
            IsUpdateAvailable = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateChecker] Update check failed: {ex.Message}");
        }
    }

    internal static (string Version, string Url) ParseCudacoderUpdate(JsonDocument? json)
    {
        var raw = json?.RootElement.GetRawText() ?? "";
        var version = Regex.Match(raw, @"[\d]+\.[\d]+\.[\d]+");
        return (version.Success ? version.Value : "", "https://cudacoder.com");
    }

    private void AppendLog(string text)
    {
        LogOutput = string.IsNullOrEmpty(LogOutput)
            ? text
            : $"{LogOutput}\n{text}";
    }
}
