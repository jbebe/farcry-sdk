using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// The Map tab resolves placed entities against the library single-player actually reads, the
/// suffix-less one (see docs/docs/engine-internals/entity-instancing.md). That is only safe if a
/// campaign sector places nothing the smaller library lacks.
/// </summary>
public class ArchetypeChainTests
{
    /// <summary>world1's single-player library alone, or null without the fixture.</summary>
    internal static readonly Lazy<ArchetypeIndex?> SinglePlayer = new(() =>
        Fixture.Read(FcbDocumentTests.World1) is { } library
            ? ArchetypeIndex.Load([ArchetypeIndex.BaseLayer("world1")], _ => library)
            : null);

    [Theory]
    [InlineData(WorldSectorFragmentTests.Sector4027)]
    [InlineData(WorldSectorFragmentTests.Sector3859)]
    public void Every_archetype_a_campaign_sector_places_resolves_in_the_single_player_library(string sector)
    {
        if (Fixture.Read(sector) is not { } bytes || SinglePlayer.Value is not { } single) return;

        List<string> placed = [.. FcbDocument.Deserialize(bytes).Children
            .SelectMany(layer => layer.Children)
            .Select(entity => FcbEntityFields.ReadString(entity, WorldHashes.TplCreatureType))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        Assert.NotEmpty(placed);

        List<string> missing = [.. placed.Where(n => single.Winner(n) is null).Order()];
        Assert.True(missing.Count == 0,
            $"{missing.Count} of {placed.Count} placed archetypes are not in world1's entitylibrary.fcb: "
            + string.Join(", ", missing));
    }
}
