using System.Security.Cryptography;
using System.Text.Json;
using StreamExtract.Services;

namespace StreamExtract.Tests;

public sealed class NativeToolResolverTests
{
    [Fact]
    public void ExpectedFilenameMatchesCurrentPlatform()
    {
        Assert.Equal(OperatingSystem.IsWindows() ? "mkvmerge.exe" : "mkvmerge",
            NativeTool.GetFilename(NativeToolId.MkvMerge));
        Assert.Equal(OperatingSystem.IsWindows() ? "mp4box.exe" : "MP4Box",
            NativeTool.GetFilename(NativeToolId.Mp4Box));
    }

    [Fact]
    public void ResolvesOnlyFromExplicitApplicationBaseDirectory()
    {
        using var fixture = ToolFixture.Create();
        var path = new NativeToolResolver(fixture.DirectoryPath).Resolve(NativeToolId.MkvMerge);

        Assert.Equal(Path.Combine(fixture.DirectoryPath, "tools", NativeTool.GetFilename(NativeToolId.MkvMerge)), path);
    }

    [Fact]
    public void DoesNotFallBackToPath()
    {
        using var fixture = ToolFixture.Create(includeMkvMerge: false);
        var error = Assert.Throws<NativeToolValidationException>(() => new NativeToolResolver(fixture.DirectoryPath));
        Assert.Equal(NativeToolValidationFailure.MissingRequired, error.Failure);
    }

    [Fact]
    public void RejectsUnknownLogicalId()
    {
        using var fixture = ToolFixture.Create();
        var error = Assert.Throws<NativeToolValidationException>(() => new NativeToolResolver(fixture.DirectoryPath).Resolve((NativeToolId)999));
        Assert.Equal(NativeToolValidationFailure.UnknownId, error.Failure);
    }

    private sealed class ToolFixture : IDisposable
    {
        public string DirectoryPath { get; }
        private readonly string _toolPath;

        private ToolFixture(string directoryPath, string toolPath)
        {
            DirectoryPath = directoryPath;
            _toolPath = toolPath;
        }

        public static ToolFixture Create(bool includeMkvMerge = true)
        {
            var directory = Path.Combine(Path.GetTempPath(), "streamextract-tools-" + Guid.NewGuid().ToString("N"));
            var tools = Path.Combine(directory, "tools");
            Directory.CreateDirectory(tools);
            var records = new List<NativeToolManifestRecord>();
            foreach (var id in NativeTool.RequiredIds)
            {
                if (!includeMkvMerge && id == NativeToolId.MkvMerge) continue;
                var path = Path.Combine(tools, NativeTool.GetFilename(id));
                File.WriteAllText(path, "tool");
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
                records.Add(new(id, Path.GetFileName(path), "test", NativeTool.CurrentRid, "test", Hash(path)));
            }
            File.WriteAllText(Path.Combine(directory, "tools-manifest.json"), JsonSerializer.Serialize(new NativeToolManifest(records)));
            return new ToolFixture(directory, tools);
        }

        private static string Hash(string path)
            => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
}
