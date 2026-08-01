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

    [Fact]
    public async Task AnalyzeFileAsync_ParsesTracksFromStderr()
    {
        // mp4box (GPAC) writes "-info" output to stderr, stdout stays empty.
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", SampleInfo);
        var plugin = new Mp4ExtractorPlugin(@"C:\tools", runner);

        var info = await plugin.AnalyzeFileAsync(@"C:\media\movie.mp4");

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
    public async Task AnalyzeFileAsync_MalformedTrackId_SkipsTrack()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", "# Track 1 Info - ID abc\nMedia Type: vide:avc1\n# Track 2 Info - ID 2\nMedia Type: soun:mp4a\n");
        var plugin = new Mp4ExtractorPlugin(@"C:\tools", runner);

        var info = await plugin.AnalyzeFileAsync(@"C:\media\movie.mp4");

        Assert.Single(info.Tracks);
        Assert.Equal(2, info.Tracks[0].Id);
    }

    [Fact]
    public async Task AnalyzeFileAsync_EmptyOutput_YieldsNoTracks()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", "");
        var plugin = new Mp4ExtractorPlugin(@"C:\tools", runner);

        var info = await plugin.AnalyzeFileAsync(@"C:\media\movie.mp4");

        Assert.Empty(info.Tracks);
    }

    [Fact]
    public async Task AnalyzeFileAsync_InfoOnStdoutOnly_YieldsNoTracks()
    {
        // mp4box writes "-info" to stderr; stdout-only output must not be parsed.
        var runner = new FakeProcessRunner();
        runner.AddResult(0, SampleInfo, "");
        var plugin = new Mp4ExtractorPlugin(@"C:\tools", runner);

        var info = await plugin.AnalyzeFileAsync(@"C:\media\movie.mp4");

        Assert.Empty(info.Tracks);
    }
}
