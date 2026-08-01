using StreamExtract.Models;

namespace StreamExtract.Plugins;

public interface IExtractorPlugin
{
    string Name { get; }
    IReadOnlySet<string> SupportedExtensions { get; }
    ExtractorFeatures SupportedFeatures { get; }
    Task<MediaFileInfo> AnalyzeFileAsync(string filePath, CancellationToken ct = default);
    Task ExtractAsync(ExtractRequest request, IProgress<ExtractionProgress> progress, CancellationToken ct = default);
}
