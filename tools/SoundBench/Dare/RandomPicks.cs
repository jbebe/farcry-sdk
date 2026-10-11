using JackAll.Tools.Spk;

namespace SoundBench.Dare;

/// <summary>
/// Picks a random container's entry by the traced rules (docs/docs/file-formats/spk.md, "How a random
/// container picks"): a chance of silence, never twice in a row unless allowed; the last pick skipped
/// unless its entry's flag; a sequence stepping through the entries from a random start.
/// </summary>
public sealed class RandomPicks
{
    private const int Silence = -1;

    private readonly Dictionary<uint, int> _last = [];

    /// <summary>The entry to play, or null for silence.</summary>
    public SpkEntry? Pick(SpkBankRecord random)
    {
        List<SpkEntry> entries = random.Entries;
        if (entries.Count == 0)
        {
            return null;
        }

        bool played = _last.TryGetValue(random.Id, out int last);
        if (random.Word(SpkLayout.RandomSequence) == 1)
        {
            int next = !played || last == Silence ? System.Random.Shared.Next(entries.Count) : (last + 1) % entries.Count;
            _last[random.Id] = next;
            return entries[next];
        }

        double silence = played && last == Silence && random.Word(SpkLayout.RandomRepeatSilence) != 1
            ? 0
            : random.Word(SpkLayout.RandomSilence);
        double[] weights = [.. entries.Select((e, i) => played && i == last && e.Extra != 1 && entries.Count > 1 ? 0 : (double)e.Value)];
        double draw = System.Random.Shared.NextDouble() * (silence + weights.Sum());
        for (int i = 0; i < weights.Length; i++)
        {
            if ((draw -= weights[i]) < 0)
            {
                _last[random.Id] = i;
                return entries[i];
            }
        }
        _last[random.Id] = silence > 0 ? Silence : 0;
        return silence > 0 ? null : entries[0];
    }
}
