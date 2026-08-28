using StreamExtract.Cli.Commands;
using StreamExtract.Cli.Formatting;
using StreamExtract.Models;
using StreamExtract.Plugins;
using StreamExtract.Services;

namespace StreamExtract.Cli.Extraction;

public sealed class CommandRunner(PluginRegistry registry, TextWriter stdout, TextWriter stderr)
{
    public async Task<int> RunAsync(CliParseResult options, CancellationToken ct)
    {
        if (options.Command == CliCommand.Help) { await stdout.WriteLineAsync(CliOptions.HelpText); return 0; }
        if (options.Command == CliCommand.Version) { await stdout.WriteLineAsync(CliOptions.Version); return 0; }
        if (options.Command == CliCommand.Info) return await InfoAsync(options.InputFiles[0], ct);
        var failed = false;
        var usageFailure = false;
        string outputDirectory;
        try { outputDirectory = ResolveOutputDirectory(options.OutputDirectory); }
        catch (CliUsageException ex)
        {
            await stderr.WriteLineAsync(ex.Message);
            return 2;
        }
        foreach (var path in options.InputFiles)
        {
            try
            {
                ValidateInputFile(path);
            }
            catch (CliUsageException ex)
            {
                await stderr.WriteLineAsync(ex.Message);
                return 2;
            }
        }
        foreach (var path in options.InputFiles)
        {
        try
        {
            var plugin = registry.GetPlugin(path) ?? throw new CliUsageException($"unsupported file type: {path}");
            ValidateInputFile(path);
            ValidateSupportedFeatures(options, plugin);
                var info = await plugin.AnalyzeFileAsync(path, ct);
                var ids = options.All || (options.TrackIds.Count == 0 && (options.ExtractCuesForSelectedTracks || options.ExtractTimestamps))
                    ? info.Tracks.Select(x => x.Id).ToHashSet() : options.TrackIds.ToHashSet();
                if (!options.All)
                {
                    var availableIds = info.Tracks.Select(x => x.Id).ToHashSet();
                    var unknownIds = ids.Where(id => !availableIds.Contains(id)).ToArray();
                    if (unknownIds.Length > 0)
                        throw new CliUsageException($"unknown track ID(s): {string.Join(", ", unknownIds)}");
                }
                var selection = new FileSelection(ids, options.All || options.ExtractAttachments, options.ExtractChapters || options.All ? info.Chapters.Select(x => x.Id).ToHashSet() : [], options.All || options.ExtractTags, options.All || options.ExtractCueSheets, options.All || options.ExtractTimestamps, options.All || options.ExtractCuesForSelectedTracks);
                var request = ExtractionRequestBuilder.TryBuild(new ImportedFile(path, plugin, info), outputDirectory, selection)
                    ?? throw new CliUsageException("invalid output directory or empty selection");
                var progress = new Progress<ExtractionProgress>(p => { if (options.Verbose || !Console.IsOutputRedirected) stdout.WriteLine($"{info.FileName}: {p.StatusText}"); });
                var outcome = await plugin.ExtractAsync(request, progress, ct);
                foreach (var failure in outcome.Failures) { await stderr.WriteLineAsync($"{path}: {failure}"); failed = true; }
            }
            catch (OperationCanceledException) { throw; }
            catch (CliUsageException ex) { await stderr.WriteLineAsync($"{path}: {ex.Message}"); failed = true; usageFailure = true; }
            catch (Exception ex) { await stderr.WriteLineAsync($"{path}: {ex.Message}"); failed = true; }
        }
        return usageFailure ? 2 : failed ? 1 : 0;
    }

    private static void ValidateSupportedFeatures(CliParseResult options, IExtractorPlugin plugin)
    {
        var requested = options.All
            ? ExtractorFeatures.Tracks | ExtractorFeatures.Chapters | ExtractorFeatures.Attachments |
              ExtractorFeatures.Tags | ExtractorFeatures.CueSheets | ExtractorFeatures.Timestamps
            : ((options.TrackIds.Count > 0 || options.ExtractCuesForSelectedTracks || options.ExtractTimestamps) ? ExtractorFeatures.Tracks : (ExtractorFeatures)0) |
              (options.ExtractChapters ? ExtractorFeatures.Chapters : (ExtractorFeatures)0) |
              (options.ExtractAttachments ? ExtractorFeatures.Attachments : (ExtractorFeatures)0) |
              (options.ExtractTags ? ExtractorFeatures.Tags : (ExtractorFeatures)0) |
              (options.ExtractCueSheets || options.ExtractCuesForSelectedTracks ? ExtractorFeatures.CueSheets : (ExtractorFeatures)0) |
              (options.ExtractTimestamps ? ExtractorFeatures.Timestamps : (ExtractorFeatures)0);
        var unsupported = requested & ~plugin.SupportedFeatures;
        if (unsupported == 0) return;

        var names = Enum.GetValues<ExtractorFeatures>()
            .Where(feature => (unsupported & feature) != 0)
            .Select(feature => feature switch
            {
                ExtractorFeatures.Tracks => "tracks",
                ExtractorFeatures.Chapters => "chapters",
                ExtractorFeatures.Attachments => "attachments",
                ExtractorFeatures.Tags => "tags",
                ExtractorFeatures.CueSheets => "cue sheets",
                ExtractorFeatures.Timestamps => "timestamps",
                _ => feature.ToString()
            });
        throw new CliUsageException($"{plugin.Name} does not support: {string.Join(", ", names)}");
    }

    private static string ResolveOutputDirectory(string? configuredPath)
    {
        var displayPath = configuredPath ?? Directory.GetCurrentDirectory();
        try
        {
            var path = Path.GetFullPath(displayPath);
            if (File.Exists(path))
                throw new CliUsageException($"output path is not a directory: {path}");

            Directory.CreateDirectory(path);
            if (!Directory.Exists(path))
                throw new CliUsageException($"output path is not a directory: {path}");
            return path;
        }
        catch (CliUsageException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new CliUsageException($"invalid output directory '{displayPath}': {ex.Message}");
        }
    }

    private static void ValidateInputFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                throw new CliUsageException($"input file does not exist: {path}");
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                throw new CliUsageException($"input path is not a regular file: {path}");
        }
        catch (CliUsageException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new CliUsageException($"input path is not a regular file: {path}: {ex.Message}");
        }
    }

    private async Task<int> InfoAsync(string path, CancellationToken ct)
    {
        try
        {
            var plugin = registry.GetPlugin(path) ?? throw new CliUsageException($"unsupported file type: {path}");
            ValidateInputFile(path);
            await stdout.WriteLineAsync(TerminalFormatter.FormatMediaInfo(await plugin.AnalyzeFileAsync(path, ct)));
            return 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (CliUsageException ex) { await stderr.WriteLineAsync(ex.Message); return 2; }
        catch (NotSupportedException ex) { await stderr.WriteLineAsync(ex.Message); return 2; }
        catch (Exception ex) { await stderr.WriteLineAsync(ex.Message); return 1; }
    }
}
