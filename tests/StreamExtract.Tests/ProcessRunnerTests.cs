using System.Text.RegularExpressions;
using StreamExtract.Models;
using StreamExtract.Services;

namespace StreamExtract.Tests;

/// <summary>
/// Integration tests that exercise the real <see cref="ProcessRunner"/> against a tiny
/// cross-platform process fixture.
/// These cover the process layer the fake-based unit tests cannot: real stdout/stderr
/// capture, exit-code handling, progress parsing, and the stdout-error fallback
/// (mkvextract writes both progress and error text to stdout).
/// </summary>
public class ProcessRunnerTests
{
    private static readonly string HostPath = Path.Combine(AppContext.BaseDirectory, "TestProcessHost.dll");
    private const string DotnetPath = "dotnet";
    private static readonly ProcessRunner Runner = new("");

    private static string[] HostArgs(params string[] arguments)
        => [HostPath, .. arguments];

    private static string[] Emit(string? stdout = null, string? stderr = null, int? exitCode = null,
        int? sleepMilliseconds = null, string? readyFile = null, string? spawnChildReadyFile = null,
        string? childPidFile = null)
    {
        var args = new List<string>();
        if (stdout is not null) args.AddRange(["--stdout", stdout]);
        if (stderr is not null) args.AddRange(["--stderr", stderr]);
        if (exitCode is not null) args.AddRange(["--exit-code", exitCode.Value.ToString()]);
        if (sleepMilliseconds is not null) args.AddRange(["--sleep-ms", sleepMilliseconds.Value.ToString()]);
        if (readyFile is not null) args.AddRange(["--ready-file", readyFile]);
        if (spawnChildReadyFile is not null) args.AddRange(["--spawn-child", spawnChildReadyFile]);
        if (childPidFile is not null) args.AddRange(["--child-pid-file", childPidFile]);
        return HostArgs([.. args]);
    }

    private static async Task WaitForFileAsync(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(File.Exists(path), $"Timed out waiting for {path}");
    }

    private static async Task WaitForProcessExitAsync(int pid)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                if (process.HasExited) return;
            }
            catch (ArgumentException) { return; }
            await Task.Delay(10);
        }
        Assert.Fail($"Process {pid} did not exit");
    }

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
        await Runner.RunWithProgressAsync(DotnetPath, Emit("Progress: 42%\n"), PercentParser, progress);

        Assert.Contains(progress.Items, p => p.Percentage == 42);
    }

    [Fact]
    public async Task RunWithProgressAsync_NonZeroExit_UsesStdoutErrorWhenStderrEmpty()
    {
        var ex = await Assert.ThrowsAsync<ExternalToolException>(
            () => Runner.RunWithProgressAsync(DotnetPath, Emit("Error: boom\n", exitCode: 2), _ => null, new CollectingProgress()));

        Assert.Equal(2, ex.ExitCode);
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public async Task RunWithProgressAsync_ZeroExit_DoesNotThrow()
    {
        await Runner.RunWithProgressAsync(DotnetPath, Emit("ok\n"), _ => null, new CollectingProgress());
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_UsesStdoutWhenStderrEmpty()
    {
        var ex = await Assert.ThrowsAsync<ExternalToolException>(
            () => Runner.RunAsync(DotnetPath, Emit("oh no\n", exitCode: 3)));

        Assert.Equal(3, ex.ExitCode);
        Assert.Contains("oh no", ex.Message);
    }

    [Fact]
    public async Task RunAsync_ZeroExit_ReturnsStdout()
    {
        var result = await Runner.RunAsync(DotnetPath, Emit("hello\n"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_PreCancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Runner.RunAsync(DotnetPath, Emit("x\n"), cts.Token));
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsHungProcess()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Runner.RunAsync(DotnetPath, Emit(sleepMilliseconds: 60_000), default, null, TimeSpan.FromSeconds(1)));
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Timeout took too long: {sw.Elapsed}");
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsActiveFixtureAndDescendant()
    {
        var directory = Directory.CreateTempSubdirectory("stream-extract-timeout-");
        var readyFile = Path.Combine(directory.FullName, "ready");
        var childReadyFile = Path.Combine(directory.FullName, "child-ready");
        var childPidFile = Path.Combine(directory.FullName, "child-pid");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var run = Runner.RunAsync(DotnetPath,
                Emit(sleepMilliseconds: 60_000, readyFile: readyFile,
                    spawnChildReadyFile: childReadyFile, childPidFile: childPidFile),
                default, null, TimeSpan.FromSeconds(1));
            await WaitForFileAsync(readyFile);
            await WaitForFileAsync(childReadyFile);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Timeout took too long: {sw.Elapsed}");
            var childPid = int.Parse(await File.ReadAllTextAsync(childPidFile));
            await WaitForProcessExitAsync(childPid);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task RunAsync_Cancellation_KillsActiveFixtureAndDescendant()
    {
        var directory = Directory.CreateTempSubdirectory("stream-extract-cancel-");
        var readyFile = Path.Combine(directory.FullName, "ready");
        var childReadyFile = Path.Combine(directory.FullName, "child-ready");
        var childPidFile = Path.Combine(directory.FullName, "child-pid");
        using var cts = new CancellationTokenSource();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var run = Runner.RunAsync(DotnetPath,
                Emit(sleepMilliseconds: 60_000, readyFile: readyFile,
                    spawnChildReadyFile: childReadyFile, childPidFile: childPidFile), cts.Token);
            await WaitForFileAsync(readyFile);
            await WaitForFileAsync(childReadyFile);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Cancellation took too long: {sw.Elapsed}");
            await WaitForProcessExitAsync(int.Parse(await File.ReadAllTextAsync(childPidFile)));
        }
        finally { directory.Delete(true); }
    }
}
