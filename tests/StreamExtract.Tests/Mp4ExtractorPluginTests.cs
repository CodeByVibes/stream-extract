using StreamExtract.Models;
using StreamExtract.Plugins;
using StreamExtract.Services;

namespace StreamExtract.Tests;

public class Mp4ExtractorPluginTests
{
    private const string SampleInfo =
        """
        # Track 1 Info - ID 1
        Track Type: video
        Media Type: vide:avc1
        Width: 640 Height: 480
        width=640 height=480
        # Track 2 Info - ID 2
        Track Type: audio
        Media Type: soun:mp4a
        2 Channel(s)
        SampleRate 48000
        """;

    // Captured from the bundled mp4box (GPAC) with `-info` on a real file with 2 tracks and
    // 3 chapters. Whitespace is normalized (tabs → spaces); parsing is whitespace-agnostic.
    private const string RealInfoOutput =
        """
        # Movie Info - 2 tracks - TimeScale 1000
        Duration 00:31:28.700
        Fragmented: no
        Major Brand mp42 - version 512 - compatible brands: mp42 iso2 avc1 mp41
        Created: GMT Sat Jul 25 15:25:04 2026


        Chapters:
            #1 - 00:00:00.000 - "Intro"
            #2 - 00:00:05.000 - "Part Two"
            #3 - 00:01:00.500 - "Part Three"

        Meta-Data Tags:
            tool: HandBrake 1.11.2 2026060700
        1 UDTA types:
            meta:

        # Track 1 Info - ID 1 - TimeScale 90000
        Media Duration 00:31:28.733
        Track has 1 edits: track duration is 00:31:28.700
        Track flags: Enabled In Movie
        Media Samples: 56661 - CFR 30/sec
        Visual Track layout: x=0 y=0 width=640 height=480
        Media Type: vide:avc1
            Visual Sample Entry Info: width=640 height=480 (depth=24 bits)
            AVC/H264 Video - Visual Size 640 x 480
            AVC Info: 1 SPS - 1 PPS - Profile Main @ Level 4
            NAL Unit length bits: 32
            Pixel Aspect Ratio 1:1 - Indicated track size 640 x 480
            Chroma format YUV 4:2:0 - Luma bit depth 8 - chroma bit depth 8
            SPS#1 hash: E5F0EFBA7FD5DD485BDBE84F2C3E4B795DA3CECF
            PPS#1 hash: 9C3B95A61B186D4EE83EAB6C385DCCD8CE9EF6CA
            RFC6381 Codec Parameters: avc1.4D4028

            Average GOP length: 179 samples
            Max sample duration: 3000 / 90000

        # Track 2 Info - ID 2 - TimeScale 48000
        Media Duration 00:31:28.704
        Track has 1 edits: track duration is 00:31:28.682
        Track flags: Enabled In Movie
        Media Samples: 88533 - CFR 46.875000/sec
        2 UDTA types:
            name: eo
            titl:  unknown type (13 bytes)
        Alternate Group ID 1
        Media Type: soun:mp4a
            MPEG-4 Audio AAC LC (AOT=2 implicit) - 2 Channel(s) - SampleRate 48000
            RFC6381 Codec Parameters: mp4a.40.2

            All samples are sync
            Max sample duration: 1024 / 48000

        """;

    [Fact]
    public async Task AnalyzeFileAsync_ParsesTracksFromStderr()
    {
        // mp4box (GPAC) writes "-info" output to stderr, stdout stays empty.
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", SampleInfo);
        var plugin = new Mp4ExtractorPlugin(new TestNativeToolResolver(), runner);

        var info = await plugin.AnalyzeFileAsync(Path.Combine(Path.GetTempPath(), "media", "movie.mp4"));

        Assert.Equal(2, info.Tracks.Count);
        Assert.Equal(1, info.Tracks[0].Id);
        Assert.Equal(TrackType.Video, info.Tracks[0].Type);
        Assert.Equal("avc1", info.Tracks[0].Codec);
        Assert.Equal("640x480", info.Tracks[0].Properties["PixelDimensions"]);
        Assert.Equal(2, info.Tracks[1].Id);
        Assert.Equal(TrackType.Audio, info.Tracks[1].Type);
        Assert.Equal("48000", info.Tracks[1].Properties["SampleRate"]);
        Assert.Equal("2", info.Tracks[1].Properties["Channels"]);
    }

    [Fact]
    public async Task AnalyzeFileAsync_ParsesRealMp4boxOutput()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", RealInfoOutput);
        var plugin = new Mp4ExtractorPlugin(new TestNativeToolResolver(), runner);

        var info = await plugin.AnalyzeFileAsync(Path.Combine(Path.GetTempPath(), "media", "movie.mp4"));

