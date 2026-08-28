using StreamExtract.Cli.Commands;
using StreamExtract.Cli.Extraction;
using StreamExtract.Models;
using StreamExtract.Plugins;

namespace StreamExtract.Tests;

public sealed class CliIntegrationTests
{
    [Fact]
    public async Task ExplicitTrackSelectionIsPreservedForTimestampExtraction()
    {
        var plugin = new CapturingPlugin([1, 2]);
        var registry = new PluginRegistry();
        registry.Register(plugin);
        var outputDirectory = Path.Combine(Path.GetTempPath(), "stream-extract-cli-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var exitCode = await new CommandRunner(registry, new StringWriter(), new StringWriter())
                .RunAsync(CliParser.Parse(["extract", ExistingInput("movie.mkv"), "--tracks", "2", "--timestamps", "--output", outputDirectory]), CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal([2], plugin.Request!.SelectedTrackIds);
        }
        finally
        {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true);
        }
    }

    [Fact]
    public async Task UnknownTrackIdIsUsageErrorBeforeExtraction()
    {
        var plugin = new CapturingPlugin([1]);
        var registry = new PluginRegistry();
        registry.Register(plugin);

        var exitCode = await new CommandRunner(registry, new StringWriter(), new StringWriter())
            .RunAsync(CliParser.Parse(["extract", ExistingInput("movie.mkv"), "--tracks", "99"]), CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Null(plugin.Request);
    }

    [Fact]
    public async Task UnsupportedExtractionFileIsUsageError()
    {
        var stderr = new StringWriter();
        var input = ExistingInput("movie.xyz");
        try
        {
            var exitCode = await new CommandRunner(new PluginRegistry(), new StringWriter(), stderr)
                .RunAsync(CliParser.Parse(["extract", input, "--tracks", "1"]), CancellationToken.None);

            Assert.Equal(2, exitCode);
            Assert.Contains("unsupported file type", stderr.ToString());
        }
        finally { File.Delete(input); }
    }

    [Fact]
    public async Task InvalidOutputDirectoryIsUsageErrorBeforeExtraction()
    {
        var plugin = new CapturingPlugin([1]);
        var registry = new PluginRegistry();
        registry.Register(plugin);
        var outputFile = Path.Combine(Path.GetTempPath(), "stream-extract-output-file-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(outputFile, "not a directory");

        try
        {
            var exitCode = await new CommandRunner(registry, new StringWriter(), new StringWriter())
                .RunAsync(CliParser.Parse(["extract", ExistingInput("movie.mkv"), "--tracks", "1", "--output", outputFile]), CancellationToken.None);

            Assert.Equal(2, exitCode);
            Assert.Null(plugin.Request);
        }
        finally
        {
            File.Delete(outputFile);
        }
    }

    [Fact]
    public async Task InvalidOutputDirectoryIsValidatedBeforeAnyAnalysis()
    {
        var plugin = new CapturingPlugin([1]);
        var registry = new PluginRegistry();
        registry.Register(plugin);
        var outputFile = Path.Combine(Path.GetTempPath(), "stream-extract-output-file-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(outputFile, "not a directory");

        try
        {
            var exitCode = await new CommandRunner(registry, new StringWriter(), new StringWriter())
                .RunAsync(CliParser.Parse(["extract", ExistingInput("a.mkv"), ExistingInput("b.mkv"), "--tracks", "1", "--output", outputFile]), CancellationToken.None);

            Assert.Equal(2, exitCode);
            Assert.Null(plugin.Request);
        }
        finally { File.Delete(outputFile); }
    }

    [Fact]
    public async Task MissingInputFileIsUsageErrorBeforeAnyAnalysis()
    {
        var plugin = new CapturingPlugin([1]);
        var registry = new PluginRegistry();
        registry.Register(plugin);

        var exitCode = await new CommandRunner(registry, new StringWriter(), new StringWriter())
            .RunAsync(CliParser.Parse(["extract", "missing.mkv", "--tracks", "1"]), CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Null(plugin.Request);
    }

    [Fact]
    public async Task DirectoryInputIsUsageErrorBeforeAnyAnalysis()
    {
        var directory = Path.Combine(Path.GetTempPath(), "stream-extract-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var plugin = new CapturingPlugin([1]);
            var registry = new PluginRegistry();
            registry.Register(plugin);
            var exitCode = await new CommandRunner(registry, new StringWriter(), new StringWriter())
                .RunAsync(CliParser.Parse(["extract", directory, "--tracks", "1"]), CancellationToken.None);

            Assert.Equal(2, exitCode);
            Assert.Null(plugin.Request);
        }
        finally { Directory.Delete(directory); }
    }

    [Theory]
    [InlineData("--timestamps")]
    [InlineData("--cues-for-selected-tracks")]
    public async Task ImplicitAllTrackSelectionRequiresTracksFeature(string option)
    {
        var input = Path.Combine(Path.GetTempPath(), "stream-extract-input-" + Guid.NewGuid().ToString("N") + ".mkv");
        await File.WriteAllTextAsync(input, "input");
        try
        {
            var plugin = new FeaturePlugin(".mkv", ExtractorFeatures.Timestamps | ExtractorFeatures.CueSheets);
            var registry = new PluginRegistry();
            registry.Register(plugin);
            var exitCode = await new CommandRunner(registry, new StringWriter(), new StringWriter())
                .RunAsync(CliParser.Parse(["extract", input, option]), CancellationToken.None);

            Assert.Equal(2, exitCode);
            Assert.Null(plugin.Request);
        }
        finally { File.Delete(input); }
    }

    [Theory]
    [InlineData("--attachments")]
    [InlineData("--tags")]
    [InlineData("--cue-sheets")]
    [InlineData("--timestamps")]
    public async Task Mp4UnsupportedExtractionModeIsUsageError(string mode)
    {
        var plugin = new FeaturePlugin(".mp4", ExtractorFeatures.Tracks | ExtractorFeatures.Chapters);
        var registry = new PluginRegistry();
        registry.Register(plugin);

        var exitCode = await new CommandRunner(registry, new StringWriter(), new StringWriter())
            .RunAsync(CliParser.Parse(["extract", ExistingInput("movie.mp4"), mode]), CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Null(plugin.Request);
    }

    [Fact]
    public async Task Mp4AllUnsupportedExtractionModesAreUsageError()
    {
        var plugin = new FeaturePlugin(".mp4", ExtractorFeatures.Tracks | ExtractorFeatures.Chapters);
        var registry = new PluginRegistry();
        registry.Register(plugin);

        var exitCode = await new CommandRunner(registry, new StringWriter(), new StringWriter())
            .RunAsync(CliParser.Parse(["extract", ExistingInput("movie.mp4"), "--all"]), CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Null(plugin.Request);
    }

    [Fact]
    public async Task ExtractsMultipleFilesAndContinuesAfterFailure()
    {
        var runner = new FakeProcessRunner();
        runner.AddResult(0, "");
        runner.AddResult(1, "", "bad media");
        var plugin = new FakePlugin(runner);
        var registry = new PluginRegistry();
        registry.Register(plugin);
        var output = new StringWriter();
        var app = new CommandRunner(registry, output, output);

        var exitCode = await app.RunAsync(CliParser.Parse(["extract", ExistingInput("a.mkv"), ExistingInput("b.mkv"), "--tracks", "1"]), CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Contains("b.mkv", output.ToString());
        Assert.Equal(2, plugin.AnalyzeCount);
        Assert.Equal(2, plugin.ExtractCount);
    }

    public static IEnumerable<object[]> SelectionCases =>
    [
        ["--tracks", new[] { "1" }, true, false, false, false, false, false, false],
        ["--chapters", Array.Empty<string>(), false, true, false, false, false, false, false],
        ["--attachments", Array.Empty<string>(), false, false, true, false, false, false, false],
        ["--tags", Array.Empty<string>(), false, false, false, true, false, false, false],
        ["--cue-sheets", Array.Empty<string>(), false, false, false, false, true, false, false],
        ["--timestamps", Array.Empty<string>(), true, false, false, false, false, true, false],
        ["--cues-for-selected-tracks", Array.Empty<string>(), true, false, false, false, false, false, true],
        ["--all", Array.Empty<string>(), true, true, true, true, true, true, true]
    ];

    [Theory]
    [MemberData(nameof(SelectionCases))]
    public async Task ExtractionOptionMapsToExpectedSelection(string option, string[] values,
        bool tracks, bool chapters, bool attachments, bool tags, bool cueSheets, bool timestamps, bool selectedCues)
    {
        var plugin = new SelectionPlugin();
        var registry = new PluginRegistry();
        registry.Register(plugin);
        var args = new List<string> { "extract", ExistingInput("movie.mkv"), option };
        args.AddRange(values);

        var exitCode = await new CommandRunner(registry, new StringWriter(), new StringWriter())
            .RunAsync(CliParser.Parse(args.ToArray()), CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.NotNull(plugin.Request);
        var request = plugin.Request!;
        Assert.Equal(tracks, request.SelectedTrackIds.Count > 0);
        Assert.Equal(chapters, request.SelectedChapterIds.Count > 0);
        Assert.Equal(attachments, request.ExtractAttachments);
        Assert.Equal(tags, request.ExtractTags);
        Assert.Equal(cueSheets, request.ExtractCueSheets);
        Assert.Equal(timestamps, request.ExtractTimestamps);
        Assert.Equal(selectedCues, request.ExtractCuesForSelectedTracks);
    }

    [Fact]
    public async Task FailedExtractionDoesNotProduceTerminalSuccessOutput()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var registry = new PluginRegistry();
        registry.Register(new FailingPlugin());

        var exitCode = await new CommandRunner(registry, stdout, stderr)
            .RunAsync(CliParser.Parse(["extract", ExistingInput("movie.mkv"), "--tracks", "1"]), CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("Done", stdout.ToString());
        Assert.Contains("failed", stderr.ToString());
    }

    private sealed class FakePlugin(FakeProcessRunner runner) : IExtractorPlugin
    {
        public int AnalyzeCount { get; private set; }
        public int ExtractCount { get; private set; }
        public string Name => "Fake";
        public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>([".mkv"]);
        public ExtractorFeatures SupportedFeatures => ExtractorFeatures.Tracks;
        public Task<MediaFileInfo> AnalyzeFileAsync(string path, CancellationToken ct = default)
        {
            AnalyzeCount++;
            return Task.FromResult(new MediaFileInfo(path, Path.GetFileName(path), SupportedFeatures,
                [new TrackInfo(1, TrackType.Audio, "aac", "", "und", new())], [], [], []));
        }
        public async Task<ExtractOutcome> ExtractAsync(ExtractRequest request, IProgress<ExtractionProgress> progress, CancellationToken ct = default)
        {
            ExtractCount++;
            await runner.RunAsync("fake", [], ct);
            return ExtractOutcome.Success;
        }
    }

    private static string ExistingInput(string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), "stream-extract-cli-" + Guid.NewGuid().ToString("N") + "-" + fileName);
        File.WriteAllText(path, "test input");
        return path;
    }

    private sealed class CapturingPlugin(IReadOnlyList<int> trackIds) : IExtractorPlugin
    {
        public ExtractRequest? Request { get; private set; }
        public string Name => "Fake";
        public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>([".mkv"]);
        public ExtractorFeatures SupportedFeatures => ExtractorFeatures.Tracks | ExtractorFeatures.Timestamps;
        public Task<MediaFileInfo> AnalyzeFileAsync(string path, CancellationToken ct = default)
            => Task.FromResult(new MediaFileInfo(path, Path.GetFileName(path), SupportedFeatures,
                trackIds.Select(id => new TrackInfo(id, TrackType.Audio, "aac", "", "und", new())).ToList(), [], [], []));
        public Task<ExtractOutcome> ExtractAsync(ExtractRequest request, IProgress<ExtractionProgress> progress, CancellationToken ct = default)
        {
            Request = request;
            return Task.FromResult(ExtractOutcome.Success);
        }
    }

    private sealed class FeaturePlugin(string extension, ExtractorFeatures features) : IExtractorPlugin
    {
        public ExtractRequest? Request { get; private set; }
        public string Name => "Feature test plugin";
        public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>([extension]);
        public ExtractorFeatures SupportedFeatures => features;
        public Task<MediaFileInfo> AnalyzeFileAsync(string path, CancellationToken ct = default)
            => Task.FromResult(new MediaFileInfo(path, Path.GetFileName(path), features,
                [new TrackInfo(1, TrackType.Video, "avc1", "", "und", new())], [], [], []));
        public Task<ExtractOutcome> ExtractAsync(ExtractRequest request, IProgress<ExtractionProgress> progress, CancellationToken ct = default)
        {
            Request = request;
            return Task.FromResult(ExtractOutcome.Success);
        }
    }

    private sealed class SelectionPlugin : IExtractorPlugin
    {
        public ExtractRequest? Request { get; private set; }
        public string Name => "Selection test plugin";
        public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>([".mkv"]);
        public ExtractorFeatures SupportedFeatures => ExtractorFeatures.Tracks | ExtractorFeatures.Chapters |
            ExtractorFeatures.Attachments | ExtractorFeatures.Tags | ExtractorFeatures.CueSheets | ExtractorFeatures.Timestamps;
        public Task<MediaFileInfo> AnalyzeFileAsync(string path, CancellationToken ct = default)
            => Task.FromResult(new MediaFileInfo(path, Path.GetFileName(path), SupportedFeatures,
                [new TrackInfo(1, TrackType.Audio, "aac", "", "und", new())], [new ChapterInfo(2, "", "")],
                [new AttachmentInfo(3, "file.bin", "application/octet-stream", 1)], [new TagInfo(0, "key", "value")]));
        public Task<ExtractOutcome> ExtractAsync(ExtractRequest request, IProgress<ExtractionProgress> progress, CancellationToken ct = default)
        {
            Request = request;
            return Task.FromResult(ExtractOutcome.Success);
        }
    }

    private sealed class FailingPlugin : IExtractorPlugin
    {
        public string Name => "Failing test plugin";
        public IReadOnlySet<string> SupportedExtensions { get; } = new HashSet<string>([".mkv"]);
        public ExtractorFeatures SupportedFeatures => ExtractorFeatures.Tracks;
        public Task<MediaFileInfo> AnalyzeFileAsync(string path, CancellationToken ct = default)
            => Task.FromResult(new MediaFileInfo(path, Path.GetFileName(path), SupportedFeatures,
                [new TrackInfo(1, TrackType.Audio, "aac", "", "und", new())], [], [], []));
        public Task<ExtractOutcome> ExtractAsync(ExtractRequest request, IProgress<ExtractionProgress> progress, CancellationToken ct = default)
            => Task.FromResult(new ExtractOutcome(false, ["failed"]));
    }
}
