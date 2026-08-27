using StreamExtract.Plugins;

namespace StreamExtract.Tests;

/// <summary>
/// Tests for the client-side per-track CUE generation ("Cues for selected tracks").
/// mkvextract's own cuesheet mode cannot produce per-track cues and drops chapter
/// names/times for chapters not imported from a CUE sheet, so the cue is generated
/// from the chapter XML instead.
/// </summary>
public class CueSheetTests
{
    private const string SampleChapterXml = """
        <?xml version="1.0"?>
        <Chapters>
          <EditionEntry>
            <ChapterAtom>
              <ChapterTimeStart>00:00:00.000000000</ChapterTimeStart>
              <ChapterDisplay>
                <ChapterString>Intro</ChapterString>
              </ChapterDisplay>
            </ChapterAtom>
            <ChapterAtom>
              <ChapterTimeStart>00:00:05.000000000</ChapterTimeStart>
              <ChapterDisplay>
                <ChapterString>Part Two</ChapterString>
              </ChapterDisplay>
            </ChapterAtom>
            <ChapterAtom>
              <ChapterTimeStart>00:01:00.500000000</ChapterTimeStart>
              <ChapterDisplay>
                <ChapterString>Part Three</ChapterString>
              </ChapterDisplay>
            </ChapterAtom>
          </EditionEntry>
        </Chapters>
        """;

    [Fact]
    public void ParseChapterXml_ExtractsNamesAndStartTimes()
    {
        var chapters = MkvExtractorPlugin.ParseChapterXml(SampleChapterXml);

        Assert.Equal(3, chapters.Count);
        Assert.Equal("Intro", chapters[0].Name);
        Assert.Equal(TimeSpan.Zero, chapters[0].Start);
        Assert.Equal("Part Two", chapters[1].Name);
        Assert.Equal(TimeSpan.FromSeconds(5), chapters[1].Start);
        Assert.Equal("Part Three", chapters[2].Name);
        Assert.Equal(TimeSpan.FromSeconds(60.5), chapters[2].Start);
    }

    [Fact]
    public void ParseChapterXml_NoChapters_ReturnsEmpty()
    {
        var chapters = MkvExtractorPlugin.ParseChapterXml("<Chapters/>");

        Assert.Empty(chapters);
    }

    [Fact]
    public void BuildPerTrackCue_EmitsFileTrackTitleIndex()
    {
        var chapters = MkvExtractorPlugin.ParseChapterXml(SampleChapterXml);
        var cue = MkvExtractorPlugin.BuildPerTrackCue("movie_Track1.h264", chapters);

        Assert.Contains("FILE \"movie_Track1.h264\" WAVE", cue);
        Assert.Contains("  TRACK 01 AUDIO", cue);
        Assert.Contains("    TITLE \"Intro\"", cue);
        Assert.Contains("    INDEX 01 00:00:00", cue);
        Assert.Contains("  TRACK 03 AUDIO", cue);
        Assert.Contains("    INDEX 01 01:00:38", cue); // CUE uses total minutes and 75 frames per second.
    }

    [Fact]
    public void BuildPerTrackCue_EmptyTitle_OmitsTitleLine()
    {
        var cue = MkvExtractorPlugin.BuildPerTrackCue("t.aac", [("", TimeSpan.Zero)]);

        Assert.Contains("  TRACK 01 AUDIO", cue);
        Assert.DoesNotContain("TITLE", cue);
    }

    [Fact]
    public void BuildPerTrackCue_EscapesQuotesAndNewlinesInTitle()
    {
        var cue = MkvExtractorPlugin.BuildPerTrackCue("t.aac", [("He said \"hi\"\nline2", TimeSpan.Zero)]);

        Assert.Contains("TITLE \"He said 'hi' line2\"", cue);
    }

    [Theory]
    [InlineData("00:00:00.000000000", "00:00:00")]
    [InlineData("00:00:05.000000000", "00:05:00")]
    [InlineData("00:01:00.500000000", "01:00:38")]
    [InlineData("00:00:59.700000000", "00:59:53")]
    [InlineData("01:02:03.000000000", "62:03:00")]
    public void FormatCueTime_RoundsToNearestSecond(string raw, string expected)
    {
        var chapters = MkvExtractorPlugin.ParseChapterXml(
            $"<Chapters><ChapterAtom><ChapterTimeStart>{raw}</ChapterTimeStart></ChapterAtom></Chapters>");

        Assert.Equal(expected, MkvExtractorPlugin.FormatCueTime(chapters[0].Start));
    }

    [Fact]
    public void BuildModes_IncludesCuesForSelectedTracks_WhenFlaggedWithTracks()
    {
        var plugin = new MkvExtractorPlugin(@"C:\tools", new FakeProcessRunner());
        var req = new StreamExtract.Models.ExtractRequest(
            MakeInfo(), @"D:\out", [0], [], false, false, false, false, true);

        var (modes, failures) = plugin.BuildModes(req, new Progress<StreamExtract.Models.ExtractionProgress>());

        Assert.Contains(modes, m => m.Name == "cues for selected tracks");
        Assert.Empty(failures);
    }

    [Fact]
    public void BuildModes_OmitsCuesForSelectedTracks_WhenNoTracksSelected()
    {
        var plugin = new MkvExtractorPlugin(@"C:\tools", new FakeProcessRunner());
        var req = new StreamExtract.Models.ExtractRequest(
            MakeInfo(), @"D:\out", [], [], false, false, false, false, true);

        var (modes, _) = plugin.BuildModes(req, new Progress<StreamExtract.Models.ExtractionProgress>());

        Assert.DoesNotContain(modes, m => m.Name == "cues for selected tracks");
    }

    private static StreamExtract.Models.MediaFileInfo MakeInfo() => new(
        @"C:\media\movie.mkv", "movie.mkv",
        StreamExtract.Models.ExtractorFeatures.Tracks | StreamExtract.Models.ExtractorFeatures.CueSheets,
        [new StreamExtract.Models.TrackInfo(0, StreamExtract.Models.TrackType.Video,
            "V_MPEG4/ISO/AVC", "Main", "eng", new() { ["CodecId"] = "V_MPEG4/ISO/AVC" })],
        [], [], []);
}