        Assert.Equal(2, info.Tracks.Count);
        Assert.Equal(1, info.Tracks[0].Id);
        Assert.Equal(TrackType.Video, info.Tracks[0].Type);
        Assert.Equal("avc1", info.Tracks[0].Codec);
        Assert.Equal("640x480", info.Tracks[0].Properties["PixelDimensions"]);
        Assert.Equal(2, info.Tracks[1].Id);
        Assert.Equal(TrackType.Audio, info.Tracks[1].Type);
        Assert.Equal("mp4a", info.Tracks[1].Codec);
        Assert.Equal("48000", info.Tracks[1].Properties["SampleRate"]);
        Assert.Equal("2", info.Tracks[1].Properties["Channels"]);
    }

    [Fact]
    public async Task AnalyzeFileAsync_ParsesChaptersFromRealFormat()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", RealInfoOutput);
        var plugin = new Mp4ExtractorPlugin(new TestNativeToolResolver(), runner);

        var info = await plugin.AnalyzeFileAsync(@"C:\media\movie.mp4");

        Assert.Equal(3, info.Chapters.Count);
        Assert.Equal("Intro", info.Chapters[0].Name);
        Assert.Equal("Part Two", info.Chapters[1].Name);
        Assert.Equal("Part Three", info.Chapters[2].Name);
    }

    [Fact]
    public async Task AnalyzeFileAsync_ParsesChapterPrefixedFormatForOlderGpac()
    {
        var output = "# Track 1 Info - ID 1\nMedia Type: vide:avc1\nChapter #1 - 00:00:00.000 - \"Intro\"\n";
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", output);
        var plugin = new Mp4ExtractorPlugin(new TestNativeToolResolver(), runner);

        var info = await plugin.AnalyzeFileAsync(@"C:\media\movie.mp4");

        Assert.Single(info.Chapters);
        Assert.Equal("Intro", info.Chapters[0].Name);
    }

    [Fact]
    public async Task AnalyzeFileAsync_TrackBlockLongerThanOldCap_StillParses()
    {
        // A track block with 30 informational lines (more than the old 20-line cap)
        // must still be parsed fully when the next track header follows.
        var output = new System.Text.StringBuilder();
        output.AppendLine("# Track 1 Info - ID 1");
        for (var i = 0; i < 30; i++) output.AppendLine($"Info line {i}");
        output.AppendLine("Media Type: vide:avc1");
        output.AppendLine("# Track 2 Info - ID 2");
        output.AppendLine("Media Type: soun:mp4a");

        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", output.ToString());
        var plugin = new Mp4ExtractorPlugin(new TestNativeToolResolver(), runner);

        var info = await plugin.AnalyzeFileAsync(@"C:\media\movie.mp4");

        Assert.Equal(2, info.Tracks.Count);
        Assert.Equal("avc1", info.Tracks[0].Codec);
    }

    [Fact]
    public async Task AnalyzeFileAsync_MalformedTrackId_SkipsTrack()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", "# Track 1 Info - ID abc\nMedia Type: vide:avc1\n# Track 2 Info - ID 2\nMedia Type: soun:mp4a\n");
        var plugin = new Mp4ExtractorPlugin(new TestNativeToolResolver(), runner);

        var info = await plugin.AnalyzeFileAsync(@"C:\media\movie.mp4");

        Assert.Single(info.Tracks);
        Assert.Equal(2, info.Tracks[0].Id);
    }

    [Fact]
    public async Task AnalyzeFileAsync_EmptyOutput_YieldsNoTracks()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", "");
        var plugin = new Mp4ExtractorPlugin(new TestNativeToolResolver(), runner);

        var info = await plugin.AnalyzeFileAsync(@"C:\media\movie.mp4");

        Assert.Empty(info.Tracks);
    }

    [Fact]
    public async Task AnalyzeFileAsync_InfoOnStdoutOnly_YieldsNoTracks()
    {
        // mp4box writes "-info" to stderr; stdout-only output must not be parsed.
        var runner = new FakeProcessRunner();
        runner.AddResult(0, SampleInfo, "");
        var plugin = new Mp4ExtractorPlugin(new TestNativeToolResolver(), runner);

        var info = await plugin.AnalyzeFileAsync(@"C:\media\movie.mp4");

        Assert.Empty(info.Tracks);
    }

    [Theory]
    [InlineData("avc1", "h264")]
    [InlineData("AVC3", "h264")]
    [InlineData("hvc1", "h265")]
    [InlineData("mp4a", "aac")]
    [InlineData("opus", "opus")]
    [InlineData("av01", "av1")]
    [InlineData("something-unknown", "bin")]
    public void Mp4CodecExtensions_GetExtension_MapsCodecs(string codec, string expected)
    {
        Assert.Equal(expected, Mp4CodecExtensions.GetExtension(codec));
    }

    [Fact]
    public void BuildRawOutputName_UsesCodecExtension()
    {
        var info = new MediaFileInfo(
            Path.Combine(Path.GetTempPath(), "media", "movie.mp4"), "movie.mp4",
            ExtractorFeatures.Tracks,
            [new TrackInfo(1, TrackType.Video, "avc1", "Track 1", "und", new() { ["CodecId"] = "avc1" })],
            [], [], []);
        var req = new ExtractRequest(info, Path.Combine(Path.GetTempPath(), "out"), [1], [], false, false, false, false, false);

        Assert.Equal("movie_Track2.h264", Mp4ExtractorPlugin.BuildRawOutputName(req, 1));
    }

    [Fact]
    public async Task ResolverPluginRunner_UsesAbsoluteBundledToolAndExtractionPaths()
    {
        var resolver = new TestNativeToolResolver();
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", "");
        var plugin = new Mp4ExtractorPlugin(resolver, runner);
        var source = new MediaFileInfo(
            Path.Combine(Path.GetTempPath(), "media", "movie.mp4"), "movie.mp4", ExtractorFeatures.Tracks,
            [new TrackInfo(1, TrackType.Video, "avc1", "Track 1", "und", new() { ["CodecId"] = "avc1" })],
            [], [], []);
        var output = Path.Combine(Path.GetTempPath(), "stream-extract-output", Guid.NewGuid().ToString("N"));
        var request = new ExtractRequest(source, output, [1], [], false, false, false, false, false);

        var outcome = await plugin.ExtractAsync(request, new Progress<ExtractionProgress>());

        Assert.True(outcome.Succeeded);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(resolver.Resolve(NativeToolId.Mp4Box), call.FileName);
        Assert.Equal(["-raw", "1:output=" + Path.Combine(output, "movie_Track2.h264"), source.FilePath], call.Arguments);
        Assert.Equal(output, call.WorkingDirectory);
        Assert.True(Path.IsPathFullyQualified(call.FileName));
        Assert.True(Path.IsPathFullyQualified(source.FilePath));
        Assert.True(Path.IsPathFullyQualified(output));
        Assert.Equal(default, call.CancellationToken);
        Assert.Null(call.Timeout);
    }

    [Fact]
    public async Task ExtractAsync_PreservesPathsAsSingleArgumentListEntries()
    {
        var resolver = new TestNativeToolResolver();
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", "");
        var sourcePath = Path.Combine(Path.GetTempPath(), "media [test]", "movie;$(touch hacked).mp4");
        var output = Path.Combine(Path.GetTempPath(), "output folder;$(touch hacked)");
        var request = new ExtractRequest(new MediaFileInfo(sourcePath, Path.GetFileName(sourcePath),
            ExtractorFeatures.Tracks, [new TrackInfo(1, TrackType.Video, "avc1", "Track", "und",
                new() { ["CodecId"] = "avc1" })], [], [], []), output, [1], [], false, false, false, false, false);

        await new Mp4ExtractorPlugin(resolver, runner).ExtractAsync(request, new Progress<ExtractionProgress>());

        Assert.Equal(["-raw", "1:output=" + Path.Combine(output, "movie;$(touch hacked)_Track2.h264"), sourcePath],
            Assert.Single(runner.Calls).Arguments);
    }

    [Fact]
    public async Task ExtractAsync_PartialFailure_DoesNotReportCompletion()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", "");
        runner.AddResult(1, "", "second track failed");
        var source = new MediaFileInfo(@"C:\media\movie.mp4", "movie.mp4", ExtractorFeatures.Tracks,
            [new TrackInfo(1, TrackType.Video, "avc1", "Video", "und", new() { ["CodecId"] = "avc1" }),
             new TrackInfo(2, TrackType.Audio, "mp4a", "Audio", "und", new() { ["CodecId"] = "mp4a" })], [], [], []);
        var request = new ExtractRequest(source, Path.Combine(Path.GetTempPath(), "mp4-output"), [1, 2], [], false, false, false, false, false);
        var progress = new List<ExtractionProgress>();

        var outcome = await new Mp4ExtractorPlugin(new TestNativeToolResolver(), runner)
            .ExtractAsync(request, new Progress<ExtractionProgress>(progress.Add));

        Assert.False(outcome.Succeeded);
        Assert.Contains(outcome.Failures, failure => failure.Contains("second track failed"));
        Assert.DoesNotContain(progress, item => item.IsComplete);
    }

    [Fact]
    public async Task ExtractAsync_ReportsItemProgressOnlyAfterExtractionCompletes()
    {
        var extractionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowExtraction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeProcessRunner();
        runner.AddHandler(async (_, _) =>
        {
            extractionStarted.SetResult();
            await allowExtraction.Task;
            return new ProcessResult(0, "", "");
        });
        var source = new MediaFileInfo("movie.mp4", "movie.mp4", ExtractorFeatures.Tracks,
            [new TrackInfo(1, TrackType.Video, "avc1", "Video", "und", new() { ["CodecId"] = "avc1" })], [], [], []);
        var request = new ExtractRequest(source, Path.GetTempPath(), [1], [], false, false, false, false, false);
        var progress = new List<ExtractionProgress>();

        var extraction = new Mp4ExtractorPlugin(new TestNativeToolResolver(), runner)
            .ExtractAsync(request, new ImmediateProgress(progress.Add));
        await extractionStarted.Task;
        Assert.Empty(progress);
        allowExtraction.SetResult();
        await extraction;
        Assert.Contains(progress, item => item.CurrentItem == "Track 1");
    }

    private sealed class ImmediateProgress(Action<ExtractionProgress> handler) : IProgress<ExtractionProgress>
    {
        public void Report(ExtractionProgress value) => handler(value);
    }
}
