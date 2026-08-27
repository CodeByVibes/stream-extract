using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using StreamExtract.Models;

namespace StreamExtract.Services;

public sealed class ProcessRunner(string toolPath) : IProcessRunner
{
    private const int MaxDiagnosticChars = 4096;

    public async Task<ProcessResult> RunAsync(
        string fileName, IEnumerable<string> arguments, CancellationToken ct = default,
        string? workingDirectory = null, TimeSpan? timeout = null)
    {
        using var timeoutCts = CreateTimeoutCts(ct, timeout);
        var effectiveCt = timeoutCts?.Token ?? ct;

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

        var stdoutTask = p.StandardOutput.ReadToEndAsync(effectiveCt);
        var stderrTask = p.StandardError.ReadToEndAsync(effectiveCt);

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
            await p.WaitForExitAsync(effectiveCt);
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
            throw new ExternalToolException(Path.GetFileName(fileName), p.ExitCode,
                FirstNonEmpty(stderrTask.Result, stdoutTask.Result));
        }

        return new ProcessResult(p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    public async Task RunWithProgressAsync(
        string fileName, IEnumerable<string> arguments,
        Func<string, ExtractionProgress?> lineParser,
        IProgress<ExtractionProgress> progress, CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        using var timeoutCts = CreateTimeoutCts(ct, timeout);
        var effectiveCt = timeoutCts?.Token ?? ct;

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

        var stderrTask = p.StandardError.ReadToEndAsync(effectiveCt);
        var diagnostics = new StringBuilder();
        var stdoutTask = ReadStdoutAsync(p, lineParser, progress, diagnostics, effectiveCt);

        try
        {
            await stdoutTask;
            await p.WaitForExitAsync(effectiveCt);
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
            // mkvextract writes both progress and error messages to stdout; stderr is usually empty.
            throw new ExternalToolException(Path.GetFileName(fileName), p.ExitCode,
                FirstNonEmpty(stderrTask.Result, diagnostics.ToString()));
        }
    }

    private Process CreateProcess(string fileName, IEnumerable<string> arguments, string? workingDirectory)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.IsPathRooted(fileName) ? fileName : Path.Combine(toolPath, fileName),
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
        IProgress<ExtractionProgress> progress, StringBuilder diagnostics, CancellationToken ct)
    {
        string? line;
        while ((line = await p.StandardOutput.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (lineParser(line) is { } pr)
            {
                progress.Report(pr);
                continue;
            }
            AppendDiagnostic(diagnostics, line);
        }
    }

    private static void AppendDiagnostic(StringBuilder diagnostics, string line)
    {
        if (diagnostics.Length >= MaxDiagnosticChars) return;
        var remaining = MaxDiagnosticChars - diagnostics.Length;
        if (line.Length <= remaining) diagnostics.AppendLine(line);
        else diagnostics.AppendLine(line[..remaining]);
    }

    private static string FirstNonEmpty(string primary, string fallback)
        => string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private static CancellationTokenSource? CreateTimeoutCts(CancellationToken ct, TimeSpan? timeout)
    {
        if (timeout is not { } t) return null;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(t);
        return cts;
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
