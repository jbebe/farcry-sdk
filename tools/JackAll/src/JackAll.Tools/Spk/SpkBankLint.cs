namespace JackAll.Tools.Spk;

public enum SpkProblemSeverity
{
    Error,
    Warning,
    Note,
}

public sealed record SpkProblem(SpkProblemSeverity Severity, string Message);

/// <summary>What would make a bank fail or misbehave in game, found before it is built.</summary>
public static class SpkBankLint
{
    /// <param name="bankId">The id the bank is loaded by: the game opens <c>soundbinary\&lt;id&gt;.spk</c>.</param>
    public static IReadOnlyList<SpkProblem> Check(SpkBank bank, uint? bankId)
    {
        var problems = new List<SpkProblem>();
        void Add(SpkProblemSeverity severity, string message) => problems.Add(new SpkProblem(severity, message));

        // Only an event bank is looked up by its own id; a bank of samples or curves is named by a variant of it.
        if (bankId is { } id && bank.Find(id) is null && bank.Records.Any(r => r.IsEvent))
        {
            Add(SpkProblemSeverity.Warning,
                $"The game loads this bank for id 0x{id:x8}, but no record has that id; name the file after the event it plays.");
        }

        foreach (SpkBankRecord record in bank.Records)
        {
            string name = $"{record.Element} 0x{record.Id:x8}";
            foreach ((uint target, SpkReference kind) in record.References())
            {
                if (bank.Find(target) is not { } found)
                {
                    if (kind != SpkReference.Rolloff)
                    {
                        Add(SpkProblemSeverity.Note, $"{name}: {kind} 0x{target:x8} is not in this bank, so another loaded bank must hold it.");
                    }
                }
                else if (!Is(found, kind))
                {
                    Add(SpkProblemSeverity.Error, $"{name} points at 0x{target:x8} as {Article(kind)}, but that record is not one.");
                }
            }

            if (record.IsAudio)
            {
                switch (SpkBank.DescribeAudio(record))
                {
                    case null:
                        Add(SpkProblemSeverity.Error, $"{name} is neither Ogg Vorbis nor an IMA-ADPCM stream.");
                        break;
                    case { Ogg: false } when record.SampleRate is null:
                        Add(SpkProblemSeverity.Error, $"{name} is IMA-ADPCM, which does not carry its sample rate: give it a rate.");
                        break;
                }
            }

            if (record.Layout?.ChildCount is not null && record.Tail is null && record.Entries.Count + record.Layers.Count == 0)
            {
                Add(SpkProblemSeverity.Warning, $"{name} has no children, so it plays nothing.");
            }

            if (record.IsRolloff && record.Points.Zip(record.Points.Skip(1)).Any(p => p.Second.X < p.First.X))
            {
                Add(SpkProblemSeverity.Error, $"{name}: its distances must increase.");
            }

            if (record.Layout == SpkLayout.Play && record.Word(7) != SpkLayout.NoId
                && StereoSamples(bank, record.Word(2), []).Select(id => (uint?)id).FirstOrDefault() is { } stereo)
            {
                Add(SpkProblemSeverity.Warning,
                    $"{name} has a rolloff, but sample 0x{stereo:x8} is stereo: stereo plays unpositioned, so the rolloff is ignored.");
            }
        }
        return problems;
    }

    private static bool Is(SpkBankRecord record, SpkReference kind) => kind switch
    {
        SpkReference.Event => record.IsEvent,
        SpkReference.Resource => record.IsResource,
        SpkReference.Audio => record.IsAudio,
        _ => record.IsRolloff,
    };

    private static string Article(SpkReference kind) => kind is SpkReference.Event or SpkReference.Audio
        ? $"an {kind.ToString().ToLowerInvariant()}"
        : $"a {kind.ToString().ToLowerInvariant()}";

    /// <summary>The stereo samples a resource can play, following containers within the bank.</summary>
    private static IEnumerable<uint> StereoSamples(SpkBank bank, uint resource, HashSet<uint> seen)
    {
        if (!seen.Add(resource) || bank.Find(resource) is not { IsResource: true } record)
        {
            yield break;
        }

        if (record.Layout == SpkLayout.Sample)
        {
            if (SpkBank.DescribeAudio(bank.Find(record.Word(SpkLayout.SampleAudio))) is { Channels: > 1 })
            {
                yield return resource;
            }
            yield break;
        }

        foreach ((uint child, SpkReference _) in record.References())
        {
            foreach (uint stereo in StereoSamples(bank, child, seen))
            {
                yield return stereo;
            }
        }
    }
}
