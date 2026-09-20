namespace StreamExtract.Services;

/// <summary>
/// Shared string comparison for file paths. Windows treats paths case-insensitively, so
/// deduplicating dropped or pasted paths with an ordinal comparison there would let
/// <c>C:\File.mkv</c> and <c>c:\file.mkv</c> through as two separate entries.
/// </summary>
public static class PathComparer
{
    public static StringComparer Default { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
