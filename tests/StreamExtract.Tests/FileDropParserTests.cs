using StreamExtract.Services;

namespace StreamExtract.Tests;

public sealed class FileDropParserTests
{
    [Fact]
    public void ParsesFileUriListAndPlainPaths()
    {
        var result = FileDropParser.ParseUriList("# comment\nfile:///home/johan/video.mkv\n/home/johan/other.mp4\n");

        Assert.Equal(["/home/johan/video.mkv", "/home/johan/other.mp4"], result);
    }

    [Fact]
    public void DecodesFileUriAndIgnoresNonFileUris()
    {
        var result = FileDropParser.ParseUriList("file:///home/johan/My%20Video.mkv\nhttps://example.com/video.mkv\n");

        Assert.Equal(["/home/johan/My Video.mkv"], result);
    }

    [Fact]
    public void RemovesDuplicatePaths()
    {
        var result = FileDropParser.ParseUriList("file:///tmp/video.mkv\n/tmp/video.mkv\n");

        Assert.Single(result);
    }

    [Fact]
    public void IgnoresGnomeCopiedFilesHeadersAndNonPathTokens()
    {
        var result = FileDropParser.ParseUriList("x-special/gnome-copied-files\r\ncopy\r\nfile:///home/johan/movie.mp4\r\n");

        Assert.Equal(["/home/johan/movie.mp4"], result);
    }

    /// <summary>
    /// Deduplication must follow platform path semantics: Windows is case-insensitive, so
    /// differently-cased spellings of the same path are one entry, while Unix keeps them distinct.
    /// </summary>
    [Fact]
    public void DeduplicatesPathsUsingPlatformCaseSensitivity()
    {
        var result = FileDropParser.ParseUriList("/media/Movie.mkv\n/media/movie.mkv\n");

        if (OperatingSystem.IsWindows())
            Assert.Single(result);
        else
            Assert.Equal(2, result.Count);
    }

    [Fact]
    public void PathComparer_MatchesPlatformPathSemantics()
    {
        var comparer = PathComparer.Default;

        Assert.Equal(
            OperatingSystem.IsWindows(),
            comparer.Equals("/media/Movie.mkv", "/media/movie.mkv"));
    }
}
