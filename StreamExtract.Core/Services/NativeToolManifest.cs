using System.Text.Json;
using System.Text.Json.Serialization;

namespace StreamExtract.Services;

public sealed record NativeToolManifestRecord(
    NativeToolId Id,
    string Filename,
    string Version,
    string Rid,
    string Source,
    string Sha256);

public sealed record NativeToolManifestArtifact(
    string Path,
    string Version,
    string Rid,
    string Source,
    string Sha256,
    bool Executable);

public sealed record NativeToolManifest(
    IReadOnlyList<NativeToolManifestRecord> Tools,
    IReadOnlyList<NativeToolManifestArtifact>? Artifacts = null)
{
    public static NativeToolManifest Load(string applicationBaseDirectory)
    {
        var path = Path.Combine(applicationBaseDirectory, "tools-manifest.json");
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<NativeToolManifest>(stream, JsonOptions)
                ?? throw new JsonException("Manifest is null.");
        }
        catch (NativeToolValidationException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new NativeToolValidationException(NativeToolValidationFailure.InvalidManifest, path, ex.Message);
        }
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
