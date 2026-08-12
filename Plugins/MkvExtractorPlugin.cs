using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StreamExtract.Models;
using StreamExtract.Services;

namespace StreamExtract.Plugins;

public sealed partial class MkvExtractorPlugin(string toolPath, IProcessRunner? runner = null) : IExtractorPlugin
{
    private readonly IProcessRunner _runner = runner ?? new ProcessRunner(toolPath);

    private static readonly HashSet<string> _exts = new(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mka" };

    public string Name => "MKV Extractor";
    public IReadOnlySet<string> SupportedExtensions => _exts;
    public ExtractorFeatures SupportedFeatures => ExtractorFeatures.Tracks | ExtractorFeatures.Chapters |
        ExtractorFeatures.Attachments | ExtractorFeatures.Tags | ExtractorFeatures.CueSheets | ExtractorFeatures.Timestamps;

    public async Task<MediaFileInfo> AnalyzeFileAsync(string filePath, CancellationToken ct = default)
    {
        var result = await _runner.RunAsync("mkvmerge.exe", new[] { filePath, "-i", "-F", "json" }, ct);
        var raw = JsonSerializer.Deserialize<MkvJsonRoot>(result.StandardOutput)
            ?? throw new InvalidOperationException("Failed to parse mkvmerge JSON.");

        var durNs = raw.Container?.Properties?.Duration ?? 0;
        var durFmt = FormatDuration(durNs);
        var tracks = raw.Tracks?.Select(t => new TrackInfo(t.Id,
            t.Type?.ToLowerInvariant() switch { "video" => TrackType.Video, "audio" => TrackType.Audio, "subtitles" => TrackType.Subtitle, _ => TrackType.Other },
            t.Codec ?? "unknown", t.Properties?.TrackName ?? "", t.Properties?.Language ?? "und",
            new Dictionary<string, string>
            {
                ["CodecId"] = t.Properties?.CodecId ?? "",
                ["Duration"] = durFmt,
                ["PixelDimensions"] = t.Properties?.PixelDimensions ?? ""
            }
        )).ToList() ?? [];

        var chapters = (raw.Chapters?.Any() == true)
            ? new List<ChapterInfo> { new ChapterInfo(0, $"Chapters ({raw.Chapters![0].NumEntries} entries)", "") } : new List<ChapterInfo>();

        var attachments = raw.Attachments?.Select(a =>
            new AttachmentInfo(a.Id, a.FileName ?? "attachment", a.ContentType ?? "application/octet-stream", a.Size)).ToList() ?? [];

        var tags = new List<TagInfo>();
        if (raw.GlobalTags?.Any() == true) tags.Add(new TagInfo(-1, "Global", $"{raw.GlobalTags.Count} entries"));
        if (raw.TrackTags != null)
            foreach (var tt in raw.TrackTags) tags.Add(new TagInfo(tt.TrackId, $"Track {tt.TrackId}", $"{tt.NumEntries} entries"));

        return new MediaFileInfo(filePath, Path.GetFileName(filePath), SupportedFeatures, tracks, chapters, attachments, tags);
    }

    public async Task<ExtractOutcome> ExtractAsync(ExtractRequest req, IProgress<ExtractionProgress> progress, CancellationToken ct = default)
    {
        var (modes, buildFailures) = BuildModes(req, progress);
        if (modes.Count == 0)
            return buildFailures.Count > 0 ? new ExtractOutcome(false, buildFailures) : ExtractOutcome.Success;

        var failures = new List<string>(buildFailures);
        for (var i = 0; i < modes.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (name, run) = modes[i];
            try
            {
                await run(i, modes.Count, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.Message}");
            }
        }

        return failures.Count == 0 ? ExtractOutcome.Success : new ExtractOutcome(false, failures);
    }

    internal (List<(string Name, Func<int, int, CancellationToken, Task> Run)> Modes, List<string> Failures)
        BuildModes(ExtractRequest req, IProgress<ExtractionProgress> progress)
    {
        var modes = new List<(string, Func<int, int, CancellationToken, Task>)>();
        var failures = new List<string>();

        if (req.SelectedTrackIds.Count > 0)
        {
            var args = BuildTracksCommand(req);
            modes.Add(("tracks", (i, n, ct) => RunModeAsync(args, i, n, progress, ct)));
        }
        if (req.SelectedChapterIds.Count > 0)
        {
            var args = BuildChaptersCommand(req);
            modes.Add(("chapters", (i, n, ct) => RunModeAsync(args, i, n, progress, ct)));
        }
        if (req.ExtractAttachments && req.Source.Attachments.Count > 0)
        {
            var (args, attachmentFailures) = BuildAttachmentsCommand(req);
            failures.AddRange(attachmentFailures);
            if (args.Count > 2) modes.Add(("attachments", (i, n, ct) => RunModeAsync(args, i, n, progress, ct)));
            else failures.Add("attachments: no valid attachment names to extract");
        }
        if (req.ExtractTags)
        {
            var args = BuildTagsCommand(req);
            modes.Add(("tags", (i, n, ct) => RunModeAsync(args, i, n, progress, ct)));
        }
        if (req.ExtractCueSheets)
        {
            var args = BuildCueSheetsCommand(req);
            modes.Add(("cuesheet", (i, n, ct) => RunModeAsync(args, i, n, progress, ct)));
        }
        if (req.ExtractCuesForSelectedTracks && req.SelectedTrackIds.Count > 0)
            modes.Add(("cues for selected tracks", (i, n, ct) => ExtractCuesForSelectedTracksAsync(req, progress, i, n, ct)));
        if (req.ExtractTimestamps && req.SelectedTrackIds.Count > 0)
        {
            var args = BuildTimestampsCommand(req);
            modes.Add(("timestamps", (i, n, ct) => RunModeAsync(args, i, n, progress, ct)));
        }

        return (modes, failures);
    }

    private Task RunModeAsync(IEnumerable<string> args, int modeIndex, int modeCount,
        IProgress<ExtractionProgress> progress, CancellationToken ct)
    {
        // Aggregate the per-mode 0..100 progress into an overall 0..100 across all modes,
        // so the progress bar advances monotonically instead of resetting per mode.
        IProgress<ExtractionProgress> aggregate = new Progress<ExtractionProgress>(p =>
        {
            var overall = Math.Clamp((modeIndex * 100 + p.Percentage) / modeCount, 0, 100);
            progress.Report(new ExtractionProgress("", "", overall, $"Extracting... {overall}%", false));
        });
        return _runner.RunWithProgressAsync("mkvextract.exe", args, ParseProgress, aggregate, ct);
    }

    /// <summary>
    /// Writes one CUE sheet per selected track. The cue is generated from the chapter XML:
    /// mkvextract's own cuesheet mode has no per-track mode and drops chapter names/times
    /// for chapters that were not imported from a CUE sheet, so it cannot be used here.
    /// </summary>
    private async Task ExtractCuesForSelectedTracksAsync(ExtractRequest req,
        IProgress<ExtractionProgress> progress, int modeIndex, int modeCount, CancellationToken ct)
    {
        var fn = Path.GetFileNameWithoutExtension(req.Source.FilePath);
        var xmlPath = $"{req.OutputDirectory}\\{fn}_chapters.xml";

        await RunModeAsync(BuildChaptersCommand(req), modeIndex, modeCount, progress, ct);

        var chapters = ParseChapterXml(await File.ReadAllTextAsync(xmlPath, ct));
        if (chapters.Count == 0)
            throw new InvalidDataException("no chapters found in the source file");

        foreach (var tid in req.SelectedTrackIds)
        {
            ct.ThrowIfCancellationRequested();
            var track = req.Source.Tracks.Find(t => t.Id == tid);
            if (track is null) continue;
            var ext = MkvCodecExtensions.GetExtension(track.Properties.GetValueOrDefault("CodecId", ""));
            var cuePath = $"{req.OutputDirectory}\\{fn}_Track{tid + 1}_cues.cue";
            await File.WriteAllTextAsync(cuePath, BuildPerTrackCue($"{fn}_Track{tid + 1}.{ext}", chapters), ct);
        }
    }

    internal static List<(string Name, TimeSpan Start)> ParseChapterXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        var result = new List<(string, TimeSpan)>();
        foreach (var atom in doc.Descendants("ChapterAtom"))
        {
            var startEl = atom.Element("ChapterTimeStart");
            if (startEl is null || !TryParseChapterTime(startEl.Value, out var start)) continue;
            var name = atom.Element("ChapterDisplay")?.Element("ChapterString")?.Value ?? "";
            result.Add((name, start));
        }
        return result;
    }

