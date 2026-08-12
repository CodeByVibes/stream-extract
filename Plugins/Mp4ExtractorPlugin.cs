using System.Text.RegularExpressions;
using StreamExtract.Models;
using StreamExtract.Services;

namespace StreamExtract.Plugins;

public sealed partial class Mp4ExtractorPlugin(string toolPath, IProcessRunner? runner = null) : IExtractorPlugin
{
    private readonly IProcessRunner _runner = runner ?? new ProcessRunner(toolPath);

    private static readonly HashSet<string> _exts = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".m4a", ".m4b" };

    public string Name => "MP4 Extractor";
    public IReadOnlySet<string> SupportedExtensions => _exts;
    public ExtractorFeatures SupportedFeatures => ExtractorFeatures.Tracks | ExtractorFeatures.Chapters;

    [GeneratedRegex(@"# Track (\d+) Info - ID (\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TrackRe();
    [GeneratedRegex(@"Media Type:\s*(\w+):(\w+)")]
    private static partial Regex MediaTypeRe();
    [GeneratedRegex(@"width=(\d+)\s+height=(\d+)")]
    private static partial Regex DimsRe();
    [GeneratedRegex(@"(\d+)\s+Channel")]
    private static partial Regex ChannelsRe();
    [GeneratedRegex(@"SampleRate\s+(\d+)")]
    private static partial Regex SampleRateRe();
    // Real mp4box output: '#1 - 00:00:00.000 - "Intro"' (older GPAC versions prefix with 'Chapter').
    [GeneratedRegex(@"(?:Chapter\s+)?#(\d+)\s*-\s*(?:\S+)\s*-\s*""(.+?)""", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterRe();

    private const int MaxTrackInfoLines = 500;

    public async Task<MediaFileInfo> AnalyzeFileAsync(string filePath, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("mp4box.exe", new[] { "-info", filePath }, ct);
        // mp4box (GPAC) writes all console output, including "-info" listings, to stderr.
        var output = result.StandardError;

        var tracks = new List<TrackInfo>();
        using var reader = new StringReader(output);

        string? currentLine = await reader.ReadLineAsync();
        while (currentLine != null)
        {
            var trackMatch = TrackRe().Match(currentLine);
            if (!trackMatch.Success)
            {
                currentLine = await reader.ReadLineAsync();
                continue;
            }

            if (!int.TryParse(trackMatch.Groups[2].Value, out var trackId))
            {
                currentLine = await reader.ReadLineAsync();
                continue;
            }

            var codec = "unknown";
            var mediaType = "";
            var props = new Dictionary<string, string>();

            int linesRead = 0;
            currentLine = await reader.ReadLineAsync();
            while (currentLine != null && linesRead < MaxTrackInfoLines)
            {
                linesRead++;

                // Stop at the next track header or at the blank line that separates track blocks.
                if (TrackRe().IsMatch(currentLine) || currentLine.Length == 0) break;

                // Media Type: vide:avc1
                var mt = MediaTypeRe().Match(currentLine);
                if (mt.Success)
                {
                    mediaType = mt.Groups[1].Value.ToLowerInvariant();
                    codec = mt.Groups[2].Value;
                }

                // width=640 height=480
                var dims = DimsRe().Match(currentLine);
                if (dims.Success && string.IsNullOrEmpty(props.GetValueOrDefault("PixelDimensions")))
                    props["PixelDimensions"] = $"{dims.Groups[1].Value}x{dims.Groups[2].Value}";

                // 2 Channel(s)
                var ch = ChannelsRe().Match(currentLine);
                if (ch.Success && int.TryParse(ch.Groups[1].Value, out _))
                    props["Channels"] = ch.Groups[1].Value;

                // SampleRate 48000
                var sr = SampleRateRe().Match(currentLine);
                if (sr.Success && int.TryParse(sr.Groups[1].Value, out _))
                    props["SampleRate"] = sr.Groups[1].Value;

                currentLine = await reader.ReadLineAsync();
            }

            if (codec != "unknown") props["CodecId"] = codec;

            var type = mediaType switch
            {
                "vide" => TrackType.Video,
                "soun" => TrackType.Audio,
                "text" or "sbtl" or "subt" => TrackType.Subtitle,
                _ => TrackType.Other
            };

            tracks.Add(new TrackInfo(trackId, type, codec, $"Track {trackId}", "und", props));
        }

        var chapters = new List<ChapterInfo>();
        var ci = 0;
        foreach (Match m in ChapterRe().Matches(output))
            chapters.Add(new ChapterInfo(ci++, m.Groups[2].Value, ""));

        return new MediaFileInfo(filePath, Path.GetFileName(filePath), SupportedFeatures, tracks, chapters, [], []);
    }

    public async Task<ExtractOutcome> ExtractAsync(ExtractRequest req, IProgress<ExtractionProgress> progress, CancellationToken ct = default)
    {
        var fn = Path.GetFileNameWithoutExtension(req.Source.FilePath);
        var total = req.SelectedTrackIds.Count + (req.SelectedChapterIds.Count > 0 ? 1 : 0);
        int done = 0;
        var failures = new List<string>();

        foreach (var tid in req.SelectedTrackIds)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            progress.Report(new ExtractionProgress(req.Source.FileName, $"Track {tid}",
                total > 0 ? done * 100 / total : 0, $"Extracting track {tid}...", false));
            try
            {
                await _runner.RunAsync("mp4box.exe",
                    new[] { "-raw", $"{tid}:output={BuildRawOutputName(req, tid)}", req.Source.FilePath },
                    ct, req.OutputDirectory);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"track {tid}: {ex.Message}");
            }
        }

        if (req.SelectedChapterIds.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            done++;
            progress.Report(new ExtractionProgress(req.Source.FileName, "Chapters",
                total > 0 ? done * 100 / total : 0, "Extracting chapters...", false));
            try
            {
                var chapFile = $"{req.OutputDirectory}\\{fn}_chapters.xml";
                await _runner.RunAsync("mp4box.exe", new[] { "-dump-chap", req.Source.FilePath, "-out", chapFile }, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"chapters: {ex.Message}");
            }
        }

        progress.Report(new ExtractionProgress("", "", 100, "Done", IsComplete: true));
        return failures.Count == 0 ? ExtractOutcome.Success : new ExtractOutcome(false, failures);
    }

    internal static string BuildRawOutputName(ExtractRequest req, int trackId)
    {
        var fn = Path.GetFileNameWithoutExtension(req.Source.FilePath);
        var track = req.Source.Tracks.Find(t => t.Id == trackId);
        var ext = track is null
            ? "bin"
            : Mp4CodecExtensions.GetExtension(track.Properties.GetValueOrDefault("CodecId", ""));
        return $"{fn}_Track{trackId + 1}.{ext}";
    }
}
