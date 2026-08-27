using StreamExtract.Models;
using StreamExtract.Plugins;
using StreamExtract.Services;

namespace StreamExtract.Tests;

/// <summary>
/// Tests for <see cref="MkvExtractorPlugin.AnalyzeFileAsync"/>, which parses the
/// mkvmerge JSON identification output. The main fixture is the real output captured
/// from the bundled mkvmerge on a real file.
/// </summary>
public class MkvExtractorPluginAnalyzeTests
{
    // Real `mkvmerge -i -F json` output for an MP4 (QuickTime container) with a video
    // and an audio track. Note: MP4 inputs carry no container duration — that is a
    // known handled case (Duration defaults to empty).
    private const string RealJson = """
        {
          "attachments": [],
          "chapters": [],
          "container": {
            "properties": {
              "container_type": 25,
              "is_providing_timestamps": true
            },
            "recognized": true,
            "supported": true,
            "type": "QuickTime/MP4"
          },
          "errors": [],
          "file_name": "D:/media/video.mp4",
          "global_tags": [
            {
              "num_entries": 1
            }
          ],
          "identification_format_version": 20,
          "track_tags": [],
          "tracks": [
            {
              "codec": "AVC/H.264/MPEG-4p10",
              "id": 0,
              "properties": {
                "color_matrix_coefficients": 1,
                "color_primaries": 1,
                "color_transfer_characteristics": 1,
                "enabled_track": true,
                "language": "und",
                "number": 1,
                "packetizer": "mpeg4_p10_video",
                "pixel_dimensions": "640x480"
              },
              "type": "video"
            },
            {
              "codec": "AAC",
              "id": 1,
              "properties": {
                "audio_bits_per_sample": 16,
                "audio_channels": 2,
                "audio_sampling_frequency": 48000,
                "enabled_track": true,
                "language": "und",
                "number": 2
              },
              "type": "audio"
            }
          ],
          "warnings": []
        }
        """;

    private static MkvExtractorPlugin Plugin(FakeProcessRunner runner) => new(new TestNativeToolResolver(), runner);

