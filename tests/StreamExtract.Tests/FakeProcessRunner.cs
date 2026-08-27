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
        Calls.Add(new ProcessCall(fileName, arguments.ToArray(), workingDirectory));
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
        Calls.Add(new ProcessCall(fileName, arguments.ToArray(), null));
        if (_responses.Count > 0)
        {
            var (exitCode, stdout, stderr) = _responses.Dequeue();
            if (ct.IsCancellationRequested)
                return Task.FromCanceled(ct);
            if (exitCode != 0)
                throw new ExternalToolException(fileName, exitCode, stderr);
            return Task.CompletedTask;
        }

        throw new InvalidOperationException($"Unexpected RunWithProgressAsync for '{fileName}'.");
    }
}

public sealed record ProcessCall(string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory);

public sealed class TestNativeToolResolver : INativeToolResolver
{
    private readonly string _toolsDirectory = Path.Combine(Path.GetTempPath(), "stream-extract-bundled-tools", Guid.NewGuid().ToString("N"));

    public string Resolve(NativeToolId id)
        => Path.GetFullPath(Path.Combine(_toolsDirectory, NativeTool.GetFilename(id)));
}
