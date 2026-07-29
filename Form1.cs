using StreamExtract.Models;
using StreamExtract.Plugins;
using StreamExtract.Services;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace StreamExtract;

public partial class Form1 : Form
{
    private bool _isClosing;
    private readonly PluginRegistry _pluginRegistry = new();
    private readonly List<MediaFileInfo> _fileInfos = [];
    private List<string> _filePaths = [];
    private MediaFileInfo? _currentFileInfo;
    private CancellationTokenSource? _cts;
    private string _debugText = "";
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
        using var s = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"StreamExtract.Resources.{name}.png")!;
        return Image.FromStream(s);
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

    private void TvFiles_DragDrop(object? sender, DragEventArgs e)
    {
        _filePaths = ((string[])e.Data!.GetData(DataFormats.FileDrop)!).ToList();
        StartImport();
    }

    private void BtnOpenFiles_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Media files (*.mkv,*.mka,*.mp4,*.m4v,*.m4a,*.m4b)|*.mkv;*.mka;*.mp4;*.m4v;*.m4a;*.m4b",
            Multiselect = true
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _filePaths = dlg.FileNames.ToList();
        StartImport();
    }

    private void StartImport()
    {
        tvFiles.Nodes.Clear();
        _fileInfos.Clear();
        _currentFileInfo = null;

        if (_filePaths.Count > 0 && cbUseSourceDirectory.Checked)
            txtBrowseOutputDirectory.Text = Path.GetDirectoryName(Path.GetFullPath(_filePaths[0])) ?? "";

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        new Thread(() =>
        {
            foreach (var fp in _filePaths)
            {
                if (_isClosing || ct.IsCancellationRequested) break;
                try
                {
                    var plugin = _pluginRegistry.GetPlugin(fp);
                    if (plugin is null) continue;
                    var info = plugin.AnalyzeFileAsync(fp, ct).GetAwaiter().GetResult();
                    _fileInfos.Add(info);
                    _currentFileInfo = info;
                    AddFileToTree();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { DebugLog($"Error importing {Path.GetFileName(fp)}: {ex.Message}"); }
            }
            if (!_isClosing && !ct.IsCancellationRequested && tvFiles.Nodes.Count > 0)
                Invoke(() => btnExtract.Enabled = true);
        }).Start();
    }

    private void AddFileToTree()
    {
        if (InvokeRequired) { BeginInvoke(AddFileToTree); return; }
        if (_isClosing || _currentFileInfo is null) return;

        var info = _currentFileInfo;
        var root = new TreeNode(Path.GetFileName(info.FilePath), 1, 1);

        foreach (var t in info.Tracks)
        {
            var label = $"Track{t.Id + 1} [TID {t.Id}][{t.Type}][{t.Codec}][{t.TrackName}][{t.Language}]";
            if (t.Type == TrackType.Video && t.Properties.TryGetValue("PixelDimensions", out var d))
                label += $"[{d}]";
            if (t.Properties.TryGetValue("Duration", out var dur) && dur.Length > 0)
                label += $"[{dur}]";
            int img = t.Type switch { TrackType.Video => 2, TrackType.Audio => 3, TrackType.Subtitle => 4, _ => 0 };
            root.Nodes.Add(new TreeNode(label, img, img));
        }

        if (info.Attachments.Count > 0)
            root.Nodes.Add(new TreeNode($"Attachments: {info.Attachments.Count}", 6, 6));
        if (info.Chapters.Count > 0)
            root.Nodes.Add(new TreeNode($"Chapters: {info.Chapters[0].Name}", 5, 5));
        if ((info.Features & ExtractorFeatures.Tags) != 0)
            root.Nodes.Add(new TreeNode("Tags", 7, 7));
        if ((info.Features & ExtractorFeatures.CueSheets) != 0)
        {
            root.Nodes.Add(new TreeNode("CUE sheet", 9, 9));
            root.Nodes.Add(new TreeNode("Cues for selected tracks", 9, 9));
        }
        if ((info.Features & ExtractorFeatures.Timestamps) != 0)
            root.Nodes.Add(new TreeNode("Timestamps for selected tracks", 8, 8));

        root.ExpandAll();
        tvFiles.Nodes.Add(root);
        btnExtract.Enabled = true;
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

    private void BtnExtract_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtBrowseOutputDirectory.Text))
        { MessageBox.Show("You must set an output folder!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (!Directory.Exists(txtBrowseOutputDirectory.Text))
        { MessageBox.Show("Output folder does not exist!", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (_filePaths.Count == 0) return;

        rtbDebug.Clear();
        btnExtract.Enabled = false;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        new Thread(() => ExportFiles(ct)).Start();
    }

    private void ExportFiles(CancellationToken ct)
    {
        for (int i = 0; i < tvFiles.Nodes.Count && i < _fileInfos.Count; i++)
        {
            if (ct.IsCancellationRequested) break;
            var fileNode = tvFiles.Nodes[i];
            if (!AnyChildChecked(fileNode)) continue;

            var info = _fileInfos[i];
            var filePath = _filePaths.Count > i ? _filePaths[i] : info.FilePath;
            var fn = Path.GetFileNameWithoutExtension(filePath);
            var outputDir = txtBrowseOutputDirectory.Invoke(() => txtBrowseOutputDirectory.Text);

            var req = BuildExtractRequest(info, fileNode, filePath, outputDir!);
            if (req is null) continue;

            DebugLog($"Starting: {info.FileName}");
            try
            {
                var progress = new Progress<ExtractionProgress>(p =>
                {
                    Invoke(() => { pbProgress.Value = p.Percentage; DebugLog(p.StatusText); });
                });
                var plugin = _pluginRegistry.GetPlugin(filePath)!;
                plugin.ExtractAsync(req, progress, ct).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { DebugLog("Cancelled!"); break; }
            catch (Exception ex) { DebugLog($"Error: {ex.Message}"); }
        }

        if (!ct.IsCancellationRequested)
        {
            Invoke(() => { btnExtract.Enabled = true; pbProgress.Value = 0; });
            DebugLog("Done!");
        }
    }

    private ExtractRequest? BuildExtractRequest(MediaFileInfo info, TreeNode fileNode, string fp, string outputDir)
    {
        var trackIds = new HashSet<int>();
        bool attachments = false, tags = false, cuesheet = false, timestamps = false;
        var chapterIds = new HashSet<int>();

        foreach (TreeNode child in fileNode.Nodes)
        {
            if (!child.Checked) continue;
            var text = child.Text;

            if (text.StartsWith("Track", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < info.Tracks.Count; i++)
                    if (fileNode.Nodes[i] == child) { trackIds.Add(info.Tracks[i].Id); break; }
            }
            else if (text.StartsWith("Attachments", StringComparison.OrdinalIgnoreCase))
                attachments = true;
            else if (text.StartsWith("Chapters", StringComparison.OrdinalIgnoreCase))
                chapterIds.Add(0);
            else if (text == "Tags")
                tags = true;
            else if (text == "CUE sheet")
                cuesheet = true;
            else if (text.StartsWith("Timestamps", StringComparison.OrdinalIgnoreCase))
                timestamps = true;
        }

        if (trackIds.Count == 0 && !attachments && chapterIds.Count == 0 && !tags && !cuesheet && !timestamps)
            return null;

        return new ExtractRequest(info, outputDir, trackIds, chapterIds, attachments, tags, cuesheet, timestamps);
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
        _debugText = text + Environment.NewLine;
        if (InvokeRequired) { BeginInvoke(AddDebugText); return; }
        rtbDebug.AppendText(text + Environment.NewLine);
        rtbDebug.ScrollToCaret();
    }

    private void AddDebugText()
    {
        if (_isClosing) return;
        rtbDebug.AppendText(_debugText);
        rtbDebug.ScrollToCaret();
    }

    private void BtnAbout_Click(object? sender, EventArgs e)
    {
        using var about = new AboutDialog();
        about.ShowDialog();
    }

    private void BtnNewVersion_Click(object? sender, EventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_updateDownloadUrl ?? "https://cudacoder.com") { UseShellExecute = true }); }
        catch { }
    }

    private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _isClosing = true;
        _cts?.Cancel();
    }
}
