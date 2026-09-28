namespace JackAll.Tools.Spk;

/// <summary>The edits an editor makes on a bank beyond changing a word: variations and weights.</summary>
public static class SpkBankEdits
{
    /// <summary>New records take ids from 0x10000000 + bank id × 32: past every retail id (all under
    /// 0x00500000) and a different block for every bank.</summary>
    private const uint NewIdBase = 0x10000000;
    private const int IdsPerBank = 32;

    public static uint NewId(this SpkBank bank, uint bankId)
    {
        uint first = NewIdBase + (bankId & 0x00FFFFFF) * IdsPerBank;
        for (uint id = first; id < first + IdsPerBank; id++)
        {
            if (bank.Find(id) is null)
            {
                return id;
            }
        }
        throw new InvalidOperationException($"All {IdsPerBank} ids for new records in bank 0x{bankId:x8} are taken.");
    }

    /// <summary>What variations of <paramref name="owner"/> build on: a Random itself, or the Random or
    /// Sample in this bank a Play plays; null for anything else.</summary>
    public static SpkBankRecord? SoundFor(this SpkBank bank, SpkBankRecord owner) =>
        owner.Layout == SpkLayout.Random ? owner
        : owner.Layout == SpkLayout.Play && bank.Find(owner.Word(2)) is { } sound
            && (sound.Layout == SpkLayout.Random || sound.Layout == SpkLayout.Sample) ? sound
        : null;

    /// <summary>The random container new variations of <paramref name="owner"/> go into, wrapping the
    /// sample a Play plays in a new one when it has none.</summary>
    public static SpkBankRecord RandomFor(this SpkBank bank, SpkBankRecord owner, uint bankId)
    {
        SpkBankRecord sound = bank.SoundFor(owner)
            ?? throw new InvalidOperationException("Variations go on a Random container, or a Play over a sample or one, in this bank.");
        if (sound.Layout == SpkLayout.Random)
        {
            return sound;
        }

        var random = new SpkBankRecord
        {
            Id = bank.NewId(bankId), Type = SpkRecordType.TransformedFixed128, Kind = (uint)SpkResourceKind.Random,
            Words = SpkLayout.Random.NewWords(), Entries = [new SpkEntry(sound.Id, SpkLayout.One)],
        };
        bank.Insert(random);
        owner.Words[2] = random.Id;
        return random;
    }

    /// <summary>The sample a new variation copies: the random container's first sample.</summary>
    public static SpkBankRecord? TemplateSample(this SpkBank bank, SpkBankRecord random) =>
        random.Entries.Select(e => bank.Find(e.Ref)).FirstOrDefault(r => r?.Layout == SpkLayout.Sample);

    /// <summary>Adds a sample and audio for <paramref name="stream"/> as a new choice, weighted like
    /// the average existing one; returns the new sample.</summary>
    public static SpkBankRecord AddVariation(this SpkBank bank, SpkBankRecord random, byte[] stream, int? sampleRate, uint bankId)
    {
        var audio = new SpkBankRecord { Id = bank.NewId(bankId), Type = SpkRecordType.FlatCopy, Data = stream, SampleRate = sampleRate };
        bank.Insert(audio);

        uint[] words = bank.TemplateSample(random)?.Words is { } template ? [.. template] : SpkLayout.Sample.NewWords();
        words[SpkLayout.SampleAudio] = audio.Id;
        var sample = new SpkBankRecord
        {
            Id = bank.NewId(bankId), Type = SpkRecordType.TransformedFixed128, Kind = (uint)SpkResourceKind.Sample, Words = words,
        };
        bank.Insert(sample);

        uint average = random.Entries.Count == 0 ? SpkLayout.One : (uint)random.Entries.Average(e => e.Value);
        random.Entries.Add(new SpkEntry(sample.Id, average));
        Normalize(random);
        return sample;
    }

    /// <summary>Removes a choice, and its sample and audio when nothing else plays them.</summary>
    public static void RemoveChoice(this SpkBank bank, SpkBankRecord random, int index)
    {
        uint removed = random.Entries[index].Ref;
        random.Entries.RemoveAt(index);
        Normalize(random);
        if (bank.Find(removed) is { } sample && sample.Layout == SpkLayout.Sample && !IsReferenced(bank, removed))
        {
            bank.Records.Remove(sample);
            if (bank.Find(sample.Word(SpkLayout.SampleAudio)) is { } audio && !IsReferenced(bank, audio.Id))
            {
                bank.Records.Remove(audio);
            }
        }
    }

    /// <summary>Sets one choice's chance (or, with a null index, silence's) and rescales the rest to
    /// keep their proportions.</summary>
    public static void SetChance(SpkBankRecord random, int? index, double chance)
    {
        double[] weights = Weights(random);
        int slot = index ?? weights.Length - 1;
        chance = Math.Clamp(chance, 0, 1);
        double others = weights.Where((_, i) => i != slot).Sum();
        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] = i == slot ? chance
                : others > 0 ? weights[i] / others * (1 - chance)
                : i == weights.Length - 1 ? 0 : (1 - chance) / Math.Max(1, weights.Length - 2);
        }
        Store(random, weights);
    }

    /// <summary>A choice's or silence's share of every play.</summary>
    public static double Chance(SpkBankRecord random, int? index)
    {
        double[] weights = Weights(random);
        double total = weights.Sum();
        return total == 0 ? 0 : weights[index ?? weights.Length - 1] / total;
    }

    /// <summary>The entries' weights, then silence's.</summary>
    private static double[] Weights(SpkBankRecord random) => [.. random.Entries.Select(e => (double)e.Value), random.Word(8)];

    /// <summary>Rescales the weights and silence to sum to 1.0.</summary>
    private static void Normalize(SpkBankRecord random) => Store(random, Weights(random));

    private static void Store(SpkBankRecord random, double[] weights)
    {
        double total = weights.Sum();
        uint Q16(double w) => total == 0 ? 0 : (uint)Math.Floor(w / total * SpkLayout.One);
        for (int i = 0; i < random.Entries.Count; i++)
        {
            random.Entries[i] = random.Entries[i] with { Value = Q16(weights[i]) };
        }
        random.Words[8] = Q16(weights[^1]);
    }

    /// <summary>Adds a record where its id falls, as retail sound banks list their records.</summary>
    public static void Insert(this SpkBank bank, SpkBankRecord record)
    {
        int after = bank.Records.FindIndex(r => r.Id > record.Id);
        bank.Records.Insert(after < 0 ? bank.Records.Count : after, record);
    }

    private static bool IsReferenced(SpkBank bank, uint id) =>
        bank.Records.Any(r => r.References().Any(link => link.Id == id));
}
