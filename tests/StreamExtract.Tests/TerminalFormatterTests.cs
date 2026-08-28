using StreamExtract.Cli.Formatting;
using StreamExtract.Models;

namespace StreamExtract.Tests;

public sealed class TerminalFormatterTests
{
    [Fact]
    public void FormatsMediaInformationInStableSections()
    {
        var info = new MediaFileInfo("/x/movie.mkv", "movie.mkv", ExtractorFeatures.Tracks | ExtractorFeatures.Chapters,
            [new TrackInfo(1, TrackType.Audio, "AAC", "Commentary", "en", new() { ["Duration"] = "00:01:02.000" })],
            [new ChapterInfo(0, "Intro", "en")], [], [new TagInfo(-1, "TITLE", "Movie")]);

        var text = TerminalFormatter.FormatMediaInfo(info);

        Assert.Contains("File: movie.mkv", text);
        Assert.Contains("Tracks:", text);
        Assert.Contains("1  Audio  AAC  Commentary [en]", text);
        Assert.Contains("Chapters:\n  0  Intro [en]", text);
        Assert.Contains("Tags:\n  TITLE = Movie", text);
    }
}
