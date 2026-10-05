namespace JackAll.Core.Legacy;

/// <summary>What a pick wrote, and what it could not carry into a layer.</summary>
/// <param name="Outside">Picked changes beside the archives - binary patches and loose files - which
/// a layer cannot deploy and need porting by hand.</param>
/// <param name="TakenWhole">Units written in full although only some of their changes were picked,
/// because they cannot be rebuilt in part.</param>
public sealed record LegacyPick(int Copied, int Merged, IReadOnlyList<LegacyChange> Outside, IReadOnlyList<string> TakenWhole);

/// <summary>
/// Builds an ordinary JackAll layer holding exactly the picked changes of an analyzed legacy mod,
/// each unit rebuilt from the base game and the mod's own version rather than from anything stored.
/// </summary>
public static class LegacyPicker
{
    public static LegacyPick Pick(LegacyUnits units, IReadOnlyList<LegacyChange> changes, IReadOnlySet<string> picked, string outDir)
    {
        int copied = 0, merged = 0;
        List<LegacyChange> outside = [];
        List<string> takenWhole = [];
        foreach (IGrouping<string, LegacyChange> unit in changes.GroupBy(c => c.Unit))
        {
            List<LegacyChange> chosen = [.. unit.Where(c => picked.Contains(c.Address))];
            if (chosen.Count == 0)
            {
                continue;
            }

            if (unit.Key.StartsWith("install/", StringComparison.Ordinal))
            {
                outside.AddRange(chosen);
                continue;
            }

            byte[] bytes;
            if (chosen.Count == unit.Count() || unit.Any(c => c.Whole))
            {
                bytes = units.Staged(unit.Key);
                copied++;
                if (chosen.Count != unit.Count())
                {
                    takenWhole.Add(unit.Key);
                }
            }
            else
            {
                bytes = units.Merge(unit.Key, picked.Contains);
                merged++;
            }

            string destination = Path.Combine(outDir, "mods", unit.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, bytes);
        }

        return new LegacyPick(copied, merged, outside, takenWhole);
    }
}
