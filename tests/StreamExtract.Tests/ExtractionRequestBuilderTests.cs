using StreamExtract.Models;
using StreamExtract.Plugins;
using StreamExtract.Services;

namespace StreamExtract.Tests;

public class ExtractionRequestBuilderTests
{
    private static ImportedFile MakeImported()
    {
        var info = new MediaFileInfo(
            Path.Combine(Path.GetTempPath(), "media", "movie.mkv"), "movie.mkv",
            ExtractorFeatures.Tracks | ExtractorFeatures.Attachments,
            [new TrackInfo(0, TrackType.Video, "V_MPEG4/ISO/AVC", "Main", "eng", new())],
            [], [], []);
        return new ImportedFile(info.FilePath, new MkvExtractorPlugin(new TestNativeToolResolver()), info);
    }

    [Fact]
    public void EmptySelection_ReturnsNull()
    {
        var imported = MakeImported();
        var selection = new FileSelection([], false, [], false, false, false, false);
        Assert.Null(ExtractionRequestBuilder.TryBuild(imported, @"C:\out", selection));
    }

    [Fact]
    public void TrackSelection_MapsTrackIdsAndFlags()
    {
        var imported = MakeImported();
        var selection = new FileSelection([0], true, [], true, false, false, false);
        var request = ExtractionRequestBuilder.TryBuild(imported, Path.Combine(Path.GetTempPath(), "out"), selection);

        Assert.NotNull(request);
        Assert.Contains(0, request!.SelectedTrackIds);
        Assert.True(request.ExtractAttachments);
        Assert.True(request.ExtractTags);
        Assert.False(request.ExtractCueSheets);
        Assert.False(request.ExtractTimestamps);
    }

    [Fact]
    public void InvalidOutputDirectory_ReturnsNull()
    {
        var imported = MakeImported();
        var selection = new FileSelection([0], false, [], false, false, false, false);
        Assert.Null(ExtractionRequestBuilder.TryBuild(imported, "   ", selection));
    }

    [Fact]
    public void RelativeOutputDirectory_ReturnsNull()
    {
        var imported = MakeImported();
        var selection = new FileSelection([0], false, [], false, false, false, false);
        Assert.Null(ExtractionRequestBuilder.TryBuild(imported, "out", selection));
    }

    [Fact]
    public void OutputDirectory_IsUsedVerbatim()
    {
        var imported = MakeImported();
        var selection = new FileSelection([0], false, [], false, false, false, false);
        var outputDirectory = Path.Combine(Path.GetTempPath(), "media", "out");
        var request = ExtractionRequestBuilder.TryBuild(imported, outputDirectory, selection);
        Assert.Equal(outputDirectory, request!.OutputDirectory);
        Assert.Same(imported.Info, request.Source);
    }
}
