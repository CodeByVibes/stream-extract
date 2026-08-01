using StreamExtract.Models;
using StreamExtract.Plugins;
using StreamExtract.Services;
using System.Reflection;

namespace StreamExtract;

public partial class Form1 : Form
{
    private enum SelectionKind { Attachments, Chapters, Tags, CueSheet, CuesForSelectedTracks, Timestamps }

    private bool _isClosing;
    private readonly PluginRegistry _pluginRegistry = new();
    private List<string> _pendingPaths = [];
    private readonly List<ImportedFile> _importedFiles = [];
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _perOpCts;
    private Task? _activeOperation;
    private string? _updateDownloadUrl;

    private TreeView tvFiles = null!;
    private ImageList ilIcons = null!;
    private RichTextBox rtbDebug = null!;
    private Label lblBrowseOutputDirectory = null!;
    private TextBox txtBrowseOutputDirectory = null!;
    private CheckBox cbUseSourceDirectory = null!;
    private Button btnBrowseOutputDirectory = null!;
    private Label lblInputFiles = null!;
    private Button btnOpenFiles = null!;
    private Button btnExtract = null!;
    private SmoothProgressBar pbProgress = null!;
    private StatusStrip statusStrip1 = null!;
    private Button btnAbout = null!;
    private Button btnNewVersion = null!;

    public Form1()
    {
        InitializeComponent();
        using var ico = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("StreamExtract.Resources.AppIcon.ico")!;
        Icon = new Icon(ico);

        var v = Assembly.GetExecutingAssembly().GetName().Version!;
        Text = $"StreamExtract v{v.Major}.{v.Minor}";

        SetupIcons();
        SetupPlugins();
        WireEvents();
    }

    private void SetupIcons()
    {
        ilIcons.Images.Add(LoadPng("1px"));
        ilIcons.Images.Add(LoadPng("video_root"));
        ilIcons.Images.Add(LoadPng("video"));
        ilIcons.Images.Add(LoadPng("audio"));
        ilIcons.Images.Add(LoadPng("subtitle"));
        ilIcons.Images.Add(LoadPng("clock"));
        ilIcons.Images.Add(LoadPng("paperclip"));
        ilIcons.Images.Add(LoadPng("tags"));
        ilIcons.Images.Add(LoadPng("stopwatch"));
        ilIcons.Images.Add(LoadPng("list"));
    }

