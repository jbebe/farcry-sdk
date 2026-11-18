using JackAll.Core.Format;

namespace JackAll.Core.Mods;

/// <summary>One bank of a layer: the layer and the bank's path in it.</summary>
public readonly record struct SpkLayerBank(IModLayer Layer, string Path);

/// <summary>Two banks holding different records under the same ids.</summary>
public sealed record SpkIdClash(SpkLayerBank First, SpkLayerBank Second, IReadOnlyList<uint> Ids)
{
    /// <summary>The clash and what to do about it, as one sentence, each layer named by <paramref name="nameOf"/>.</summary>
    public string Describe(Func<IModLayer, string> nameOf)
    {
        string ids = Ids.Count == 1 ? $"id 0x{Ids[0]:x8}" : $"{Ids.Count} ids, 0x{Ids[0]:x8} to 0x{Ids[^1]:x8}";
        return $"'{nameOf(First.Layer)}' {First.Path} and '{nameOf(Second.Layer)}' {Second.Path} hold different "
            + $"sound records under {ids}, and the game can crash once both are loaded - give one bank's records "
            + "ids of their own.";
    }
}

/// <summary>Sound ids that two of the banks a build installs hold with different records.</summary>
public static class SpkIdClashes
{
    public static IReadOnlyList<SpkIdClash> Find(IReadOnlyList<IModLayer> layers)
    {
        // The copy of each bank the build installs: the last layer's.
        var banks = new Dictionary<uint, SpkLayerBank>();
        foreach (IModLayer layer in layers.Where(l => l.Enabled))
        {
            foreach (uint hash in layer.Hashes)
            {
                if (layer.PathOf(hash) is { } path && path.EndsWith(".spk", StringComparison.OrdinalIgnoreCase))
                {
                    banks[hash] = new SpkLayerBank(layer, path);
                }
            }
        }

        var owners = new Dictionary<uint, (SpkLayerBank Bank, byte[] Payload)>();
        var shared = new List<(SpkLayerBank First, SpkLayerBank Second, uint Id)>();
        foreach ((uint hash, SpkLayerBank bank) in banks.OrderBy(kv => kv.Value.Path, StringComparer.OrdinalIgnoreCase))
        {
            foreach (SpkFrame record in SpkFraming.Read(bank.Layer.Read(hash)))
            {
                if (!owners.TryAdd(record.Id, (bank, record.Payload))
                    && !owners[record.Id].Payload.AsSpan().SequenceEqual(record.Payload))
                {
                    shared.Add((owners[record.Id].Bank, bank, record.Id));
                }
            }
        }
        return [.. shared.GroupBy(s => (s.First, s.Second), s => s.Id)
            .Select(g => new SpkIdClash(g.Key.First, g.Key.Second, [.. g.Order()]))];
    }
}
