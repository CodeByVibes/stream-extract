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

            var path = Path.GetFullPath(Path.Combine(applicationBaseDirectory, "tools", record.Filename));
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
        return result;
    }
}
