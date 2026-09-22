namespace JackAll.Core.Mods;

/// <summary>One fragment whose text differs from the container it is compared against.</summary>
/// <param name="Added">True when the other container has no such fragment at all.</param>
public sealed record FragmentChange(string Id, string Xml, bool Added);

/// <summary>Which fragments of one container a mod has to stage to turn another into it.</summary>
public static class FragmentDiff
{
    /// <summary>
    /// The listed fragments of <paramref name="mine"/> whose text differs from
    /// <paramref name="baseline"/>'s, or all of them when there is no baseline.
    /// </summary>
    /// <exception cref="InvalidDataException">A listed fragment is missing from <paramref name="mine"/>,
    /// which a fragment override has no way to say.</exception>
    public static IReadOnlyList<FragmentChange> Changed(
        IContainerTree mine, IContainerTree? baseline, IEnumerable<string> ids)
    {
        List<FragmentChange> changed = [];
        foreach (string id in ids)
        {
            string? before = baseline?.Extract(id);
            string xml = mine.Extract(id)
                ?? throw new InvalidDataException(
                    $"'{id}' is gone, and a fragment override can change or add a unit but never remove one.");
            if (xml != before)
            {
                changed.Add(new FragmentChange(id, xml, baseline is not null && before is null));
            }
        }

        return changed;
    }
}
