using System.Text.RegularExpressions;
using StreamExtract.Models;
using StreamExtract.Services;

namespace StreamExtract.Tests;

/// <summary>
/// Integration tests that exercise the real <see cref="ProcessRunner"/> against cmd.exe.
/// These cover the process layer the fake-based unit tests cannot: real stdout/stderr
/// capture, exit-code handling, progress parsing, and the stdout-error fallback
/// (mkvextract writes both progress and error text to stdout).
/// </summary>
public class ProcessRunnerTests
{
    private static readonly ProcessRunner Runner = new(Environment.SystemDirectory);

    private static string[] Cmd(string command) => ["/c", command];

    private sealed class CollectingProgress : IProgress<ExtractionProgress>
    {
        public List<ExtractionProgress> Items { get; } = [];

        public void Report(ExtractionProgress value) => Items.Add(value);
    }

    private static ExtractionProgress? PercentParser(string line)
    {
        var m = Regex.Match(line, @"(\d+)%");
        return m.Success && int.TryParse(m.Groups[1].Value, out var pct)
            ? new ExtractionProgress("", "", pct, $"Extracting... {pct}%", false)
            : null;
    }

    [Fact]
    public async Task RunWithProgressAsync_ReportsParsedProgressFromStdout()
    {
        var progress = new CollectingProgress();
        await Runner.RunWithProgressAsync("cmd.exe", Cmd("echo Progress: 42% & exit /b 0"), PercentParser, progress);

        Assert.Contains(progress.Items, p => p.Percentage == 42);
    }

    [Fact]
    public async Task RunWithProgressAsync_NonZeroExit_UsesStdoutErrorWhenStderrEmpty()
    {
        var ex = await Assert.ThrowsAsync<ExternalToolException>(
            () => Runner.RunWithProgressAsync("cmd.exe", Cmd("echo Error: boom & exit /b 2"), _ => null, new CollectingProgress()));

        Assert.Equal(2, ex.ExitCode);
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public async Task RunWithProgressAsync_ZeroExit_DoesNotThrow()
    {
        await Runner.RunWithProgressAsync("cmd.exe", Cmd("echo ok & exit /b 0"), _ => null, new CollectingProgress());
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_UsesStdoutWhenStderrEmpty()
    {
        var ex = await Assert.ThrowsAsync<ExternalToolException>(
            () => Runner.RunAsync("cmd.exe", Cmd("echo oh no & exit /b 3")));

        Assert.Equal(3, ex.ExitCode);
        Assert.Contains("oh no", ex.Message);
    }

    [Fact]
    public async Task RunAsync_ZeroExit_ReturnsStdout()
    {
        var result = await Runner.RunAsync("cmd.exe", Cmd("echo hello"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_PreCancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Runner.RunAsync("cmd.exe", Cmd("echo x"), cts.Token));
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsHungProcess()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // ping -n 60 would run ~60s; the 1s timeout must abort it well before then.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Runner.RunAsync("cmd.exe", Cmd("ping -n 60 127.0.0.1 > nul"), default, null, TimeSpan.FromSeconds(1)));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"Timeout took too long: {sw.Elapsed}");
    }
}
