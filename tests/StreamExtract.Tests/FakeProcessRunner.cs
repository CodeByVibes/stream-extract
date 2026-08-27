using StreamExtract.Models;
using StreamExtract.Services;

namespace StreamExtract.Tests;

public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<(int ExitCode, string StdOut, string StdErr)> _responses = new();
    private readonly Queue<Func<string, CancellationToken, Task<ProcessResult>>> _handlers = new();

    public int RunCount { get; private set; }
    public List<ProcessCall> Calls { get; } = [];

    public void AddResult(int exitCode, string stdout, string stderr = "")
        => _responses.Enqueue((exitCode, stdout, stderr));

    public void AddHandler(Func<string, CancellationToken, Task<ProcessResult>> handler)
        => _handlers.Enqueue(handler);

    public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments,
        CancellationToken ct = default, string? workingDirectory = null, TimeSpan? timeout = null)
    {
        RunCount++;
        Calls.Add(new ProcessCall(fileName, arguments.ToArray(), workingDirectory, ct, timeout));
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<ProcessResult>(ct);
        if (_handlers.Count > 0)
            return _handlers.Dequeue()(fileName, ct);

        if (_responses.Count > 0)
        {
            var (exitCode, stdout, stderr) = _responses.Dequeue();
            if (exitCode != 0)
                throw new ExternalToolException(fileName, exitCode, stderr);
            return Task.FromResult(new ProcessResult(exitCode, stdout, stderr));
        }

        throw new InvalidOperationException($"Unexpected RunAsync for '{fileName}'.");
    }

    public Task RunWithProgressAsync(string fileName, IEnumerable<string> arguments,
        Func<string, ExtractionProgress?> lineParser, IProgress<ExtractionProgress> progress,
        CancellationToken ct = default, TimeSpan? timeout = null)
    {
        RunCount++;
        Calls.Add(new ProcessCall(fileName, arguments.ToArray(), null, ct, timeout));
        if (ct.IsCancellationRequested)
            return Task.FromCanceled(ct);
        if (_handlers.Count > 0)
            return RunProgressHandlerAsync(_handlers.Dequeue(), fileName, ct, lineParser, progress);
        if (_responses.Count > 0)
        {
            var (exitCode, stdout, stderr) = _responses.Dequeue();
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                ct.ThrowIfCancellationRequested();
                if (lineParser(line) is { } parsed)
                    progress.Report(parsed);
            }
            if (exitCode != 0)
                throw new ExternalToolException(fileName, exitCode, stderr);
            return Task.CompletedTask;
        }

        throw new InvalidOperationException($"Unexpected RunWithProgressAsync for '{fileName}'.");
    }

    private static async Task RunProgressHandlerAsync(
        Func<string, CancellationToken, Task<ProcessResult>> handler, string fileName,
        CancellationToken ct, Func<string, ExtractionProgress?> lineParser,
        IProgress<ExtractionProgress> progress)
    {
        var result = await handler(fileName, ct);
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            if (lineParser(line) is { } parsed)
                progress.Report(parsed);
        }
        if (result.ExitCode != 0)
            throw new ExternalToolException(fileName, result.ExitCode, result.StandardError);
    }
}

public sealed record ProcessCall(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    CancellationToken CancellationToken,
    TimeSpan? Timeout);

public sealed class TestNativeToolResolver : INativeToolResolver
{
    private readonly string _toolsDirectory = Path.Combine(Path.GetTempPath(), "stream-extract-bundled-tools", Guid.NewGuid().ToString("N"));

    public string Resolve(NativeToolId id)
        => Path.GetFullPath(Path.Combine(_toolsDirectory, NativeTool.GetFilename(id)));
}