    private static bool TryParseChapterTime(string value, out TimeSpan time)
    {
        // ChapterTimeStart is HH:MM:SS.nnnnnnnnn (9 fractional digits); TimeSpan only
        // parses up to 7, so trim the fraction before parsing.
        var dot = value.IndexOf('.');
        if (dot >= 0 && value.Length > dot + 8)
            value = value[..(dot + 8)];
        return TimeSpan.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out time);
    }

    internal static string BuildPerTrackCue(string fileReference, IReadOnlyList<(string Name, TimeSpan Start)> chapters)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"FILE \"{fileReference}\" WAVE");
        for (var i = 0; i < chapters.Count; i++)
        {
            sb.AppendLine($"  TRACK {i + 1:D2} AUDIO");
            var title = chapters[i].Name.Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ');
            if (title.Length > 0)
                sb.AppendLine($"    TITLE \"{title}\"");
            sb.AppendLine($"    INDEX 01 {FormatCueTime(chapters[i].Start)}");
        }
        return sb.ToString();
    }

    internal static string FormatCueTime(TimeSpan start)
    {
        var totalSeconds = (long)Math.Round(start.TotalSeconds, MidpointRounding.AwayFromZero);
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    [GeneratedRegex(@"(\d+)%")] private static partial Regex ProgressRe();
    private static ExtractionProgress? ParseProgress(string line)
    {
        var m = ProgressRe().Match(line);
        if (!m.Success) return null;
        var pct = int.Parse(m.Groups[1].Value);
        return new ExtractionProgress("", "", pct, $"Extracting... {pct}%", false);
    }

    internal static IEnumerable<string> BuildTracksCommand(ExtractRequest req)
    {
        var fn = Path.GetFileNameWithoutExtension(req.Source.FilePath);
        var args = new List<string> { req.Source.FilePath, "tracks" };
        foreach (var tid in req.SelectedTrackIds)
        {
            var t = req.Source.Tracks.Find(x => x.Id == tid);
            if (t is null) continue;
            var ext = MkvCodecExtensions.GetExtension(t.Properties.GetValueOrDefault("CodecId", ""));
            args.Add($"{tid}:{req.OutputDirectory}\\{fn}_Track{tid + 1}.{ext}");
        }
        return args;
    }

    internal static IEnumerable<string> BuildChaptersCommand(ExtractRequest req)
    {
        var fn = Path.GetFileNameWithoutExtension(req.Source.FilePath);
        return new[] { req.Source.FilePath, "chapters", $"{req.OutputDirectory}\\{fn}_chapters.xml" };
    }

    internal static (List<string> Args, List<string> Failures) BuildAttachmentsCommand(ExtractRequest req)
    {
        var args = new List<string> { req.Source.FilePath, "attachments" };
        var failures = new List<string>();
        foreach (var a in req.Source.Attachments)
        {
            try
            {
                var containedPath = OutputPathGuard.ResolveContainedPath(req.OutputDirectory, a.FileName);
                args.Add($"{a.Id}:{containedPath}");
            }
            catch (InvalidDataException ex)
            {
                failures.Add($"attachment '{a.FileName}': {ex.Message}");
            }
        }
        return (args, failures);
    }

    internal static IEnumerable<string> BuildTagsCommand(ExtractRequest req)
    {
        var fn = Path.GetFileNameWithoutExtension(req.Source.FilePath);
        return new[] { req.Source.FilePath, "tags", $"{req.OutputDirectory}\\{fn}_tags.xml" };
    }

    internal static IEnumerable<string> BuildCueSheetsCommand(ExtractRequest req)
    {
        var fn = Path.GetFileNameWithoutExtension(req.Source.FilePath);
        return new[] { req.Source.FilePath, "cuesheet", $"{req.OutputDirectory}\\{fn}_cuesheet.cue" };
    }

    internal static IEnumerable<string> BuildTimestampsCommand(ExtractRequest req)
    {
        var fn = Path.GetFileNameWithoutExtension(req.Source.FilePath);
        var args = new List<string> { req.Source.FilePath, "timestamps_v2" };
        foreach (var tid in req.SelectedTrackIds)
        {
            if (req.Source.Tracks.Find(x => x.Id == tid) is null) continue;
            args.Add($"{tid}:{req.OutputDirectory}\\{fn}_Track{tid + 1}_timestamps.txt");
        }
        return args;
    }

    private static string FormatDuration(long ns) => ns == 0 ? "" : TimeSpan.FromMilliseconds(ns / 1_000_000L).ToString(@"hh\:mm\:ss\.fff");

    // JSON DTOs
    internal record MkvJsonRoot(
        [property: JsonPropertyName("tracks")] List<MkvJsonTrack>? Tracks,
        [property: JsonPropertyName("attachments")] List<MkvJsonAttachment>? Attachments,
        [property: JsonPropertyName("chapters")] List<MkvJsonChapter>? Chapters,
        [property: JsonPropertyName("container")] MkvJsonContainer? Container,
        [property: JsonPropertyName("global_tags")] List<MkvJsonTagHeader>? GlobalTags,
        [property: JsonPropertyName("track_tags")] List<MkvJsonTrackTag>? TrackTags);
    internal record MkvJsonTrack([property: JsonPropertyName("codec")] string Codec, [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("properties")] MkvJsonTrackProps? Properties, [property: JsonPropertyName("type")] string? Type);
    internal record MkvJsonTrackProps([property: JsonPropertyName("codec_id")] string? CodecId,
        [property: JsonPropertyName("language")] string? Language, [property: JsonPropertyName("track_name")] string? TrackName,
        [property: JsonPropertyName("pixel_dimensions")] string? PixelDimensions);
    internal record MkvJsonAttachment([property: JsonPropertyName("id")] long Id, [property: JsonPropertyName("file_name")] string? FileName,
        [property: JsonPropertyName("content_type")] string? ContentType, [property: JsonPropertyName("size")] long Size);
    internal record MkvJsonChapter([property: JsonPropertyName("num_entries")] int NumEntries);
    internal record MkvJsonTagHeader([property: JsonPropertyName("num_entries")] int NumEntries);
    internal record MkvJsonTrackTag([property: JsonPropertyName("num_entries")] int NumEntries, [property: JsonPropertyName("track_id")] int TrackId);
    internal record MkvJsonContainer([property: JsonPropertyName("properties")] MkvJsonContainerProps? Properties);
    internal record MkvJsonContainerProps([property: JsonPropertyName("duration")] long Duration);
}
