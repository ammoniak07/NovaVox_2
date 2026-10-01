using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class SchemaDatabaseTests
{
    [Fact]
    public void Find_KnownSchema_ReturnsDescriptionAndManufacturer()
    {
        var info = SchemaDatabase.Find("Ezra");

        Assert.NotNull(info);
        Assert.Equal("Ezra", info!.Name);
        Assert.Equal("Shubin Interstellar", info.Manufacturer);
        Assert.False(string.IsNullOrWhiteSpace(info.Description));
        Assert.NotEmpty(info.Ingredients);
    }

    [Theory]
    [InlineData("ezra")]
    [InlineData("EZRA")]
    [InlineData("  Ezra  ")]
    public void Find_IsCaseInsensitiveAndTrims(string query)
    {
        Assert.NotNull(SchemaDatabase.Find(query));
    }

    [Fact]
    public void Find_UnknownSchema_ReturnsNull()
    {
        Assert.Null(SchemaDatabase.Find("Ce schéma n'existe vraiment pas"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Find_EmptyOrNull_ReturnsNull(string? query)
    {
        Assert.Null(SchemaDatabase.Find(query));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("nl")]
    [InlineData("es")]
    [InlineData("it")]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData(null)]
    [InlineData("xx")] // code inconnu : ne doit jamais faire planter la recherche
    public void Find_AnyLanguageCode_NeverLosesNameOrManufacturer(string? language)
    {
        var info = SchemaDatabase.Find("Ezra", language);

        Assert.NotNull(info);
        Assert.Equal("Ezra", info!.Name);
        Assert.Equal("Shubin Interstellar", info.Manufacturer);
        Assert.False(string.IsNullOrWhiteSpace(info.Description)); // traduit si dispo, sinon repli anglais — jamais vide
    }

    [Fact]
    public void Database_HasASubstantialNumberOfEntries()
    {
        // Vérifie que la ressource embarquée se charge réellement (pas juste
        // un dictionnaire vide suite à un échec silencieux) — voir quelques
        // noms connus plutôt qu'un total exact, pour ne pas casser ce test
        // à chaque régénération de la base.
        Assert.NotNull(SchemaDatabase.Find("Ezra"));
        Assert.NotNull(SchemaDatabase.Find("Deadbolt IV Cannon"));
        Assert.NotNull(SchemaDatabase.Find("NewDawn"));
    }
}
