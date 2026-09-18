using NovaVox.Core.GameLog;
using Xunit;

namespace NovaVox.Core.Tests.GameLog;

public class GameLogDestinationsTests
{
    // Valeurs de référence calculées directement avec game_log_watcher.py
    // (voir historique de session) pour garantir la parité de comportement.
    [Theory]
    [InlineData("ObjectContainer_RestStop", "RestStop")]
    [InlineData("rs_ext_pyro-stan_jp1", "Stanton Gateway")]
    [InlineData("OOC_Stanton_1d_Ita", "Ita")]
    [InlineData("ab_mine_stanton3_med_005", "Base minière DYV-JKE")]
    [InlineData("MISSION_QT_Quantum_Beacon_732699457697", "Quantum Beacon")]
    [InlineData("rs_ext_cru-leo1", "Seraphim Station")]
    public void HumanizeDestination_MatchesPythonReference(string rawId, string expected)
    {
        Assert.Equal(expected, GameLogDestinations.HumanizeDestination(rawId));
    }

    [Fact]
    public void DestinationAliasKey_ReturnsNullForAmbiguousId()
    {
        Assert.Null(GameLogDestinations.DestinationAliasKey("ObjectContainer_RestStop"));
    }

    [Fact]
    public void DestinationAliasKey_ReturnsNormalizedKeyForKnownId()
    {
        Assert.Equal("ooc stanton 1d ita", GameLogDestinations.DestinationAliasKey("OOC_Stanton_1d_Ita"));
    }

    [Theory]
    [InlineData("ObjectContainer_RestStop", false)]
    [InlineData("OOC_Stanton_1d_Ita", false)]
    [InlineData("some_unknown_thing", true)]
    public void DestinationIsUnresolved_MatchesPythonReference(string rawId, bool expected)
    {
        Assert.Equal(expected, GameLogDestinations.DestinationIsUnresolved(rawId));
    }

    [Fact]
    public void ResolveDestinationLabel_UsesGenericObstructionAsStationLookup()
    {
        Assert.Equal("Baijini Point", GameLogDestinations.ResolveDestinationLabel("ObjectContainer_RestStop", obstructionLabel: "ArcCorp"));
    }

    [Fact]
    public void ResolveDestinationLabel_UsesSpecificObstructionLabelVerbatim()
    {
        Assert.Equal("Base minière #ODD-E9B", GameLogDestinations.ResolveDestinationLabel("ObjectContainer_RestStop", obstructionLabel: "Base minière #ODD-E9B"));
    }

    [Fact]
    public void ResolveDestinationLabel_FallsBackToStartLocationGuessForAmbiguousId()
    {
        Assert.Equal("Everus Harbor", GameLogDestinations.ResolveDestinationLabel("ObjectContainer_RestStop", startLocation: "Lorville"));
    }

    [Fact]
    public void ExtractShipName_StripsInstanceSuffixAndUnderscores()
    {
        Assert.Equal("ANVL Hornet F7C Mk2", GameLogDestinations.ExtractShipName("ANVL_Hornet_F7C_Mk2_1234567890123"));
    }

    [Fact]
    public void FixMojibake_RepairsDoubleEncodedAccents()
    {
        Assert.Equal("terminé", GameLogText.FixMojibake("terminÃ©"));
    }

    [Fact]
    public void FixMojibake_LeavesAlreadyCorrectTextUnchanged()
    {
        Assert.Equal("café", GameLogText.FixMojibake("café"));
    }

    [Fact]
    public void PruneAliasesCoveredByCatalog_RemovesEntriesAlreadyInKnownLocationAliases()
    {
        var userAliases = new Dictionary<string, string>
        {
            ["ooc stanton 1 hurston"] = "Hurston", // déjà dans le catalogue
            ["ab collector gas stanton4"] = "Wikelo emporium kinga station", // idem, texte périmé
            ["ma station perso"] = "Ma station perso", // lieu vraiment inconnu, à garder
        };

        var changed = GameLogDestinations.PruneAliasesCoveredByCatalog(userAliases);

        Assert.True(changed);
        Assert.Single(userAliases);
        Assert.True(userAliases.ContainsKey("ma station perso"));
    }

    [Fact]
    public void PruneAliasesCoveredByCatalog_ReturnsFalseWhenNothingToRemove()
    {
        var userAliases = new Dictionary<string, string> { ["ma station perso"] = "Ma station perso" };

        var changed = GameLogDestinations.PruneAliasesCoveredByCatalog(userAliases);

        Assert.False(changed);
        Assert.Single(userAliases);
    }

    [Fact]
    public void PruneAliasesCoveredByCatalog_RemovesJumpPointResolvableEntries()
    {
        // Points de saut absents de KnownLocationAliases mais déjà résolus par
        // l'algorithme (JumpPointIdRegex + SystemNames) — des entrées enregistrées
        // avant le correctif de MaybeRegisterDestinationAlias, dont une avec un
        // texte devenu faux ("Stanton Gateway" au lieu de "Castra Gateway").
        var userAliases = new Dictionary<string, string>
        {
            ["rs ext pyro stan jp1"] = "Stanton Gateway",
            ["rs ext nyx pyro jp1"] = "Pyro Gateway",
            ["rs ext nyx castra jp1"] = "Stanton Gateway", // faux, doit être retiré pour laisser l'algorithme reprendre la main
            ["ma station perso"] = "Ma station perso",
        };

        var changed = GameLogDestinations.PruneAliasesCoveredByCatalog(userAliases);

        Assert.True(changed);
        Assert.Single(userAliases);
        Assert.True(userAliases.ContainsKey("ma station perso"));
    }

    [Theory]
    [InlineData("MISSION_QT_Bounty_Beacon_816657711603", "mission qt bounty beacon")]
    [InlineData("NavPoint_Dynamic_811091209650", "navpoint dynamic")]
    [InlineData("ab_mine_stanton3_med_005", "ab mine stanton3 med 005")] // suffixe court (3 chiffres) : pas un numéro d'instance, conservé
    public void NormalizeForAliasLookup_StripsLongInstanceSuffixFromKey(string rawId, string expectedKey)
    {
        Assert.Equal(expectedKey, GameLogDestinations.NormalizeForAliasLookup(rawId));
    }

    [Fact]
    public void CollapseInstanceSuffixedAliases_MergesDuplicatesIntoCanonicalKey()
    {
        var userAliases = new Dictionary<string, string>
        {
            ["mission qt bounty beacon 816657711603"] = "Bounty Beacon",
            ["mission qt bounty beacon 816663687671"] = "Bounty Beacon",
            ["partymembermarker 781819795315"] = "PartyMemberMarker",
            ["partymembermarker 781800303699"] = "PartyMemberMarker",
            ["ma station perso"] = "Ma station perso",
        };

        var changed = GameLogDestinations.CollapseInstanceSuffixedAliases(userAliases);

        Assert.True(changed);
        Assert.Equal(3, userAliases.Count);
        Assert.True(userAliases.ContainsKey("mission qt bounty beacon"));
        Assert.True(userAliases.ContainsKey("partymembermarker"));
        Assert.True(userAliases.ContainsKey("ma station perso"));
    }

    [Fact]
    public void CollapseInstanceSuffixedAliases_ReturnsFalseWhenNothingToCollapse()
    {
        var userAliases = new Dictionary<string, string> { ["ma station perso"] = "Ma station perso" };

        var changed = GameLogDestinations.CollapseInstanceSuffixedAliases(userAliases);

        Assert.False(changed);
        Assert.Single(userAliases);
    }
}
