using NovaVox.Core.Update;
using Xunit;

namespace NovaVox.Core.Tests.Update;

public class UpdateManifestTests
{
    [Fact]
    public void Parse_SimpleManifest_AvailableWhenNewer()
    {
        var result = UpdateManifest.Parse("""{"version": "0.3.0", "url": "https://novanox.1ercorpscolonial.fr/NovaVoxNET_Setup.exe"}""", "0.2.8");
        Assert.True(result.Available);
        Assert.Equal("0.3.0", result.Version);
        Assert.Equal("https://novanox.1ercorpscolonial.fr/NovaVoxNET_Setup.exe", result.Url);
    }

    [Fact]
    public void Parse_SimpleManifest_NotAvailableWhenSameVersion()
    {
        var result = UpdateManifest.Parse("""{"version": "0.2.8", "url": "https://novanox.1ercorpscolonial.fr/NovaVoxNET_Setup.exe"}""", "0.2.8");
        Assert.False(result.Available);
    }

    [Fact]
    public void Parse_GitHubReleaseFormat_ExtractsVersionAndExeAsset()
    {
        var json = """
        {
            "tag_name": "v0.3.0",
            "assets": [
                {"name": "source.zip", "browser_download_url": "https://github.com/ammoniak07/NovaVox_2/archive/source.zip"},
                {"name": "NovaVox_Setup.exe", "browser_download_url": "https://github.com/ammoniak07/NovaVox_2/releases/download/v0.3.0/NovaVox_Setup.exe"}
            ]
        }
        """;
        var result = UpdateManifest.Parse(json, "0.2.8");
        Assert.True(result.Available);
        Assert.Equal("0.3.0", result.Version);
        Assert.Equal("https://github.com/ammoniak07/NovaVox_2/releases/download/v0.3.0/NovaVox_Setup.exe", result.Url);
    }

    [Fact]
    public void Parse_ReturnsNotAvailableOnMalformedJson()
    {
        Assert.False(UpdateManifest.Parse("not json", "0.2.8").Available);
    }

    [Theory]
    [InlineData("http://novanox.1ercorpscolonial.fr/NovaVoxNET_Setup.exe")]
    [InlineData("https://example.com/setup.exe")]
    [InlineData("https://github.com.evil.com/setup.exe")]
    [InlineData("https://user@github.com/setup.exe")]
    [InlineData("https://github.com:8443/setup.exe")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("C:/Windows/System32/cmd.exe")]
    public void Parse_UntrustedUrl_IsDropped(string url)
    {
        var result = UpdateManifest.Parse($$"""{"version": "0.3.0", "url": "{{url}}"}""", "0.2.8");
        Assert.True(result.Available);
        Assert.Equal("", result.Url);
    }
}
