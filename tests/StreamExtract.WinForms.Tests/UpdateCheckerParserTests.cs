using System.Text.Json;
using Xunit;

namespace StreamExtract.WinForms.Tests;

public class UpdateCheckerParserTests
{
    [Theory]
    [InlineData("{\"version\":\"1.2.3\"}", "1.2.3")]
    [InlineData("\"1.2.3\"", "1.2.3")]
    [InlineData("{\"latest\":\"v2.0.0-rc1\"}", "2.0.0")]
    [InlineData("\"no version here\"", "")]
    public void ParseCudacoderUpdate_ExtractsVersionFromStringOrObject(string body, string expected)
    {
        using var json = JsonDocument.Parse(body);
        var (version, url) = StreamExtract.Form1.ParseCudacoderUpdate(json);

        Assert.Equal(expected, version);
        Assert.Equal("https://cudacoder.com", url);
    }
}