    private static Image LoadPng(string name)
    {
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"StreamExtract.Resources.{name}.png")
            ?? throw new InvalidOperationException($"Embedded resource 'StreamExtract.Resources.{name}.png' is missing.");
        using (stream)
        using (var img = Image.FromStream(stream))
        {
            return new Bitmap(img);
        }
    }

    private void SetupPlugins()
    {
        _pluginRegistry.Register(new MkvExtractorPlugin(Program.ToolPath));
        _pluginRegistry.Register(new Mp4ExtractorPlugin(Program.ToolPath));
    }

    private void WireEvents()
    {
        tvFiles.DragEnter += TvFiles_DragEnter!;
        tvFiles.DragDrop += TvFiles_DragDrop!;
        tvFiles.AfterCheck += TvFiles_AfterCheck!;
        btnOpenFiles.Click += BtnOpenFiles_Click!;
        btnExtract.Click += BtnExtract_Click!;
        btnBrowseOutputDirectory.Click += BtnBrowseOutputDirectory_Click!;
        cbUseSourceDirectory.CheckedChanged += CbUseSourceDirectory_CheckedChanged!;
        btnAbout.Click += BtnAbout_Click!;
        btnNewVersion.Click += BtnNewVersion_Click!;
        FormClosing += Form1_FormClosing!;
        Shown += async (_, _) => await CheckUpdateAsync();
    }

    private async Task CheckUpdateAsync()
    {
        // Switch to UpdateChecker.CreateGitHub("owner", "repo") when publishing on GitHub
        var checker = UpdateChecker.CreateCustom(
            "https://cudacoder.com/version_stream_extract.php",
            json =>
            {
                var raw = json!.RootElement.GetString() ?? "";
                var version = System.Text.RegularExpressions.Regex.Match(raw, @"[\d]+\.[\d]+\.[\d]+");
                return (version.Success ? version.Value : raw, "https://cudacoder.com");
            });

        var update = await checker.CheckAsync();
        if (update is null) return;

        _updateDownloadUrl = update.DownloadUrl;
        if (InvokeRequired) { BeginInvoke(() => btnNewVersion.Visible = true); return; }
        btnNewVersion.Visible = true;
    }

    private void TvFiles_DragEnter(object? sender, DragEventArgs e)
        => e.Effect = e.Data!.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.All : DragDropEffects.None;

    private async void TvFiles_DragDrop(object? sender, DragEventArgs e)
    {
        _pendingPaths = ((string[])e.Data!.GetData(DataFormats.FileDrop)!).ToList();
        await RunExclusiveAsync(StartImportAsync);
    }

    private async void BtnOpenFiles_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Media files (*.mkv,*.mka,*.mp4,*.m4v,*.m4a,*.m4b)|*.mkv;*.mka;*.mp4;*.m4v;*.m4a;*.m4b",
            Multiselect = true
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _pendingPaths = dlg.FileNames.ToList();
        await RunExclusiveAsync(StartImportAsync);
    }

    private async Task StartImportAsync(CancellationToken ct)
    {
        tvFiles.Nodes.Clear();
        _importedFiles.Clear();
        btnExtract.Enabled = false;

        if (_pendingPaths.Count > 0 && cbUseSourceDirectory.Checked)
            txtBrowseOutputDirectory.Text = Path.GetDirectoryName(Path.GetFullPath(_pendingPaths[0])) ?? "";

        foreach (var fp in _pendingPaths)
        {
            if (_isClosing || ct.IsCancellationRequested) break;
            try
            {
                var plugin = _pluginRegistry.GetPlugin(fp);
                if (plugin is null) continue;
                var info = await plugin.AnalyzeFileAsync(fp, ct);
                var imported = new ImportedFile(fp, plugin, info);
                _importedFiles.Add(imported);
                BeginInvoke(() => AddFileToTree(imported));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { DebugLog($"Error importing {Path.GetFileName(fp)}: {ex.Message}"); }
        }

        btnExtract.Enabled = _importedFiles.Count > 0;
    }

    private void AddFileToTree(ImportedFile imported)
    {
        if (_isClosing || IsDisposed) return;

        var info = imported.Info;
        var root = new TreeNode(Path.GetFileName(info.FilePath), 1, 1) { Tag = imported };

        foreach (var t in info.Tracks)
        {
            var label = $"Track{t.Id + 1} [TID {t.Id}][{t.Type}][{t.Codec}][{t.TrackName}][{t.Language}]";
            if (t.Type == TrackType.Video && t.Properties.TryGetValue("PixelDimensions", out var d))
                label += $"[{d}]";
            if (t.Properties.TryGetValue("Duration", out var dur) && dur.Length > 0)
                label += $"[{dur}]";
            int img = t.Type switch { TrackType.Video => 2, TrackType.Audio => 3, TrackType.Subtitle => 4, _ => 0 };
            root.Nodes.Add(new TreeNode(label, img, img) { Tag = (int)t.Id });
        }

        if (info.Attachments.Count > 0)
            root.Nodes.Add(new TreeNode($"Attachments: {info.Attachments.Count}", 6, 6) { Tag = SelectionKind.Attachments });
        if (info.Chapters.Count > 0)
            root.Nodes.Add(new TreeNode($"Chapters: {info.Chapters[0].Name}", 5, 5) { Tag = SelectionKind.Chapters });
        if ((info.Features & ExtractorFeatures.Tags) != 0)
            root.Nodes.Add(new TreeNode("Tags", 7, 7) { Tag = SelectionKind.Tags });
        if ((info.Features & ExtractorFeatures.CueSheets) != 0)
        {
            root.Nodes.Add(new TreeNode("CUE sheet", 9, 9) { Tag = SelectionKind.CueSheet });
            root.Nodes.Add(new TreeNode("Cues for selected tracks", 9, 9) { Tag = SelectionKind.CuesForSelectedTracks });
        }
        if ((info.Features & ExtractorFeatures.Timestamps) != 0)
            root.Nodes.Add(new TreeNode("Timestamps for selected tracks", 8, 8) { Tag = SelectionKind.Timestamps });

        root.ExpandAll();
        tvFiles.Nodes.Add(root);
    }

    private void CbUseSourceDirectory_CheckedChanged(object? sender, EventArgs e)
    {
        txtBrowseOutputDirectory.Enabled = !cbUseSourceDirectory.Checked;
        btnBrowseOutputDirectory.Enabled = !cbUseSourceDirectory.Checked;
    }

    private void BtnBrowseOutputDirectory_Click(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog();
        if (dlg.ShowDialog() == DialogResult.OK)
            txtBrowseOutputDirectory.Text = dlg.SelectedPath;
    }

    private async void BtnExtract_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtBrowseOutputDirectory.Text))
        { MessageBox.Show("You must set an output folder!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (_importedFiles.Count == 0) return;

        rtbDebug.Clear();
        btnExtract.Enabled = false;
        await RunExclusiveAsync(ExportFilesAsync);
    }

    private async Task ExportFilesAsync(CancellationToken ct)
    {
        var requests = SnapshotExtractRequests();
        if (requests.Count == 0)
        {
            btnExtract.Enabled = true;
            pbProgress.Value = 0;
            DebugLog("Nothing to extract: no items selected or no valid output directory.");
            return;
        }

        int succeeded = 0, failed = 0;
        var total = requests.Count;

        foreach (var (imported, request) in requests)
        {
            if (_isClosing || ct.IsCancellationRequested) break;

            if (!Directory.Exists(request.OutputDirectory))
            {
                try { Directory.CreateDirectory(request.OutputDirectory); }
                catch (Exception ex) { DebugLog($"Failed to create directory: {ex.Message}"); failed++; continue; }
            }

            DebugLog($"Starting: {imported.Info.FileName}");
            try
            {
                var progress = new Progress<ExtractionProgress>(UpdateProgressUi);
                await imported.Plugin.ExtractAsync(request, progress, ct);
                succeeded++;
            }
            catch (OperationCanceledException)
            {
                DebugLog("Cancelled!");
                break;
            }
            catch (Exception ex)
            {
                failed++;
                DebugLog($"Error: {ex.Message}");
            }
        }

        if (_isClosing) return;

        btnExtract.Enabled = true;
        pbProgress.Value = 0;

        if (ct.IsCancellationRequested)
            DebugLog("\r\nCancelled.");
        else if (failed > 0)
            DebugLog($"\r\nFailed: {failed} of {total} files extracted.");
        else
            DebugLog($"\r\nDone ({succeeded} of {total} files extracted)");
    }

    private static FileSelection? BuildFileSelection(TreeNode fileNode)
    {
        var trackIds = new HashSet<int>();
        bool attachments = false, tags = false, cuesheet = false, timestamps = false;
        var chapterIds = new HashSet<int>();

        foreach (TreeNode child in fileNode.Nodes)
        {
            if (!child.Checked) continue;
            switch (child.Tag)
            {
                case int trackId:
                    trackIds.Add(trackId);
                    break;
                case SelectionKind.Attachments:
                    attachments = true;
                    break;
                case SelectionKind.Chapters:
                    chapterIds.Add(0);
                    break;
                case SelectionKind.Tags:
                    tags = true;
                    break;
                case SelectionKind.CueSheet:
                    cuesheet = true;
                    break;
                case SelectionKind.Timestamps:
                    timestamps = true;
                    break;
                case SelectionKind.CuesForSelectedTracks:
                    break;
            }
        }

        if (trackIds.Count == 0 && !attachments && chapterIds.Count == 0 && !tags && !cuesheet && !timestamps)
            return null;

        return new FileSelection(trackIds, attachments, chapterIds, tags, cuesheet, timestamps);
    }

    private List<(ImportedFile File, ExtractRequest Request)> SnapshotExtractRequests()
    {
        var outputDir = txtBrowseOutputDirectory.Text.Trim();
        var result = new List<(ImportedFile, ExtractRequest)>();

        foreach (TreeNode fileNode in tvFiles.Nodes)
        {
            if (!AnyChildChecked(fileNode)) continue;
            if (fileNode.Tag is not ImportedFile imported) continue;
            var selection = BuildFileSelection(fileNode);
            if (selection is null) continue;
            var request = ExtractionRequestBuilder.TryBuild(imported, outputDir, selection);
            if (request is null) continue;
            result.Add((imported, request));
        }

        return result;
    }

    private static bool AnyChildChecked(TreeNode n)
    {
        foreach (TreeNode c in n.Nodes) if (c.Checked) return true;
        return false;
    }

    private void TvFiles_AfterCheck(object? sender, TreeViewEventArgs e)
    {
        if (e.Action == TreeViewAction.Unknown) return;
        foreach (TreeNode child in e.Node!.Nodes)
            child.Checked = e.Node.Checked;
    }

    private void DebugLog(string text)
    {
        if (_isClosing) return;
        if (InvokeRequired) { BeginInvoke(() => AppendDebug(text)); return; }
        AppendDebug(text);
    }

    private void AppendDebug(string text)
    {
        if (_isClosing || IsDisposed) return;
        rtbDebug.AppendText(text + Environment.NewLine);
        rtbDebug.ScrollToCaret();
    }

    private void DebugLogProgress(string text)
    {
        if (_isClosing || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => DebugLogProgress(text)); return; }

        var fullText = rtbDebug.Text;
        if (fullText.Length < 2) { AppendDebug(text); return; }

        var lastNewline = fullText.LastIndexOf('\n', fullText.Length - 2);
        var lastLine = lastNewline >= 0 ? fullText[(lastNewline + 1)..] : fullText;
        if (lastLine.StartsWith("Extracting"))
        {
            var start = lastNewline >= 0 ? lastNewline + 1 : 0;
            rtbDebug.Select(start, lastLine.Length);
            rtbDebug.SelectedText = text;
        }
        else
        {
            rtbDebug.AppendText(text + Environment.NewLine);
        }
        rtbDebug.SelectionStart = rtbDebug.TextLength;
        rtbDebug.ScrollToCaret();
    }

    private void UpdateProgressUi(ExtractionProgress p)
    {
        if (_isClosing || IsDisposed) return;
        pbProgress.Value = p.Percentage;
        if (!p.IsComplete) DebugLogProgress(p.StatusText);
    }

    private async Task RunExclusiveAsync(Func<CancellationToken, Task> op)
    {
        if (_activeOperation != null) return;
        _perOpCts?.Cancel();
        _perOpCts?.Dispose();
        _perOpCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var ct = _perOpCts.Token;
        try
        {
            _activeOperation = op(ct);
            await _activeOperation;
        }
        finally { _activeOperation = null; }
    }

    private void BtnAbout_Click(object? sender, EventArgs e)
    {
        using var about = new AboutDialog();
        about.ShowDialog();
    }

    private void BtnNewVersion_Click(object? sender, EventArgs e)
    {
        var url = _updateDownloadUrl ?? "https://cudacoder.com";
        if (!BrowserLauncher.TryOpen(url, out var error))
            MessageBox.Show(error, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _isClosing = true;
        _lifetimeCts.Cancel();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _lifetimeCts.Dispose();
        base.OnFormClosed(e);
    }
}
