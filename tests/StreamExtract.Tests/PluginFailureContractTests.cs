using StreamExtract.Models;
using StreamExtract.Services;

namespace StreamExtract.Tests;

/// <summary>
/// Failure contract: a failing native tool does not abort the remaining extraction —
/// failures are collected per mode/item into <see cref="ExtractOutcome"/> — while
/// cancellation always propagates.
/// </summary>
public class PluginFailureContractTests
{
    private static ExtractRequest MakeRequest(bool attachments = false, bool chapters = false)
    {
        var info = new MediaFileInfo(
            @"C:\media\movie.mkv", "movie.mkv",
            ExtractorFeatures.Tracks | ExtractorFeatures.Attachments | ExtractorFeatures.Chapters,
            [new TrackInfo(0, TrackType.Video, "V_MPEG4/ISO/AVC", "Main", "eng",
                new() { ["CodecId"] = "V_MPEG4/ISO/AVC" })],
            chapters ? [new ChapterInfo(0, "C1", "")] : [],
            attachments ? [new AttachmentInfo(1, "font.ttf", "font/ttf", 1000)] : [], []);
        return new ExtractRequest(info, @"D:\out", [0], chapters ? [0] : [], attachments, false, false, false, false);
    }

    [Fact]
    public async Task MkvExtractAsync_NonZeroExit_ReturnsFailedOutcome()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(2, "progress 50%", "boom");
        var plugin = new Plugins.MkvExtractorPlugin(new TestNativeToolResolver(), runner);

        var outcome = await plugin.ExtractAsync(MakeRequest(), new Progress<ExtractionProgress>());

        Assert.False(outcome.Succeeded);
        Assert.Contains(outcome.Failures, f => f.Contains("boom"));
    }

    [Fact]
    public async Task Mp4ExtractAsync_NonZeroExit_ReturnsFailedOutcome()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(1, "", "mp4box exploded");
        var plugin = new Plugins.Mp4ExtractorPlugin(new TestNativeToolResolver(), runner);

        var outcome = await plugin.ExtractAsync(MakeRequest(), new Progress<ExtractionProgress>());

        Assert.False(outcome.Succeeded);
        Assert.Contains(outcome.Failures, f => f.Contains("mp4box exploded"));
    }

    [Fact]
    public async Task MkvExtractAsync_ZeroExit_Succeeds()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "progress 100%", "");
        var plugin = new Plugins.MkvExtractorPlugin(new TestNativeToolResolver(), runner);

        var outcome = await plugin.ExtractAsync(MakeRequest(), new Progress<ExtractionProgress>());

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Failures);
    }

    [Fact]
    public async Task Mp4ExtractAsync_ZeroExit_Succeeds()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "", "");
        var plugin = new Plugins.Mp4ExtractorPlugin(new TestNativeToolResolver(), runner);

        var outcome = await plugin.ExtractAsync(MakeRequest(), new Progress<ExtractionProgress>());

        Assert.True(outcome.Succeeded);
    }

    [Fact]
    public async Task MkvExtractAsync_FailingMode_DoesNotAbortRemainingModes()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(2, "", "tracks boom");    // tracks mode fails
        runner.AddResult(0, "progress 100%", "");  // chapters mode still runs
        var plugin = new Plugins.MkvExtractorPlugin(new TestNativeToolResolver(), runner);

        var outcome = await plugin.ExtractAsync(MakeRequest(chapters: true), new Progress<ExtractionProgress>());

        Assert.False(outcome.Succeeded);
        Assert.Contains(outcome.Failures, f => f.Contains("tracks"));
        Assert.Equal(2, runner.RunCount); // both modes attempted
    }

    [Fact]
    public async Task MkvExtractAsync_MultipleFailures_AllCollected()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(2, "", "first boom");
        runner.AddResult(2, "", "second boom");
        var plugin = new Plugins.MkvExtractorPlugin(new TestNativeToolResolver(), runner);

        var outcome = await plugin.ExtractAsync(MakeRequest(chapters: true), new Progress<ExtractionProgress>());

        Assert.False(outcome.Succeeded);
        Assert.Equal(2, outcome.Failures.Count);
    }

    [Fact]
    public async Task MkvExtractAsync_InvalidAttachmentName_SkippedAndRemainingExtracted()
    {
        var info = new MediaFileInfo(
            @"C:\media\movie.mkv", "movie.mkv",
            ExtractorFeatures.Tracks | ExtractorFeatures.Attachments,
            [new TrackInfo(0, TrackType.Video, "V_MPEG4/ISO/AVC", "Main", "eng",
                new() { ["CodecId"] = "V_MPEG4/ISO/AVC" })],
            [], [new AttachmentInfo(1, "CON", "font/ttf", 1000)], []);
        var req = new ExtractRequest(info, @"D:\out", [0], [], true, false, false, false, false);

        var runner = new FakeProcessRunner();
        runner.AddResult(0, "progress 100%", ""); // tracks mode runs; attachments mode is skipped
        var plugin = new Plugins.MkvExtractorPlugin(new TestNativeToolResolver(), runner);

        var outcome = await plugin.ExtractAsync(req, new Progress<ExtractionProgress>());

        Assert.False(outcome.Succeeded);
        Assert.Contains(outcome.Failures, f => f.Contains("attachments"));
        Assert.Equal(1, runner.RunCount);
    }

    [Fact]
    public async Task MkvExtractAsync_PreCancelledToken_ThrowsOperationCanceled()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "progress 50%", "");
        var plugin = new Plugins.MkvExtractorPlugin(new TestNativeToolResolver(), runner);
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
        var plugin = new Plugins.Mp4ExtractorPlugin(new TestNativeToolResolver(), runner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => plugin.ExtractAsync(MakeRequest(), new Progress<ExtractionProgress>(), cts.Token));
    }

    [Fact]
    public async Task MkvExtractAsync_PropagatesCallerCancellationTokenToRunner()
    {
        var runner = new FakeProcessRunner();
        runner.AddHandler((_, _) => Task.FromResult(new ProcessResult(0, "", "")));
        using var cts = new CancellationTokenSource();
        var plugin = new Plugins.MkvExtractorPlugin(new TestNativeToolResolver(), runner);

        await plugin.ExtractAsync(MakeRequest(), new Progress<ExtractionProgress>(), cts.Token);

        Assert.Equal(cts.Token, Assert.Single(runner.Calls).CancellationToken);
    }

    [Fact]
    public async Task Mp4ExtractAsync_PropagatesCallerCancellationTokenToRunner()
    {
        var runner = new FakeProcessRunner();
        runner.AddHandler((_, _) => Task.FromResult(new ProcessResult(0, "", "")));
        using var cts = new CancellationTokenSource();
        var plugin = new Plugins.Mp4ExtractorPlugin(new TestNativeToolResolver(), runner);

        await plugin.ExtractAsync(MakeRequest(), new Progress<ExtractionProgress>(), cts.Token);

        Assert.Equal(cts.Token, Assert.Single(runner.Calls).CancellationToken);
    }

    [Fact]
    public async Task FakeProcessRunner_RunWithProgressAsync_PreCancelledTokenSkipsHandler()
    {
        var runner = new FakeProcessRunner();
        var invoked = false;
        runner.AddHandler((_, _) =>
        {
            invoked = true;
            return Task.FromResult(new ProcessResult(0, "", ""));
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunWithProgressAsync(
            "tool", [], _ => null, new Progress<ExtractionProgress>(), cts.Token));

        Assert.False(invoked);
    }
}
