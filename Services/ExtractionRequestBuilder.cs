using StreamExtract.Models;
using StreamExtract.Services;

namespace StreamExtract.Services;

public static class ExtractionRequestBuilder
{
    public static ExtractRequest? TryBuild(ImportedFile imported, string outputDirectory, FileSelection selection)
    {
        if (!OutputPathGuard.IsValidOutputDirectory(outputDirectory))
            return null;
        if (!IsNonEmpty(selection))
            return null;

        return new ExtractRequest(
            imported.Info, outputDirectory,
            selection.TrackIds, selection.ChapterIds,
            selection.ExtractAttachments, selection.ExtractTags,
            selection.ExtractCueSheets, selection.ExtractTimestamps);
    }

    private static bool IsNonEmpty(FileSelection selection)
        => selection.TrackIds.Count > 0 || selection.ChapterIds.Count > 0
            || selection.ExtractAttachments || selection.ExtractTags
            || selection.ExtractCueSheets || selection.ExtractTimestamps;
}
