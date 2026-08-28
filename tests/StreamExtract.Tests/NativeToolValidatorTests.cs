using System.Security.Cryptography;
using System.Text.Json;
using StreamExtract.Services;

namespace StreamExtract.Tests;

public sealed class NativeToolValidatorTests
{
    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public void ValidatesAValidTool()
    {
        using var fixture = Fixture.Create();
        var result = NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void RejectsMissingTool()
    {
        using var fixture = Fixture.Create(includeFile: false);
        var error = Assert.Throws<NativeToolValidationException>(() => NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest));
        Assert.Equal(NativeToolValidationFailure.Missing, error.Failure);
    }

    [Fact]
    public void RejectsWrongHash()
    {
        using var fixture = Fixture.Create(hash: new string('0', 64));
        var error = Assert.Throws<NativeToolValidationException>(() => NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest));
        Assert.Equal(NativeToolValidationFailure.HashMismatch, error.Failure);
    }

    [Fact]
    public void RejectsMalformedManifest()
    {
        using var fixture = Fixture.Create();
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, "tools-manifest.json"), "not json");
        var error = Assert.Throws<NativeToolValidationException>(() => NativeToolManifest.Load(fixture.DirectoryPath));
        Assert.Equal(NativeToolValidationFailure.InvalidManifest, error.Failure);
    }

    [Fact]
    public void RejectsNullManifestRecordAsInvalidManifest()
    {
        var manifest = new NativeToolManifest([null!]);

        var error = Assert.Throws<NativeToolValidationException>(() =>
            NativeToolValidator.Validate(Path.GetTempPath(), manifest));

        Assert.Equal(NativeToolValidationFailure.InvalidManifest, error.Failure);
    }

    [Fact]
    public void RejectsWrongRid()
    {
        using var fixture = Fixture.Create(rid: "not-this-platform");
        var error = Assert.Throws<NativeToolValidationException>(() => NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest));
        Assert.Equal(NativeToolValidationFailure.WrongPlatform, error.Failure);
    }

    [Fact]
    public void RejectsNonExecutableLinuxTool()
    {
        using var fixture = Fixture.Create();
        var path = Path.Combine(fixture.DirectoryPath, "tools", NativeTool.GetFilename(NativeToolId.MkvMerge));
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, File.GetUnixFileMode(path) & ~UnixFileMode.UserExecute);
        var error = Assert.Throws<NativeToolValidationException>(() => NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest));
        Assert.Equal(NativeToolValidationFailure.NotExecutable, error.Failure);
    }

    [Fact]
    public void RejectsPathTraversal()
    {
        using var fixture = Fixture.Create(filename: "../outside");
        var error = Assert.Throws<NativeToolValidationException>(() => NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest));
        Assert.Equal(NativeToolValidationFailure.InvalidPath, error.Failure);
    }

    [Theory]
    [InlineData("sub/tool")]
    [InlineData("sub\\tool")]
    [InlineData("/tool")]
    [InlineData("\\tool")]
    [InlineData("C:/tool")]
    [InlineData("C:\\tool")]
    public void RejectsCrossPlatformOrRootedFilename(string filename)
    {
        using var fixture = Fixture.Create(filename: filename);
        var error = Assert.Throws<NativeToolValidationException>(() => NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest));
        Assert.Equal(NativeToolValidationFailure.InvalidPath, error.Failure);
    }

    [Fact]
    public void RejectsMalformedHash()
    {
        using var fixture = Fixture.Create(hash: "bad");
        var error = Assert.Throws<NativeToolValidationException>(() => NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest));
        Assert.Equal(NativeToolValidationFailure.MalformedHash, error.Failure);
    }

    [Fact]
    public void RejectsEmptyMetadata()
    {
        using var fixture = Fixture.Create(version: "");
        var error = Assert.Throws<NativeToolValidationException>(() => NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest));
        Assert.Equal(NativeToolValidationFailure.EmptyMetadata, error.Failure);
    }

    [Fact]
    public void RejectsUnknownLogicalId()
    {
        using var fixture = Fixture.Create(unknown: true);
        var error = Assert.Throws<NativeToolValidationException>(() => NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest));
        Assert.Equal(NativeToolValidationFailure.UnknownId, error.Failure);
    }

    [Fact]
    public void RejectsDuplicateRecords()
    {
        using var fixture = Fixture.Create(duplicate: true);
        var error = Assert.Throws<NativeToolValidationException>(() => NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest));
        Assert.Equal(NativeToolValidationFailure.Duplicate, error.Failure);
    }

    [Fact]
    public void ValidatesBundledRuntimeArtifacts()
    {
        using var fixture = Fixture.Create();
        var artifactDirectory = Path.Combine(fixture.DirectoryPath, "tools", "lib");
        Directory.CreateDirectory(artifactDirectory);
        var artifactPath = Path.Combine(artifactDirectory, "libgpac.so.12");
        File.WriteAllText(artifactPath, "runtime library");
        var artifact = new NativeToolManifestArtifact(
            "tools/lib/libgpac.so.12", "test", NativeTool.CurrentRid, "test",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(artifactPath))), false);
        var manifest = fixture.Manifest with { Artifacts = [artifact] };

        NativeToolValidator.Validate(fixture.DirectoryPath, manifest);
    }

    [Fact]
    public void RejectsMissingBundledRuntimeArtifact()
    {
        using var fixture = Fixture.Create();
        var manifest = fixture.Manifest with
        {
            Artifacts = [new NativeToolManifestArtifact(
                "tools/lib/missing.so", "test", NativeTool.CurrentRid, "test", new string('0', 64), false)]
        };

        var error = Assert.Throws<NativeToolValidationException>(() =>
            NativeToolValidator.Validate(fixture.DirectoryPath, manifest));

        Assert.Equal(NativeToolValidationFailure.Missing, error.Failure);
    }

    [Fact]
    public void RejectsSymlinkedBundledRuntimeArtifact()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = Fixture.Create();
        var targetPath = Path.Combine(fixture.DirectoryPath, "outside.so");
        var artifactPath = Path.Combine(fixture.DirectoryPath, "tools", "libgpac.so.12");
        File.WriteAllText(targetPath, "outside runtime library");
        File.CreateSymbolicLink(artifactPath, targetPath);
        var artifact = new NativeToolManifestArtifact(
            "tools/libgpac.so.12", "test", NativeTool.CurrentRid, "test", Hash(targetPath), false);

        var error = Assert.Throws<NativeToolValidationException>(() =>
            NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest with { Artifacts = [artifact] }));

        Assert.Equal(NativeToolValidationFailure.InvalidPath, error.Failure);
    }

    [Fact]
    public void RejectsSymlinkedToolParentDirectory()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = Fixture.Create();
        var realTools = Path.Combine(fixture.DirectoryPath, "real-tools");
        Directory.Move(Path.Combine(fixture.DirectoryPath, "tools"), realTools);
        File.CreateSymbolicLink(Path.Combine(fixture.DirectoryPath, "tools"), realTools);

        var error = Assert.Throws<NativeToolValidationException>(() =>
            NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest));

        Assert.Equal(NativeToolValidationFailure.InvalidPath, error.Failure);
    }

    [Fact]
    public void RejectsSymlinkedArtifactParentDirectory()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = Fixture.Create();
        var realLib = Path.Combine(fixture.DirectoryPath, "real-lib");
        Directory.CreateDirectory(realLib);
        var artifactPath = Path.Combine(realLib, "runtime.so");
        File.WriteAllText(artifactPath, "runtime library");
        var toolsLib = Path.Combine(fixture.DirectoryPath, "tools", "lib");
        File.CreateSymbolicLink(toolsLib, realLib);
        var artifact = new NativeToolManifestArtifact(
            "tools/lib/runtime.so", "test", NativeTool.CurrentRid, "test", Hash(artifactPath), false);

        var error = Assert.Throws<NativeToolValidationException>(() =>
            NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest with { Artifacts = [artifact] }));

        Assert.Equal(NativeToolValidationFailure.InvalidPath, error.Failure);
    }

    [Fact]
    public void RejectsArtifactHashMismatch()
    {
        using var fixture = Fixture.Create();
        var artifactPath = Path.Combine(fixture.DirectoryPath, "tools", "libgpac.so.12");
        File.WriteAllText(artifactPath, "runtime library");
        var artifact = new NativeToolManifestArtifact(
            "tools/libgpac.so.12", "test", NativeTool.CurrentRid, "test", new string('0', 64), false);

        var error = Assert.Throws<NativeToolValidationException>(() =>
            NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest with { Artifacts = [artifact] }));

        Assert.Equal(NativeToolValidationFailure.HashMismatch, error.Failure);
    }

    [Fact]
    public void RejectsArtifactMarkedExecutableWithoutExecutePermission()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = Fixture.Create();
        var artifactPath = Path.Combine(fixture.DirectoryPath, "tools", "runtime-helper");
        File.WriteAllText(artifactPath, "runtime helper");
        var artifact = new NativeToolManifestArtifact(
            "tools/runtime-helper", "test", NativeTool.CurrentRid, "test", Hash(artifactPath), true);

        var error = Assert.Throws<NativeToolValidationException>(() =>
            NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest with { Artifacts = [artifact] }));

        Assert.Equal(NativeToolValidationFailure.NotExecutable, error.Failure);
    }

    [Fact]
    public void RejectsUnlistedRegularToolFile()
    {
        using var fixture = Fixture.Create();
        var unlistedPath = Path.Combine(fixture.DirectoryPath, "tools", "unlisted-tool");
        File.WriteAllText(unlistedPath, "unlisted");

        var error = Assert.Throws<NativeToolValidationException>(() =>
            NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest));

        Assert.Equal(NativeToolValidationFailure.InvalidManifest, error.Failure);
    }

    [Fact]
    public void RejectsLogicalToolAndArtifactSharingPath()
    {
        using var fixture = Fixture.Create();
        var path = Path.Combine(fixture.DirectoryPath, "tools", NativeTool.GetFilename(NativeToolId.MkvMerge));
        var artifact = new NativeToolManifestArtifact(
            $"tools/{NativeTool.GetFilename(NativeToolId.MkvMerge)}", "test", NativeTool.CurrentRid, "test", Hash(path), true);

        var error = Assert.Throws<NativeToolValidationException>(() =>
            NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest with { Artifacts = [artifact] }));

        Assert.Equal(NativeToolValidationFailure.Duplicate, error.Failure);
    }

    [Fact]
    public void RejectsDirectoryAtManifestedToolPath()
    {
        using var fixture = Fixture.Create(includeFile: false);
        Directory.CreateDirectory(Path.Combine(fixture.DirectoryPath, "tools", NativeTool.GetFilename(NativeToolId.MkvMerge)));

        var error = Assert.Throws<NativeToolValidationException>(() =>
            NativeToolValidator.Validate(fixture.DirectoryPath, fixture.Manifest));

        Assert.Equal(NativeToolValidationFailure.InvalidPath, error.Failure);
    }

    private sealed class Fixture : IDisposable
    {
        public string DirectoryPath { get; }
        public NativeToolManifest Manifest { get; }
        private Fixture(string directoryPath, NativeToolManifest manifest) { DirectoryPath = directoryPath; Manifest = manifest; }

        public static Fixture Create(bool includeFile = true, string? hash = null, string? rid = null, string? filename = null, bool duplicate = false, string? version = null, bool unknown = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), "streamextract-validator-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "tools"));
            var actualFilename = NativeTool.GetFilename(NativeToolId.MkvMerge);
            var path = Path.Combine(directory, "tools", actualFilename);
            if (includeFile)
            {
                File.WriteAllText(path, "tool");
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
            }
            var record = new NativeToolManifestRecord(NativeToolId.MkvMerge, filename ?? actualFilename, "test", rid ?? NativeTool.CurrentRid, "test",
                hash ?? Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("tool"))));
            var records = new List<NativeToolManifestRecord> { record with { Version = version ?? "test" } };
            if (duplicate) records.Add(record);
            if (unknown) records.Add(record with { Id = (NativeToolId)999 });
            foreach (var id in NativeTool.RequiredIds.Where(id => id != NativeToolId.MkvMerge))
            {
                var otherFilename = NativeTool.GetFilename(id);
                var otherPath = Path.Combine(directory, "tools", otherFilename);
                File.WriteAllText(otherPath, "tool");
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(otherPath, File.GetUnixFileMode(otherPath) | UnixFileMode.UserExecute);
                records.Add(new(id, otherFilename, "test", NativeTool.CurrentRid, "test", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(otherPath)))));
            }
            var manifest = new NativeToolManifest(records);
            File.WriteAllText(Path.Combine(directory, "tools-manifest.json"), JsonSerializer.Serialize(manifest));
            return new Fixture(directory, manifest);
        }

        public void Dispose() => Directory.Delete(DirectoryPath, true);

    }
}
