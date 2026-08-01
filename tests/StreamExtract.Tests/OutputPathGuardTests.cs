using StreamExtract.Services;

namespace StreamExtract.Tests;

public class OutputPathGuardTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "se_guard_test");

    [Fact]
    public void SimpleFileName_ReturnsContainedFullPath()
    {
        var result = OutputPathGuard.ResolveContainedPath(Root, "cover.jpg");
        Assert.Equal(Path.GetFullPath(Path.Combine(Root, "cover.jpg")), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrNullFileName_Throws(string? fileName)
    {
        Assert.Throws<InvalidDataException>(() => OutputPathGuard.ResolveContainedPath(Root, fileName!));
    }

    [Fact]
    public void DirectoryTraversal_IsFlattenedToBaseName()
    {
        var result = OutputPathGuard.ResolveContainedPath(Root, @"..\outside.txt");
        Assert.Equal(Path.GetFullPath(Path.Combine(Root, "outside.txt")), result);
    }

    [Fact]
    public void AbsoluteWindowsPath_IsFlattenedToBaseName()
    {
        var result = OutputPathGuard.ResolveContainedPath(Root, @"C:\outside.txt");
        Assert.Equal(Path.GetFullPath(Path.Combine(Root, "outside.txt")), result);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    public void DotNames_AreRejected(string fileName)
    {
        Assert.Throws<InvalidDataException>(() => OutputPathGuard.ResolveContainedPath(Root, fileName));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("NUL")]
    [InlineData("AUX")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT9")]
    [InlineData("con.txt")]
    public void DeviceNames_AreRejected(string fileName)
    {
        Assert.Throws<InvalidDataException>(() => OutputPathGuard.ResolveContainedPath(Root, fileName));
    }

    [Theory]
    [InlineData("bad<name.txt")]
    [InlineData("bad|name.txt")]
    [InlineData("bad?name.txt")]
    public void InvalidFileNameChars_AreRejected(string fileName)
    {
        Assert.Throws<InvalidDataException>(() => OutputPathGuard.ResolveContainedPath(Root, fileName));
    }

    [Fact]
    public void NameLongerThan255_IsRejected()
    {
        var fileName = new string('a', 256) + ".txt";
        Assert.Throws<InvalidDataException>(() => OutputPathGuard.ResolveContainedPath(Root, fileName));
    }

    [Fact]
    public void EmptyOutputDirectory_Throws()
    {
        Assert.Throws<ArgumentException>(() => OutputPathGuard.ResolveContainedPath("", "a.txt"));
    }

    [Fact]
    public void NestedSafeName_UsesBaseNameOnly()
    {
        var result = OutputPathGuard.ResolveContainedPath(Root, @"sub\folder\file.txt");
        Assert.Equal(Path.GetFullPath(Path.Combine(Root, "file.txt")), result);
    }

    [Fact]
    public void Containment_IsCaseInsensitive()
    {
        var result = OutputPathGuard.ResolveContainedPath(Root, "a.txt");
        Assert.StartsWith(Path.GetFullPath(Root), result, StringComparison.OrdinalIgnoreCase);
    }
}
