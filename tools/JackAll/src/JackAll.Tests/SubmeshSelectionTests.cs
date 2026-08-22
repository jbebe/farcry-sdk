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

    [Fact]
    public void One_damage_state_draws_not_all_of_them()
    {
        int withStates = 0;
        List<string> failures = [];

        foreach (string path in Fc2Corpus.Find(".xbg"))
        {
            XbgModel model = WorldModels.Triangulate(path, File.ReadAllBytes(path));
            if (!model.Submeshes.Any(s => StateToken().IsMatch(s.PartName)))
            {
                continue;
            }

            withStates++;
            foreach (int lod in model.LodLevels)
            {
                // Every part left standing must be the only state of its group.
                IEnumerable<IGrouping<string, int>> states = WorldModels.SubmeshesAt(model, lod)
                    .Where(s => StateToken().IsMatch(s.PartName))
                    .GroupBy(
                        s => StateToken().Replace(s.PartName, ""),
                        s => int.Parse(StateToken().Match(s.PartName).Groups[1].Value),
                        StringComparer.OrdinalIgnoreCase);

                foreach (IGrouping<string, int> group in states.Where(g => g.Distinct().Count() > 1))
                {
                    failures.Add(
                        $"{Path.GetFileName(path)} LOD{lod}: '{group.Key}' draws states "
                        + string.Join(", ", group.Distinct().Order()));
                }
            }
        }

        Assert.True(
            withStates > 0 || !Fc2Corpus.Present,
            "No shipped mesh carries a damage state, so this never exercised the case.");
        Assert.True(
            failures.Count == 0,
            $"{withStates} meshes carry damage states. First failures:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(5)));
    }

    /// <summary>
    /// A part absent from the selected LOD falls back to its nearest, rather than vanishing the way
    /// an exact match on the level drops it.
    /// </summary>
    [Fact]
    public void Every_part_draws_at_every_lod()
    {
        int recovered = 0;
        List<string> failures = [];

        foreach (string path in Fc2Corpus.Find(".xbg"))
        {
            XbgModel model = WorldModels.Triangulate(path, File.ReadAllBytes(path));
            if (model.Submeshes.Count == 0)
            {
                continue;
            }

            int expected = WorldModels.SubmeshesAt(model, model.LodLevels[0])
                .Select(s => s.PartName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

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

                if (drawn != expected)
                {
                    failures.Add($"{Path.GetFileName(path)} LOD{lod}: {drawn} parts, expected {expected}");
                }
                recovered += Math.Max(0, drawn - exact);
            }
        }

        Assert.True(
            failures.Count == 0,
            $"First failures:{Environment.NewLine}" + string.Join(Environment.NewLine, failures.Take(5)));

        // If this were zero the fallback would never fire and the shared rule would be
        // indistinguishable from the exact filter it replaced.
        Assert.True(
            recovered > 0 || !Fc2Corpus.Present,
            "No part was ever recovered from a neighbouring LOD, so the fallback is untested.");
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".xbg").Any(), Fc2Corpus.MissingMessage(".xbg"));
}
