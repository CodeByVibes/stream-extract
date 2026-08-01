using StreamExtract.Plugins;

namespace StreamExtract.Models;

public sealed record ImportedFile(string FilePath, IExtractorPlugin Plugin, MediaFileInfo Info);

public sealed record FileSelection(
    HashSet<int> TrackIds, bool ExtractAttachments,
    HashSet<int> ChapterIds, bool ExtractTags,
    bool ExtractCueSheets, bool ExtractTimestamps);
