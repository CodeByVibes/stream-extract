using StreamExtract.Models;
using StreamExtract.Plugins;
using StreamExtract.Services;

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

    [Fact]
    public void BuildTimestampsCommand_UsesOnlyExplicitlySelectedTracks()
    {
        var info = Request().Source with
        {
            Tracks =
            [
                new TrackInfo(0, TrackType.Audio, "A_AAC", "Audio", "eng", new() { ["CodecId"] = "A_AAC" }),
                new TrackInfo(1, TrackType.Audio, "A_OPUS", "Commentary", "eng", new() { ["CodecId"] = "A_OPUS" })
            ]
        };
        var request = new ExtractRequest(info, Request().OutputDirectory, [1], [], false, false, false, true, false);

        var args = MkvExtractorPlugin.BuildTimestampsCommand(request).ToArray();

        Assert.Contains("1:" + Path.Combine(request.OutputDirectory, "movie_Track2_timestamps.txt"), args);
        Assert.DoesNotContain(args, arg => arg.StartsWith("0:"));
    }

    [Fact]
    public async Task ExtractAsync_UsesResolverExecutableAndWorkingDirectory()
    {
        var resolver = new TestNativeToolResolver();
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "progress 100%", "");
        var plugin = new MkvExtractorPlugin(resolver, runner);
        var output = Path.Combine(Path.GetTempPath(), "stream-extract-mkv-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var request = Request(output) with { SelectedChapterIds = [] };

        try
        {
            var outcome = await plugin.ExtractAsync(request, new Progress<ExtractionProgress>());

            Assert.True(outcome.Succeeded);
            var call = Assert.Single(runner.Calls);
            Assert.Equal(resolver.Resolve(NativeToolId.MkvExtract), call.FileName);
            Assert.True(Path.IsPathFullyQualified(call.FileName));
            Assert.Equal(MkvExtractorPlugin.BuildTracksCommand(request).ToArray(), call.Arguments);
            Assert.Null(call.WorkingDirectory);
            Assert.Equal(default, call.CancellationToken);
            Assert.Null(call.Timeout);
        }
        finally
        {
            Directory.Delete(output, true);
        }
    }

    [Fact]
    public async Task ExtractAsync_PreservesPathsAsSingleArgumentListEntries()
    {
        var resolver = new TestNativeToolResolver();
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", "");
        var sourcePath = Path.Combine(Path.GetTempPath(), "media [test]", "movie;$(touch hacked).mkv");
        var output = Path.Combine(Path.GetTempPath(), "output folder;$(touch hacked)");
        var request = Request(output) with
        {
            Source = new MediaFileInfo(sourcePath, Path.GetFileName(sourcePath), ExtractorFeatures.Tracks,
                [new TrackInfo(0, TrackType.Audio, "A_AAC", "Audio", "eng", new() { ["CodecId"] = "A_AAC" })],
                [], [], []),
            SelectedChapterIds = []
        };

        await new MkvExtractorPlugin(resolver, runner).ExtractAsync(request, new Progress<ExtractionProgress>());

        Assert.Equal([sourcePath, "tracks", "0:" + Path.Combine(output, "movie;$(touch hacked)_Track1.aac")],
            Assert.Single(runner.Calls).Arguments);
    }
}
