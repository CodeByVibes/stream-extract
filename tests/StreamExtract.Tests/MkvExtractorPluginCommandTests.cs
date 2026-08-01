using StreamExtract.Models;
using StreamExtract.Plugins;
using StreamExtract.Services;

namespace StreamExtract.Tests;

public class MkvExtractorPluginCommandTests
{
    private static ExtractRequest MakeRequest(
        bool tracks = true, bool attachments = false, bool tags = false,
        bool cueSheets = false, bool timestamps = false, bool chapters = false)
    {
        var info = new MediaFileInfo(
            @"C:\media\movie.mkv", "movie.mkv",
            ExtractorFeatures.Tracks | ExtractorFeatures.Attachments | ExtractorFeatures.Tags |
            ExtractorFeatures.CueSheets | ExtractorFeatures.Timestamps,
            [new TrackInfo(0, TrackType.Video, "V_MPEG4/ISO/AVC", "Main", "eng",
                new() { ["CodecId"] = "V_MPEG4/ISO/AVC" })],
            chapters ? [new ChapterInfo(0, "C1", "")] : [],
            [new AttachmentInfo(1, "font.ttf", "font/ttf", 1000)],
            []);
        return new ExtractRequest(info, @"D:\out",
            tracks ? [0] : [], chapters ? [0] : [], attachments, tags, cueSheets, timestamps);
    }

    [Fact]
    public void TracksCommand_ContainsTrackMappingWithExtension()
    {
        var req = MakeRequest();
        var args = MkvExtractorPlugin.BuildTracksCommand(req).ToArray();
        Assert.Equal("tracks", args[1]);
        Assert.Contains("0:D:\\out\\movie_Track1.h264", args);
    }

    [Fact]
    public void ChaptersCommand_ContainsXmlOutput()
    {
        var req = MakeRequest(tracks: false, chapters: true);
        var args = MkvExtractorPlugin.BuildChaptersCommand(req).ToArray();
        Assert.Equal("chapters", args[1]);
        Assert.Contains("D:\\out\\movie_chapters.xml", args);
    }

    [Fact]
    public void AttachmentsCommand_UsesContainedPaths()
    {
        var req = MakeRequest(attachments: true);
        var args = MkvExtractorPlugin.BuildAttachmentsCommand(req).ToArray();
        Assert.Equal("attachments", args[1]);
        Assert.Contains("1:D:\\out\\font.ttf", args);
    }

    [Fact]
    public void AttachmentsCommand_TraversalFileName_IsFlattened()
    {
        var info = new MediaFileInfo(
            @"C:\media\movie.mkv", "movie.mkv",
            ExtractorFeatures.Attachments,
            [], [], [new AttachmentInfo(1, @"..\..\evil.ttf", "font/ttf", 1000)], []);
        var req = new ExtractRequest(info, @"D:\out", [], [], true, false, false, false);
        var args = MkvExtractorPlugin.BuildAttachmentsCommand(req).ToArray();
        Assert.Contains("1:D:\\out\\evil.ttf", args);
    }

    [Fact]
    public void AttachmentsCommand_DeviceName_IsRejected()
    {
        var info = new MediaFileInfo(
            @"C:\media\movie.mkv", "movie.mkv",
            ExtractorFeatures.Attachments,
            [], [], [new AttachmentInfo(1, "CON", "font/ttf", 1000)], []);
        var req = new ExtractRequest(info, @"D:\out", [], [], true, false, false, false);
        Assert.Throws<InvalidDataException>(() => MkvExtractorPlugin.BuildAttachmentsCommand(req).ToArray());
    }

    [Fact]
    public void TagsCommand_ContainsTagsMode()
    {
        var req = MakeRequest(tags: true);
        var args = MkvExtractorPlugin.BuildTagsCommand(req).ToArray();
        Assert.Equal("tags", args[1]);
        Assert.Contains("D:\\out\\movie_tags.xml", args);
    }

    [Fact]
    public void CueSheetsCommand_ContainsCueSheetMode()
    {
        var req = MakeRequest(cueSheets: true);
        var args = MkvExtractorPlugin.BuildCueSheetsCommand(req).ToArray();
        Assert.Equal("cuesheet", args[1]);
        Assert.Contains("D:\\out\\movie_cuesheet.cue", args);
    }

    [Fact]
    public void TimestampsCommand_HasOneArgPerSelectedTrack()
    {
        var req = MakeRequest(timestamps: true);
        var args = MkvExtractorPlugin.BuildTimestampsCommand(req).ToArray();
        Assert.Equal("timestamps_v2", args[1]);
        Assert.Contains("0:D:\\out\\movie_Track1_timestamps.txt", args);
    }

    [Fact]
    public void TimestampsCommand_MissingTrackId_IsSkipped()
    {
        var info = new MediaFileInfo(
            @"C:\media\movie.mkv", "movie.mkv",
            ExtractorFeatures.Timestamps,
            [new TrackInfo(0, TrackType.Video, "V_MPEG4/ISO/AVC", "Main", "eng",
                new() { ["CodecId"] = "V_MPEG4/ISO/AVC" })],
            [], [], []);
        var req = new ExtractRequest(info, @"D:\out", [0, 99], [], false, false, false, true);
        var args = MkvExtractorPlugin.BuildTimestampsCommand(req).ToArray();
        Assert.Single(args, a => a.StartsWith("0:"));
        Assert.DoesNotContain(args, a => a.StartsWith("99:"));
    }
}
