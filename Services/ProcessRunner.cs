using System.ComponentModel;
using System.Diagnostics;
using StreamExtract.Models;

namespace StreamExtract.Services;

public sealed class ProcessRunner(string toolPath) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName, IEnumerable<string> arguments, CancellationToken ct = default,
        string? workingDirectory = null)
    {
        using var p = CreateProcess(fileName, arguments, workingDirectory);
        try
        {
            p.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ExternalToolException(Path.GetFileName(fileName), -1,
                $"Unable to start '{Path.Combine(toolPath, fileName)}': {ex.Message}");
        }

        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            KillProcess(p);
            await WaitForExitNoThrowAsync(p);
            throw;
        }
        catch
        {
            KillProcess(p);
            await WaitForExitNoThrowAsync(p);
            throw;
        }

        if (p.ExitCode != 0)
        {
            throw new ExternalToolException(Path.GetFileName(fileName), p.ExitCode, stderrTask.Result);
        }

        return new ProcessResult(p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    public async Task RunWithProgressAsync(
        string fileName, IEnumerable<string> arguments,
        Func<string, ExtractionProgress?> lineParser,
        IProgress<ExtractionProgress> progress, CancellationToken ct = default)
    {
        using var p = CreateProcess(fileName, arguments, workingDirectory: null);
        try
        {
            p.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ExternalToolException(Path.GetFileName(fileName), -1,
                $"Unable to start '{Path.Combine(toolPath, fileName)}': {ex.Message}");
        }

        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        var stdoutTask = ReadStdoutAsync(p, lineParser, progress, ct);

        try
        {
            await stdoutTask;
            await p.WaitForExitAsync(ct);
            await stderrTask;
        }
        catch (OperationCanceledException)
        {
            KillProcess(p);
            await WaitForExitNoThrowAsync(p);
            throw;
        }
        catch
        {
            KillProcess(p);
            await WaitForExitNoThrowAsync(p);
            throw;
        }

        if (p.ExitCode != 0)
        {
            throw new ExternalToolException(Path.GetFileName(fileName), p.ExitCode, stderrTask.Result);
        }
    }

    private Process CreateProcess(string fileName, IEnumerable<string> arguments, string? workingDirectory)
    {
        var p = new Process
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
        return p;
    }

    private static async Task ReadStdoutAsync(Process p, Func<string, ExtractionProgress?> lineParser,
        IProgress<ExtractionProgress> progress, CancellationToken ct)
    {
        string? line;
        while ((line = await p.StandardOutput.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (lineParser(line) is { } pr) progress.Report(pr);
        }
    }

    private static async Task WaitForExitNoThrowAsync(Process p)
    {
        try { await p.WaitForExitAsync(); }
        catch { }
    }

    private static void KillProcess(Process p)
    {
        if (p.HasExited) return;
        try { p.Kill(true); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException) { }
    }
}
