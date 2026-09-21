using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// The Map tab resolves placed entities against the library single-player actually reads, the
/// suffix-less one (see docs/docs/engine-internals/entity-instancing.md). That is only safe if the
/// campaign places nothing the smaller library lacks.
/// </summary>
[Trait("Category", "RequiresFixture")]
public class ArchetypeChainTests
{
    private static readonly string ArchiveRoot = Path.Combine(Fc2Corpus.Root, "worlds", "worlds");

    [Fact]
    public void The_campaign_export_was_actually_found()
        => Assert.True(Directory.Exists(Path.Combine(ArchiveRoot, "levels")),
            $"{ArchiveRoot} holds no levels - the chain test silently no-opped.");

    [Theory]
    [InlineData("world1", "w1_")]
    [InlineData("world2", "w2_")]
    public void Every_archetype_the_campaign_places_resolves_in_the_single_player_library(string world, string cellPrefix)
    {
        string levels = Path.Combine(ArchiveRoot, "levels");
        if (!Directory.Exists(levels)) return;

        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string cell in Directory.EnumerateDirectories(levels, cellPrefix + "*"))
        {
            string sectors = Path.Combine(cell, "generated", "worldsectors");
            if (!Directory.Exists(sectors))
            {
                continue;
            }
            foreach (string file in Directory.EnumerateFiles(sectors, "worldsector*.data.fcb"))
            {
                if (FcbDocument.TryDeserialize(File.ReadAllBytes(file)) is not { } root)
                {
                    continue;
                }
                foreach (FcbObject entity in root.Children.SelectMany(layer => layer.Children))
                {
                    if (FcbEntityFields.ReadString(entity, WorldHashes.TplCreatureType) is { Length: > 0 } name)
                    {
                        placed.Add(name);
                    }
                }
            }
        }
        Assert.NotEmpty(placed);

        byte[]? Read(string path) => File.Exists(Path.Combine(ArchiveRoot, path)) ? File.ReadAllBytes(Path.Combine(ArchiveRoot, path)) : null;
        ArchetypeIndex single = ArchetypeIndex.Load(world, Read);

        List<string> missing = [.. placed.Where(n => single.Winner(n) is null).Order()];
        Assert.True(missing.Count == 0,
            $"{missing.Count} of {placed.Count} placed archetypes are not in {world}'s entitylibrary.fcb: "
            + string.Join(", ", missing.Take(20)));
    }
}
