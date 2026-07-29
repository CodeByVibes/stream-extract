namespace StreamExtract.Models;

public enum TrackType { Video, Audio, Subtitle, Other }

public sealed record TrackInfo(
    int Id, TrackType Type, string Codec, string TrackName,
    string Language, Dictionary<string, string> Properties);

public sealed record ChapterInfo(int Id, string Name, string Language);

public sealed record AttachmentInfo(long Id, string FileName, string MimeType, long Size);

public sealed record TagInfo(int TargetId, string Name, string Value);

[Flags]
public enum ExtractorFeatures
{
    Tracks = 1 << 0, Chapters = 1 << 1, Attachments = 1 << 2,
    Tags = 1 << 3, CueSheets = 1 << 4, Timestamps = 1 << 5,
}

public sealed record MediaFileInfo(
    string FilePath, string FileName, ExtractorFeatures Features,
    List<TrackInfo> Tracks, List<ChapterInfo> Chapters,
    List<AttachmentInfo> Attachments, List<TagInfo> Tags);

public sealed record ExtractRequest(
    MediaFileInfo Source, string OutputDirectory,
    HashSet<int> SelectedTrackIds, HashSet<int> SelectedChapterIds,
    bool ExtractAttachments, bool ExtractTags,
    bool ExtractCueSheets, bool ExtractTimestamps);

public sealed record ExtractionProgress(
    string CurrentFile, string CurrentItem, int Percentage,
    string StatusText, bool IsComplete);
