using System.Security.Cryptography;

namespace StreamExtract.Services;

public enum NativeToolValidationFailure
{
    Missing,
    HashMismatch,
    InvalidManifest,
    WrongPlatform,
    NotExecutable,
    InvalidPath,
    UnknownId,
    Duplicate,
    MissingRequired,
    EmptyMetadata,
    MalformedHash
}

public sealed class NativeToolValidationException(
    NativeToolValidationFailure failure, string path, string? detail = null)
    : InvalidOperationException($"Native tool validation failed ({failure}): {path}{(detail is null ? "" : $" ({detail})")}")
{
    public NativeToolValidationFailure Failure { get; } = failure;
    public string Path { get; } = path;
}

public static class NativeToolValidator
{
    public static IReadOnlyList<NativeToolManifestRecord> Validate(string applicationBaseDirectory, NativeToolManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(applicationBaseDirectory))
            throw new NativeToolValidationException(NativeToolValidationFailure.InvalidPath, applicationBaseDirectory);
        if (manifest is null || manifest.Tools is null)
            throw new NativeToolValidationException(NativeToolValidationFailure.InvalidManifest, "tools");

        var seen = new HashSet<(NativeToolId Id, string Rid)>();
        var current = new Dictionary<NativeToolId, NativeToolManifestRecord>();
        var knownIds = new HashSet<NativeToolId>();
        var result = new List<NativeToolManifestRecord>();
        var manifestPaths = new HashSet<string>(StringComparer.Ordinal);
        var logicalPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in manifest.Tools)
        {
            if (record is null)
                throw new NativeToolValidationException(NativeToolValidationFailure.InvalidManifest, "tools");
            if (!NativeTool.IsKnown(record.Id))
                throw new NativeToolValidationException(NativeToolValidationFailure.UnknownId, record.Id.ToString());
            if (string.IsNullOrWhiteSpace(record.Filename) || string.IsNullOrWhiteSpace(record.Version) ||
                string.IsNullOrWhiteSpace(record.Rid) || string.IsNullOrWhiteSpace(record.Source))
                throw new NativeToolValidationException(NativeToolValidationFailure.EmptyMetadata, record.Id.ToString());
            if (record.Filename.IndexOfAny(['/', '\\']) >= 0 ||
                record.Filename.StartsWith(['/', '\\']) ||
                (record.Filename.Length >= 2 && char.IsLetter(record.Filename[0]) && record.Filename[1] == ':'))
                throw new NativeToolValidationException(NativeToolValidationFailure.InvalidPath, record.Filename);
            if (!seen.Add((record.Id, record.Rid)))
                throw new NativeToolValidationException(NativeToolValidationFailure.Duplicate, record.Filename, record.Rid);
            if (!logicalPaths.Add($"tools/{record.Filename}"))
                throw new NativeToolValidationException(NativeToolValidationFailure.Duplicate, record.Filename, record.Rid);
            knownIds.Add(record.Id);
            if (record.Sha256.Length != 64 || !record.Sha256.All(Uri.IsHexDigit))
                throw new NativeToolValidationException(NativeToolValidationFailure.MalformedHash, record.Filename);

            if (!string.Equals(record.Rid, NativeTool.CurrentRid, StringComparison.OrdinalIgnoreCase))
                continue;

            var expected = NativeTool.GetFilename(record.Id);
            if (!string.Equals(record.Filename, expected, StringComparison.Ordinal))
                throw new NativeToolValidationException(NativeToolValidationFailure.InvalidManifest, record.Filename, $"expected {expected}");
            if (!current.TryAdd(record.Id, record))
                throw new NativeToolValidationException(NativeToolValidationFailure.Duplicate, record.Filename, record.Rid);

            var path = ValidateContainedPath(applicationBaseDirectory,
                Path.Combine(applicationBaseDirectory, "tools", record.Filename));
            EnsureRegularFile(path);
            manifestPaths.Add(Path.GetRelativePath(applicationBaseDirectory, path).Replace(Path.DirectorySeparatorChar, '/'));
            if (!File.Exists(path))
                throw new NativeToolValidationException(NativeToolValidationFailure.Missing, path);
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) == 0)
                throw new NativeToolValidationException(NativeToolValidationFailure.NotExecutable, path);

            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actual, record.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new NativeToolValidationException(NativeToolValidationFailure.HashMismatch, path,
                    $"expected {record.Sha256}, got {actual}");
            result.Add(record);
        }
        foreach (var id in NativeTool.RequiredIds)
        {
            if (knownIds.Contains(id) && !current.ContainsKey(id))
                throw new NativeToolValidationException(NativeToolValidationFailure.WrongPlatform, id.ToString(), NativeTool.CurrentRid);
            if (!current.ContainsKey(id))
                throw new NativeToolValidationException(NativeToolValidationFailure.MissingRequired, id.ToString(), NativeTool.CurrentRid);
        }
        ValidateArtifacts(applicationBaseDirectory, manifest.Artifacts, manifestPaths, logicalPaths);
        ValidateToolDirectory(applicationBaseDirectory, manifestPaths);
        return result;
    }

    private static void ValidateArtifacts(string applicationBaseDirectory, IReadOnlyList<NativeToolManifestArtifact>? artifacts,
        HashSet<string> manifestPaths, HashSet<string> logicalPaths)
    {
        if (artifacts is null) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in artifacts)
        {
            if (artifact is null || string.IsNullOrWhiteSpace(artifact.Path) ||
                string.IsNullOrWhiteSpace(artifact.Version) || string.IsNullOrWhiteSpace(artifact.Rid) ||
                string.IsNullOrWhiteSpace(artifact.Source))
                throw new NativeToolValidationException(NativeToolValidationFailure.EmptyMetadata, "artifacts");
            if (artifact.Path.IndexOfAny(['\\']) >= 0 || Path.IsPathRooted(artifact.Path) ||
                !artifact.Path.StartsWith("tools/", StringComparison.Ordinal) ||
                artifact.Path.Split('/').Any(segment => segment is "" or "." or "..") ||
                !seen.Add(artifact.Path))
                throw new NativeToolValidationException(NativeToolValidationFailure.InvalidPath, artifact.Path);
            if (artifact.Sha256.Length != 64 || !artifact.Sha256.All(Uri.IsHexDigit))
                throw new NativeToolValidationException(NativeToolValidationFailure.MalformedHash, artifact.Path);
            if (logicalPaths.Contains(artifact.Path))
                throw new NativeToolValidationException(NativeToolValidationFailure.Duplicate, artifact.Path);
            if (!string.Equals(artifact.Rid, NativeTool.CurrentRid, StringComparison.OrdinalIgnoreCase))
                continue;

            var path = ValidateContainedPath(applicationBaseDirectory,
                Path.Combine(applicationBaseDirectory, artifact.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!manifestPaths.Add(artifact.Path))
                throw new NativeToolValidationException(NativeToolValidationFailure.Duplicate, artifact.Path);
            EnsureRegularFile(path);
            if (!File.Exists(path))
                throw new NativeToolValidationException(NativeToolValidationFailure.Missing, path);
            if (artifact.Executable && !OperatingSystem.IsWindows() &&
                (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) == 0)
                throw new NativeToolValidationException(NativeToolValidationFailure.NotExecutable, path);
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actual, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new NativeToolValidationException(NativeToolValidationFailure.HashMismatch, path,
                    $"expected {artifact.Sha256}, got {actual}");
        }
    }

    private static void ValidateToolDirectory(string applicationBaseDirectory, HashSet<string> manifestPaths)
    {
        var toolsPath = ValidateContainedPath(applicationBaseDirectory,
            Path.Combine(applicationBaseDirectory, "tools"));
        if (!Directory.Exists(toolsPath))
            throw new NativeToolValidationException(NativeToolValidationFailure.Missing, toolsPath);

        foreach (var entry in Directory.EnumerateFileSystemEntries(toolsPath, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(entry);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new NativeToolValidationException(NativeToolValidationFailure.InvalidPath, entry);
            if (Directory.Exists(entry))
                continue;
            EnsureRegularFile(entry);
            var relative = Path.GetRelativePath(applicationBaseDirectory, entry).Replace(Path.DirectorySeparatorChar, '/');
            if (!manifestPaths.Contains(relative))
                throw new NativeToolValidationException(NativeToolValidationFailure.InvalidManifest, relative);
        }
    }

    private static void EnsureRegularFile(string path)
    {
        if (Directory.Exists(path))
            throw new NativeToolValidationException(NativeToolValidationFailure.InvalidPath, path);
        if (!File.Exists(path))
            throw new NativeToolValidationException(NativeToolValidationFailure.Missing, path);
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new NativeToolValidationException(NativeToolValidationFailure.InvalidPath, path);
    }

    private static string ValidateContainedPath(string applicationBaseDirectory, string candidate)
    {
        var basePath = Path.GetFullPath(applicationBaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.GetFullPath(candidate);
        var prefix = basePath + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(prefix, comparison))
            throw new NativeToolValidationException(NativeToolValidationFailure.InvalidPath, path);

        var current = basePath;
        foreach (var component in path[prefix.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (component.Length == 0) continue;
            current = Path.Combine(current, component);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new NativeToolValidationException(NativeToolValidationFailure.InvalidPath, current);
        }

        return path;
    }
}
