using NovaVox.Core.Update;
using Xunit;

namespace NovaVox.Core.Tests.Update;

public class UpdateManifestTests
{
    [Fact]
    public void Parse_SimpleManifest_AvailableWhenNewer()
    {
        var result = UpdateManifest.Parse("""{"version": "0.3.0", "url": "https://example.com/setup.exe"}""", "0.2.8");
        Assert.True(result.Available);
        Assert.Equal("0.3.0", result.Version);
        Assert.Equal("https://example.com/setup.exe", result.Url);
    }

    [Fact]
    public void Parse_SimpleManifest_NotAvailableWhenSameVersion()
    {
        var result = UpdateManifest.Parse("""{"version": "0.2.8", "url": "https://example.com/setup.exe"}""", "0.2.8");
        Assert.False(result.Available);
    }

    [Fact]
    public void Parse_GitHubReleaseFormat_ExtractsVersionAndExeAsset()
    {
        var json = """
        {
            "tag_name": "v0.3.0",
            "assets": [
                {"name": "source.zip", "browser_download_url": "https://example.com/source.zip"},
                {"name": "NovaVox_Setup.exe", "browser_download_url": "https://example.com/NovaVox_Setup.exe"}
            ]
        }
        """;
        var result = UpdateManifest.Parse(json, "0.2.8");
        Assert.True(result.Available);
        Assert.Equal("0.3.0", result.Version);
        Assert.Equal("https://example.com/NovaVox_Setup.exe", result.Url);
    }

    [Fact]
    public void Parse_ReturnsNotAvailableOnMalformedJson()
    {
        Assert.False(UpdateManifest.Parse("not json", "0.2.8").Available);
    }
}