    private static async Task<MediaFileInfo> AnalyzeAsync(string json)
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, json, "");
        return await Plugin(runner).AnalyzeFileAsync(@"C:\media\movie.mkv");
    }

    [Fact]
    public async Task AnalyzeFileAsync_ParsesRealMkvmergeJson()
    {
        var info = await AnalyzeAsync(RealJson);

        Assert.Equal(2, info.Tracks.Count);
        Assert.Equal(0, info.Tracks[0].Id);
        Assert.Equal(TrackType.Video, info.Tracks[0].Type);
        Assert.Equal("AVC/H.264/MPEG-4p10", info.Tracks[0].Codec);
        Assert.Equal("640x480", info.Tracks[0].Properties["PixelDimensions"]);
        Assert.Equal(1, info.Tracks[1].Id);
        Assert.Equal(TrackType.Audio, info.Tracks[1].Type);
        Assert.Equal("AAC", info.Tracks[1].Codec);
        Assert.Equal("und", info.Tracks[1].Language);

        // Global tags are surfaced as a single tag entry; no chapters/attachments here.
        Assert.Single(info.Tags);
        Assert.Equal("Global", info.Tags[0].Name);
        Assert.Empty(info.Chapters);
        Assert.Empty(info.Attachments);
    }

    [Fact]
    public async Task AnalyzeFileAsync_UsesResolverExecutablePath()
    {
        var resolver = new TestNativeToolResolver();
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "{}", "");

        await new MkvExtractorPlugin(resolver, runner).AnalyzeFileAsync(@"C:\media\movie.mkv");

        var call = Assert.Single(runner.Calls);
        Assert.Equal(resolver.Resolve(NativeToolId.MkvMerge), call.FileName);
        Assert.True(Path.IsPathFullyQualified(call.FileName));
        Assert.Equal([@"C:\media\movie.mkv", "-i", "-F", "json"], call.Arguments);
        Assert.Null(call.WorkingDirectory);
        Assert.Equal(default, call.CancellationToken);
        Assert.Null(call.Timeout);
    }

    [Fact]
    public async Task AnalyzeFileAsync_NoContainerDuration_ProducesEmptyDuration()
    {
        var info = await AnalyzeAsync(RealJson);

        Assert.Equal("", info.Tracks[0].Properties["Duration"]);
    }

    [Fact]
    public async Task AnalyzeFileAsync_ContainerDuration_IsFormatted()
    {
        // 90_000_000_000 ns = 90 s -> 00:01:30.000
        var json = RealJson.Replace("\"is_providing_timestamps\": true",
            "\"is_providing_timestamps\": true,\n      \"duration\": 90000000000");

        var info = await AnalyzeAsync(json);

        Assert.Equal("00:01:30.000", info.Tracks[0].Properties["Duration"]);
    }

    [Fact]
    public async Task AnalyzeFileAsync_ChaptersAttachmentsAndTrackTags_AreParsed()
    {
        var json = RealJson
            .Replace("""" "chapters": [],"""", "\"chapters\": [{\"num_entries\": 3}],")
            .Replace("""" "attachments": [],"""", """
                "attachments": [
                  { "id": 1, "file_name": "cover.jpg", "content_type": "image/jpeg", "size": 12345 }
                ],
                """)
            .Replace("""" "track_tags": [],"""", "\"track_tags\": [{\"num_entries\": 2, \"track_id\": 0}],");

        var info = await AnalyzeAsync(json);

        var chapter = Assert.Single(info.Chapters);
        Assert.Equal(0, chapter.Id);
        Assert.Equal("Chapters (3 entries)", chapter.Name);

        var attachment = Assert.Single(info.Attachments);
        Assert.Equal(1L, attachment.Id);
        Assert.Equal("cover.jpg", attachment.FileName);
        Assert.Equal("image/jpeg", attachment.MimeType);
        Assert.Equal(12345L, attachment.Size);

        Assert.Equal(2, info.Tags.Count);
        Assert.Equal(-1, info.Tags[0].TargetId);
        Assert.Equal("Global", info.Tags[0].Name);
        Assert.Equal(0, info.Tags[1].TargetId);
        Assert.Equal("Track 0", info.Tags[1].Name);
        Assert.Equal("2 entries", info.Tags[1].Value);
    }

    [Fact]
    public async Task AnalyzeFileAsync_SubtitleTrackType_IsMapped()
    {
        var json = RealJson.Replace("\"type\": \"audio\"", "\"type\": \"subtitles\"");
        var info = await AnalyzeAsync(json);

        Assert.Equal(TrackType.Subtitle, info.Tracks[1].Type);
    }

    [Fact]
    public async Task AnalyzeFileAsync_MalformedJson_ThrowsJsonException()
    {
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => AnalyzeAsync("this is not json"));
    }

    [Fact]
    public async Task AnalyzeFileAsync_NullJson_ThrowsInvalidOperation()
    {
        // Valid JSON that deserializes to null hits the explicit guard in the plugin.
        await Assert.ThrowsAsync<InvalidOperationException>(() => AnalyzeAsync("null"));
    }

    [Fact]
    public async Task AnalyzeFileAsync_EmptyTracks_YieldsNoTracks()
    {
        var json = """
            {
              "attachments": [],
              "chapters": [],
              "container": { "properties": {}, "recognized": true, "supported": true, "type": "Matroska" },
              "global_tags": [],
              "track_tags": [],
              "tracks": [],
              "warnings": []
            }
            """;

        var info = await AnalyzeAsync(json);

        Assert.Empty(info.Tracks);
        Assert.Empty(info.Chapters);
        Assert.Empty(info.Attachments);
        Assert.Empty(info.Tags);
    }

    [Fact]
    public async Task AnalyzeFileAsync_NonZeroExit_ThrowsExternalToolException()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(1, "", "mkvmerge exploded");
        var plugin = Plugin(runner);

        var ex = await Assert.ThrowsAsync<ExternalToolException>(
            () => plugin.AnalyzeFileAsync(@"C:\media\movie.mkv"));

        Assert.Contains("mkvmerge exploded", ex.Message);
    }
}
