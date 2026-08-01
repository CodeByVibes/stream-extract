using StreamExtract.Models;
using StreamExtract.Services;

namespace StreamExtract.Tests;

public class PluginFailureContractTests
{
    private static ExtractRequest MakeRequest(bool attachments = false)
    {
        var info = new MediaFileInfo(
            @"C:\media\movie.mkv", "movie.mkv",
            ExtractorFeatures.Tracks | ExtractorFeatures.Attachments,
            [new TrackInfo(0, TrackType.Video, "V_MPEG4/ISO/AVC", "Main", "eng",
                new() { ["CodecId"] = "V_MPEG4/ISO/AVC" })],
            [], attachments ? [new AttachmentInfo(1, "font.ttf", "font/ttf", 1000)] : [], []);
        return new ExtractRequest(info, @"D:\out", [0], [], attachments, false, false, false);
    }

    [Fact]
    public async Task MkvExtractAsync_NonZeroExit_ThrowsExternalToolException()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(2, "progress 50%", "boom");
        var plugin = new Plugins.MkvExtractorPlugin(@"C:\tools", runner);

        var ex = await Assert.ThrowsAsync<ExternalToolException>(
            () => plugin.ExtractAsync(MakeRequest(), new Progress<ExtractionProgress>()));
        Assert.Equal(2, ex.ExitCode);
        Assert.Contains("boom", ex.StandardError);
    }

    [Fact]
    public async Task Mp4ExtractAsync_NonZeroExit_ThrowsExternalToolException()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(1, "", "mp4box exploded");
        var plugin = new Plugins.Mp4ExtractorPlugin(@"C:\tools", runner);

        var ex = await Assert.ThrowsAsync<ExternalToolException>(
            () => plugin.ExtractAsync(MakeRequest(), new Progress<ExtractionProgress>()));
        Assert.Equal(1, ex.ExitCode);
        Assert.Contains("mp4box exploded", ex.StandardError);
    }

    [Fact]
    public async Task MkvExtractAsync_ZeroExit_Succeeds()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "progress 100%", "");
        var plugin = new Plugins.MkvExtractorPlugin(@"C:\tools", runner);

        await plugin.ExtractAsync(MakeRequest(), new Progress<ExtractionProgress>());
    }

    [Fact]
    public async Task Mp4ExtractAsync_ZeroExit_Succeeds()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", "");
        var plugin = new Plugins.Mp4ExtractorPlugin(@"C:\tools", runner);

        await plugin.ExtractAsync(MakeRequest(), new Progress<ExtractionProgress>());
    }

    [Fact]
    public async Task MkvExtractAsync_PreCancelledToken_ThrowsOperationCanceled()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "progress 50%", "");
        var plugin = new Plugins.MkvExtractorPlugin(@"C:\tools", runner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => plugin.ExtractAsync(MakeRequest(), new Progress<ExtractionProgress>(), cts.Token));
    }

    [Fact]
    public async Task Mp4ExtractAsync_PreCancelledToken_ThrowsOperationCanceled()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", "");
        var plugin = new Plugins.Mp4ExtractorPlugin(@"C:\tools", runner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => plugin.ExtractAsync(MakeRequest(), new Progress<ExtractionProgress>(), cts.Token));
    }
}
