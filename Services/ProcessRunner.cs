using System.Diagnostics;
using StreamExtract.Models;

namespace StreamExtract.Services;

public sealed class ProcessRunner(string toolPath)
{
    public async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string fileName, IEnumerable<string> arguments, CancellationToken ct = default,
        string? workingDirectory = null)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(toolPath, fileName),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? ""
            }
        };
        foreach (var arg in arguments)
        {
            p.StartInfo.ArgumentList.Add(arg);
        }
        p.Start();
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            if (!p.HasExited)
            {
                p.Kill(true);
            }
            throw;
        }

        return (p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    public async Task RunWithProgressAsync(
        string fileName, IEnumerable<string> arguments,
        Func<string, ExtractionProgress?> lineParser,
        IProgress<ExtractionProgress> progress, CancellationToken ct = default)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(toolPath, fileName),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }
        };
        foreach (var arg in arguments)
        {
            p.StartInfo.ArgumentList.Add(arg);
        }
        p.Start();
        try
        {
            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync(ct)) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (lineParser(line) is { } pr) progress.Report(pr);
            }
            await p.WaitForExitAsync(ct);
            if (p.ExitCode != 0)
            {
                var err = await p.StandardError.ReadToEndAsync(ct);
                progress.Report(new ExtractionProgress("", "", 0,
                    $"Failed (code {p.ExitCode}): {err}", IsComplete: true));
                return;
            }
        }
        catch (OperationCanceledException)
        {
            p.Kill(true);
            progress.Report(new ExtractionProgress("", "", 0, "Cancelled", IsComplete: true));
            return;
        }
        progress.Report(new ExtractionProgress("", "", 100, "Done", IsComplete: true));
    }
}
