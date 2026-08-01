using System.Text;

namespace StreamExtract.Services;

public static class OutputPathGuard
{
    private static readonly string[] _deviceNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string ResolveContainedPath(string outputDirectory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory must not be empty.", nameof(outputDirectory));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidDataException("Output file name must not be empty.");

        var baseName = Path.GetFileName(fileName.Trim().TrimEnd('/', '\\'));
        if (string.IsNullOrEmpty(baseName) || baseName is "." or "..")
            throw new InvalidDataException($"Invalid output file name: '{fileName}'.");

        if (baseName.Length > 255)
            throw new InvalidDataException($"Output file name is too long (max 255 chars): '{fileName}'.");

        if (baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException($"Output file name contains invalid characters: '{fileName}'.");

        var stem = Path.GetFileNameWithoutExtension(baseName);
        if (_deviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Output file name is a reserved device name: '{fileName}'.");

        var outputRoot = Path.GetFullPath(outputDirectory);
        var full = Path.GetFullPath(Path.Combine(outputRoot, baseName));

        var rootPrefix = outputRoot.EndsWith(Path.DirectorySeparatorChar)
            ? outputRoot
            : outputRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Output path escapes the output directory: '{fileName}'.");

        return full;
    }

    public static bool IsValidOutputDirectory(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)) return false;
        try
        {
            return Path.IsPathRooted(Path.GetFullPath(outputDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }
}
