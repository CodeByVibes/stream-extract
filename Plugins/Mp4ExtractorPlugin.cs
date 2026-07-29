using System.Text.RegularExpressions;
using StreamExtract.Models;
using StreamExtract.Services;

namespace StreamExtract.Plugins;

public sealed partial class Mp4ExtractorPlugin(string toolPath) : IExtractorPlugin
{
    private static readonly HashSet<string> _exts = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".m4a", ".m4b" };

    public string Name => "MP4 Extractor";
    public HashSet<string> SupportedExtensions => _exts;
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
    [GeneratedRegex(@"Chapter\s+#(\d+)\s*-\s*\S+\s*-\s*""(.+?)""", RegexOptions.IgnoreCase)]
    private static partial Regex ChapterRe();

    public async Task<MediaFileInfo> AnalyzeFileAsync(string filePath, CancellationToken ct = default)
    {
        var runner = new ProcessRunner(toolPath);
        var (_, stdout, stderr) = await runner.RunAsync("mp4box.exe", $"-info \"{filePath}\"", ct);
        var output = stdout + stderr;
        var lines = output.Split('\n');

        var tracks = new List<TrackInfo>();

        for (int i = 0; i < lines.Length; i++)
        {
            var trackMatch = TrackRe().Match(lines[i]);
            if (!trackMatch.Success) continue;

            var trackId = int.Parse(trackMatch.Groups[2].Value);

            var codec = "unknown";
            var mediaType = "";
            var props = new Dictionary<string, string>();

            for (int j = i + 1; j < Math.Min(i + 20, lines.Length); j++)
            {
                var line = lines[j];

                // Media Type: vide:avc1
                var mt = MediaTypeRe().Match(line);
                if (mt.Success)
                {
                    mediaType = mt.Groups[1].Value.ToLowerInvariant();
                    codec = mt.Groups[2].Value;
                }

                // width=640 height=480
                var dims = DimsRe().Match(line);
                if (dims.Success && string.IsNullOrEmpty(props.GetValueOrDefault("PixelDimensions")))
                    props["PixelDimensions"] = $"{dims.Groups[1].Value}x{dims.Groups[2].Value}";

                // 2 Channel(s)
                var ch = ChannelsRe().Match(line);
                if (ch.Success) props["Channels"] = ch.Groups[1].Value;

                // SampleRate 48000
                var sr = SampleRateRe().Match(line);
                if (sr.Success) props["SampleRate"] = sr.Groups[1].Value;

                // Stop at next track header
                if (j > i + 1 && TrackRe().IsMatch(line)) break;
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

    public async Task ExtractAsync(ExtractRequest req, IProgress<ExtractionProgress> progress, CancellationToken ct = default)
    {
        var runner = new ProcessRunner(toolPath);
        var fn = Path.GetFileNameWithoutExtension(req.Source.FilePath);
        var total = req.SelectedTrackIds.Count + (req.SelectedChapterIds.Count > 0 ? 1 : 0);
        var done = 0;

        foreach (var tid in req.SelectedTrackIds)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(new ExtractionProgress(req.Source.FileName, $"Track {tid}",
                total > 0 ? done * 100 / total : 0, $"Extracting track {tid}...", false));
            await runner.RunAsync("mp4box.exe", $"-raw {tid} \"{req.Source.FilePath}\"", ct, req.OutputDirectory);
            done++;
        }

        if (req.SelectedChapterIds.Count > 0)
        {
            progress.Report(new ExtractionProgress(req.Source.FileName, "Chapters",
                total > 0 ? done * 100 / total : 0, "Extracting chapters...", false));
            var chapFile = $"{req.OutputDirectory}\\{fn}_chapters.xml";
            await runner.RunAsync("mp4box.exe", $"-dump-chap \"{req.Source.FilePath}\" -out \"{chapFile}\"", ct);
        }

        progress.Report(new ExtractionProgress("", "", 100, "Done", IsComplete: true));
    }
}
