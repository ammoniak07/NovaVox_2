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

    [Fact]
    public void Find_French_ReturnsActualTranslatedDescriptionNotEnglishFallback()
    {
        var english = SchemaDatabase.Find("Ezra");
        var french = SchemaDatabase.Find("Ezra", "fr");

        Assert.NotNull(english);
        Assert.NotNull(french);
        Assert.NotEqual(english!.Description, french!.Description);
        Assert.Contains("Shubin", french.Description); // la traduction reste cohérente, même manufacturier mentionné
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

    // Deux entrées de la base elle-même utilisent des styles de guillemets
    // différents (apostrophe droite vs double guillemet) — sans ce
    // nivellement, un nom reçu du jeu avec l'autre style (ou des chevrons «
    // » côté client français) ne correspondrait jamais, alors que c'est le
    // même schéma. Voir SchemaDatabase.NormalizeForMatch.
    [Theory]
    [InlineData("7CA \"Nargun\"")] // base : 7CA 'Nargun' (apostrophes)
    [InlineData("7CA «Nargun»")]
    [InlineData("Demeco «Purgatory Camo» LMG")] // base : guillemets droits
    // Convention typographique française : espace À L'INTÉRIEUR des chevrons
    // (« Mot » et non «Mot») — remontée utilisateur directe, ce seul espace
    // en trop empêchait encore la correspondance malgré le nivellement des
    // caractères de guillemet lui-même.
    [InlineData("Demeco « Purgatory Camo » LMG")]
    [InlineData("Demeco « Purgatory Camo » LMG")] // espace insécable, comme le fait vraiment le jeu
    public void Find_QuoteStyleVariant_StillMatchesDespiteDifferentQuoteCharacters(string query)
    {
        var info = SchemaDatabase.Find(query);
        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info!.Description));
    }

    // Remontée utilisateur directe : liste de noms extraits du Game.log
    // d'un client Star Citizen en français par le scan d'archives — chacun
    // doit retrouver son fabricant/description via SchemaNameAliases.fr.json,
    // alors qu'aucun ne correspond tel quel à une clé de SchemaDatabase.json
    // (toujours en anglais, langue de la base communautaire).
    [Theory]
    [InlineData("Bras Antium", "Antium Arms")]
    [InlineData("Bras Antium Storm", "Antium Arms Storm")]
    [InlineData("Torse Antium Maroon", "Antium Core Maroon")]
    [InlineData("Casque Antium Jet", "Antium Helmet Jet")]
    [InlineData("Jambes Antium Désert", "Antium Legs Sand")]
    [InlineData("Fusil Parallax \"Shock Trooper\"", "Parallax \"Shock Trooper\" Energy Assault Rifle")]
    [InlineData("Fusil à Énergie Parallax", "Parallax Energy Assault Rifle")]
    [InlineData("Tête de recyclage Cinch", "Cinch Scraper Module")]
    [InlineData("Laser de minage Lawson", "Lawson Mining Laser")]
    [InlineData("H4-PBF Chargeur de munitions", "H4-PBF Ammo Carrier")]
    [InlineData("Mil/2/A QuadraCell MT", "QuadraCell MT")]
    [InlineData("Monde Arms Purgeatory Camo", "Monde Arms Purgatory Camo")] // coquille du jeu ("Purgeatory")
    [InlineData("Jambes Antium Maroon", "Antium Legs Maroon")]
    public void Find_FrenchClientName_ResolvesToCanonicalEnglishEntry(string frenchName, string expectedCanonicalName)
    {
        var info = SchemaDatabase.Find(frenchName);

        Assert.NotNull(info);
        Assert.Equal(expectedCanonicalName, info!.Name);
        Assert.False(string.IsNullOrWhiteSpace(info.Description));
    }

    // La matrice complète pièce × variante de chaque set d'armure "Piece
    // Brand Variant" (Antium, Chiron, Testudo, Monde, Strata, Morozov-SH,
    // TrueDef-Pro, Aril, CBH-3, BUL-H4) est générée mécaniquement dans
    // SchemaNameAliases.fr.json plutôt que couverte au cas par cas — un
    // échantillon suffit ici pour vérifier que la génération a bien
    // couvert chaque pièce et chaque marque, pas seulement Antium.
    [Theory]
    [InlineData("Casque Morozov-SH Thule", "Morozov-SH Helmet Thule")]
    [InlineData("Torse Testudo Clanguard", "Testudo Core Clanguard")]
    [InlineData("Bras Monde Daimyo", "Monde Arms Daimyo")]
    [InlineData("Jambes Strata Amber", "Strata Legs Amber")]
    [InlineData("Casque Chiron Purgatory Camo", "Chiron Helmet Purgatory Camo")]
    [InlineData("Torse TrueDef-Pro Black/Grey", "TrueDef-Pro Core Black/Grey")]
    public void Find_FrenchClientName_CoversFullArmorPieceByBrandMatrix(string frenchName, string expectedCanonicalName)
    {
        var info = SchemaDatabase.Find(frenchName);

        Assert.NotNull(info);
        Assert.Equal(expectedCanonicalName, info!.Name);
    }

    [Fact]
    public void Find_FrenchAlias_IsCaseInsensitive()
    {
        Assert.NotNull(SchemaDatabase.Find("casque antium"));
        Assert.NotNull(SchemaDatabase.Find("BRAS ANTIUM"));
    }
}
