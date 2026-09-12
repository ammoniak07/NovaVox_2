using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class GameLogPhraseCatalogTests
{
    [Fact]
    public void Format_UsesDefaultTemplate_WhenNoCustomOne()
    {
        var text = GameLogPhraseCatalog.Format("route_set", new Dictionary<string, string>(), new Dictionary<string, string> { ["dest"] = "Hurston" });
        Assert.Equal("Route tracée vers Hurston", text);
    }

    [Fact]
    public void Format_UsesCustomTemplate_WhenValid()
    {
        var custom = new Dictionary<string, string> { ["route_set"] = "Direction {dest}" };
        var text = GameLogPhraseCatalog.Format("route_set", custom, new Dictionary<string, string> { ["dest"] = "Hurston" });
        Assert.Equal("Direction Hurston", text);
    }

    [Fact]
    public void Format_FallsBackToDefault_WhenCustomTemplateReferencesUnknownField()
    {
        var custom = new Dictionary<string, string> { ["route_set"] = "Direction {destinationTypo}" };
        var text = GameLogPhraseCatalog.Format("route_set", custom, new Dictionary<string, string> { ["dest"] = "Hurston" });
        Assert.Equal("Route tracée vers Hurston", text);
    }

    [Fact]
    public void Format_NoDestKey_HasNoPlaceholder()
    {
        var text = GameLogPhraseCatalog.Format("route_set_no_dest", new Dictionary<string, string>(), new Dictionary<string, string>());
        Assert.Equal("Route tracée", text);
    }
}
