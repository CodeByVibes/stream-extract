using StreamExtract.Models;

namespace StreamExtract.Services;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments,
        CancellationToken ct = default, string? workingDirectory = null);

    Task RunWithProgressAsync(string fileName, IEnumerable<string> arguments,
        Func<string, ExtractionProgress?> lineParser, IProgress<ExtractionProgress> progress,
        CancellationToken ct = default);
}
