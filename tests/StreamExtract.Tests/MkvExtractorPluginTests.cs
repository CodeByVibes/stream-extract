using StreamExtract.Models;
using StreamExtract.Plugins;

namespace StreamExtract.Tests;

public sealed class MkvExtractorPluginTests
{
    private static ExtractRequest Request(string? output = null)
    {
        var info = new MediaFileInfo(
            Path.Combine(Path.GetTempPath(), "media", "movie.mkv"), "movie.mkv", ExtractorFeatures.Tracks | ExtractorFeatures.Chapters,
            [new TrackInfo(0, TrackType.Audio, "A_AAC", "Audio", "eng", new() { ["CodecId"] = "A_AAC" })],
            [new ChapterInfo(0, "Chapters", "")], [], []);
        return new ExtractRequest(info, output ?? Path.Combine(Path.GetTempPath(), "stream-extract-mkv-tests"), [0], [0], false, false, false, false, false);
    }

    [Theory]
    [InlineData(0, 0, 0, "00:00:00")]
    [InlineData(0, 1, 0, "00:01:00")]
    [InlineData(1, 23, 500, "01:23:38")]
    [InlineData(60, 0, 0, "60:00:00")]
    public void FormatCueTime_UsesCueMinutesSecondsFrames(int minutes, int seconds, int milliseconds, string expected)
    {
        Assert.Equal(expected, MkvExtractorPlugin.FormatCueTime(new TimeSpan(0, 0, minutes, seconds, milliseconds)));
    }

    [Fact]
    public void FormatCueTime_RoundsFrameAndSecondRollover()
    {
        Assert.Equal("00:01:00", MkvExtractorPlugin.FormatCueTime(TimeSpan.FromSeconds(0.999)));
    }

    [Fact]
    public void BuildPerTrackCue_UsesStandardCueTimestamp()
    {
        var cue = MkvExtractorPlugin.BuildPerTrackCue(
            "movie_Track1.aac",
            [("Intro", TimeSpan.FromSeconds(83.5))]);

        Assert.Contains("INDEX 01 01:23:38", cue);
    }

    [Fact]
    public void BuildCommands_ContainContainedOutputPaths()
    {
        var request = Request();
        var tracks = MkvExtractorPlugin.BuildTracksCommand(request).ToArray();
        var chapters = MkvExtractorPlugin.BuildChaptersCommand(request).ToArray();
        var tags = MkvExtractorPlugin.BuildTagsCommand(request).ToArray();
        var cues = MkvExtractorPlugin.BuildCueSheetsCommand(request).ToArray();
        var timestamps = MkvExtractorPlugin.BuildTimestampsCommand(
            request with { ExtractTimestamps = true }).ToArray();

        var output = request.OutputDirectory;
        Assert.Contains($"0:{Path.Combine(output, "movie_Track1.aac")}", tracks);
        Assert.Contains(Path.Combine(output, "movie_chapters.xml"), chapters);
        Assert.Contains(Path.Combine(output, "movie_tags.xml"), tags);
        Assert.Contains(Path.Combine(output, "movie_cuesheet.cue"), cues);
        Assert.Contains($"0:{Path.Combine(output, "movie_Track1_timestamps.txt")}", timestamps);
    }
}
