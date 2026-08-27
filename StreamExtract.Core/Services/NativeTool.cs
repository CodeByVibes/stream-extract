namespace StreamExtract.Services;

public enum NativeToolId
{
    MkvMerge,
    MkvExtract,
    Mp4Box
}

public static class NativeTool
{
    public static string CurrentRid => OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";

    public static string GetFilename(NativeToolId id) => id switch
    {
        NativeToolId.MkvMerge => OperatingSystem.IsWindows() ? "mkvmerge.exe" : "mkvmerge",
        NativeToolId.MkvExtract => OperatingSystem.IsWindows() ? "mkvextract.exe" : "mkvextract",
        NativeToolId.Mp4Box => OperatingSystem.IsWindows() ? "mp4box.exe" : "MP4Box",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    public static IReadOnlySet<NativeToolId> RequiredIds { get; } =
        new HashSet<NativeToolId> { NativeToolId.MkvMerge, NativeToolId.MkvExtract, NativeToolId.Mp4Box };

    public static bool IsKnown(NativeToolId id) => RequiredIds.Contains(id);
}

public interface INativeToolResolver
{
    string Resolve(NativeToolId id);
}
