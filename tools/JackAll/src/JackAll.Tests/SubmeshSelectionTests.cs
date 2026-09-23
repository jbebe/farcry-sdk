using System.Text.RegularExpressions;
using JackAll.Tools.World;
using JackAll.Tools.Xbg;

namespace JackAll.Tests;

/// <summary>
/// What a mesh draws at a given LOD, which is the rule the map editor and the file viewer share.
/// </summary>
/// <remarks>
/// Filtering submeshes on their LOD level alone looks equivalent and is not, in two ways this pins
/// down: a destructible prop ships every damage state as its own part, and a part need not exist at
/// every tier. Both were live in the viewer before it moved onto this.
/// </remarks>
public sealed partial class SubmeshSelectionTests
{
    [GeneratedRegex(@"_?STATE(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex StateToken();

    [Theory]
    [InlineData(XbgFixtures.Fence)]
    [InlineData(XbgFixtures.SwampBoat)]
    public void One_damage_state_draws_not_all_of_them(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        XbgModel model = WorldModels.Triangulate(fixture, bytes);
        Assert.Contains(model.Submeshes, s => StateToken().IsMatch(s.PartName));

        foreach (int lod in model.LodLevels)
        {
            // Every part left standing must be the only state of its group.
            IEnumerable<IGrouping<string, int>> states = WorldModels.SubmeshesAt(model, lod)
                .Where(s => StateToken().IsMatch(s.PartName))
                .GroupBy(
                    s => StateToken().Replace(s.PartName, ""),
                    s => int.Parse(StateToken().Match(s.PartName).Groups[1].Value),
                    StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, int> group in states)
            {
                Assert.True(
                    group.Distinct().Count() == 1,
                    $"LOD{lod}: '{group.Key}' draws states {string.Join(", ", group.Distinct().Order())}");
            }
        }
    }

    /// <summary>
    /// A part absent from the selected LOD falls back to its nearest, rather than vanishing the way
    /// an exact match on the level drops it. Both meshes have parts missing from some of their LODs.
    /// </summary>
    [Theory]
    [InlineData(XbgFixtures.Fence)]
    [InlineData(XbgFixtures.Ak47)]
    public void Every_part_draws_at_every_lod(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        XbgModel model = WorldModels.Triangulate(fixture, bytes);
        int expected = WorldModels.SubmeshesAt(model, model.LodLevels[0])
            .Select(s => s.PartName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        int recovered = 0;
        foreach (int lod in model.LodLevels)
        {
            int drawn = WorldModels.SubmeshesAt(model, lod)
                .Select(s => s.PartName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            int exact = model.Submeshes
                .Where(s => s.LodLevel == lod && s.Indices.Length > 0)
                .Select(s => s.PartName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            Assert.True(drawn == expected, $"LOD{lod}: {drawn} parts, expected {expected}");
            recovered += Math.Max(0, drawn - exact);
        }

        // Zero would mean the fallback never fired, leaving it indistinguishable from the exact
        // filter it replaced.
        Assert.True(recovered > 0, "No part was recovered from a neighbouring LOD.");
    }
}
