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

        // Windows strips trailing dots (and spaces) from file names, so a name ending in
        // '.' would silently alias an existing file or device — reject it.
        if (baseName.EndsWith('.'))
            throw new InvalidDataException($"Output file name ends with a dot: '{fileName}'.");

        // Trim trailing spaces/dots from the stem before the device check: "CON .txt" or
        // "CON..txt" normalize to the reserved CON device on Windows.
        var stem = Path.GetFileNameWithoutExtension(baseName).TrimEnd(' ', '.');
        if (_deviceNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Output file name is a reserved device name: '{fileName}'.");

        var outputRoot = Path.GetFullPath(outputDirectory);
        var full = Path.GetFullPath(Path.Combine(outputRoot, baseName));

        var rootPrefix = outputRoot.EndsWith(Path.DirectorySeparatorChar)
            ? outputRoot
            : outputRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Output path escapes the output directory: '{fileName}'.");

        RejectReparsePointsAlongPath(outputRoot, "output directory");
        RejectReparsePointsAlongPath(Path.GetDirectoryName(full)!, "output path");

        return full;
    }

    private static void RejectReparsePointsAlongPath(string path, string description)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"The {description} contains a reparse point: '{current}'.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase)) break;
            current = parent ?? "";
        }
    }

    public static bool IsValidOutputDirectory(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)) return false;
        if (!Path.IsPathRooted(outputDirectory)) return false;
        try
        {
            _ = Path.GetFullPath(outputDirectory);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }
}
