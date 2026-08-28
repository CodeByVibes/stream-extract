using StreamExtract.Cli.Commands;

namespace StreamExtract.Tests;

public sealed class CliParserTests
{
    [Fact]
    public void ParsesInfoAndMultipleFileExtraction()
    {
        var info = CliParser.Parse(["info", "movie.mkv"]);
        var extract = CliParser.Parse(["extract", "a.mkv", "b.mp4", "--tracks", "1,2", "--chapters", "--output", "out", "--verbose"]);

        Assert.Equal(CliCommand.Info, info.Command);
        Assert.Equal(["movie.mkv"], info.InputFiles);
        Assert.Equal(CliCommand.Extract, extract.Command);
        Assert.Equal(["a.mkv", "b.mp4"], extract.InputFiles);
        Assert.Equal([1, 2], extract.TrackIds);
        Assert.True(extract.ExtractChapters);
        Assert.Equal("out", extract.OutputDirectory);
        Assert.True(extract.Verbose);
    }

    [Fact]
    public void ParsesEveryExtractionModeAndAll()
    {
        var result = CliParser.Parse(["extract", "x.mkv", "--all"]);
        var modes = CliParser.Parse(["extract", "x.mkv", "--attachments", "--tags", "--cue-sheets", "--cues-for-selected-tracks", "--timestamps"]);

        Assert.True(result.All);
        Assert.False(result.ExtractAttachments);
        Assert.True(modes.ExtractAttachments);
        Assert.True(modes.ExtractTags);
        Assert.True(modes.ExtractCueSheets);
        Assert.True(modes.ExtractCuesForSelectedTracks);
        Assert.True(modes.ExtractTimestamps);
    }

    [Theory]
    [InlineData("--tracks", "")]
    [InlineData("--tracks", "1,,2")]
    [InlineData("--tracks", "-1")]
    [InlineData("--output")]
    [InlineData("--unknown")]
    public void RejectsInvalidOptions(params string[] tail)
        => Assert.Equal(CliParseFailure.Usage, CliParser.TryParse(["extract", "x.mkv", .. tail]).Failure);

    [Fact]
    public void SupportsHelpAndVersionWithoutInputs()
    {
        Assert.Equal(CliCommand.Help, CliParser.Parse(["--help"]).Command);
        Assert.Equal(CliCommand.Version, CliParser.Parse(["--version"]).Command);
    }
}
