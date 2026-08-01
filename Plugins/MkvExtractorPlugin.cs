using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
            new Dictionary<string, string> { ["CodecId"] = t.Properties?.CodecId ?? "", ["Duration"] = durFmt }
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

    public async Task ExtractAsync(ExtractRequest req, IProgress<ExtractionProgress> progress, CancellationToken ct = default)
    {
        if (req.SelectedTrackIds.Count > 0)
        {
            await RunModeAsync(req, BuildTracksCommand(req), progress, ct);
        }
        if (req.SelectedChapterIds.Count > 0)
        {
            await RunModeAsync(req, BuildChaptersCommand(req), progress, ct);
        }
        if (req.ExtractAttachments && req.Source.Attachments.Count > 0)
        {
            await RunModeAsync(req, BuildAttachmentsCommand(req), progress, ct);
        }
        if (req.ExtractTags)
        {
            await RunModeAsync(req, BuildTagsCommand(req), progress, ct);
        }
        if (req.ExtractCueSheets)
        {
            await RunModeAsync(req, BuildCueSheetsCommand(req), progress, ct);
        }
        if (req.ExtractTimestamps && req.SelectedTrackIds.Count > 0)
        {
            await RunModeAsync(req, BuildTimestampsCommand(req), progress, ct);
        }
    }

    private Task RunModeAsync(ExtractRequest req, IEnumerable<string> args,
        IProgress<ExtractionProgress> progress, CancellationToken ct)
        => _runner.RunWithProgressAsync("mkvextract.exe", args, ParseProgress, progress, ct);

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

    internal static IEnumerable<string> BuildAttachmentsCommand(ExtractRequest req)
    {
        var args = new List<string> { req.Source.FilePath, "attachments" };
        foreach (var a in req.Source.Attachments)
        {
            var containedPath = OutputPathGuard.ResolveContainedPath(req.OutputDirectory, a.FileName);
            args.Add($"{a.Id}:{containedPath}");
        }
        return args;
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
