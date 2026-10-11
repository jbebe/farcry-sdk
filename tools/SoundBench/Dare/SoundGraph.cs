using JackAll.Tools.Spk;

namespace SoundBench.Dare;

/// <summary>
/// Instantiates an event into the tree the game's dispatcher plays: every child of a multi-event or a
/// multilayer, one entry of a switch or a random container, down to the samples.
/// </summary>
/// <param name="switchValue">The value a switch group is set to on the playing object, or null.</param>
public sealed class SoundGraph(SoundRegistry registry, Func<uint, uint?> switchValue, RandomPicks picks)
{
    private const int MaxDepth = 16;

    /// <summary>How a Play event places what it plays.</summary>
    private sealed record Placement(bool Positioned, IReadOnlyList<SpkPoint>? Rolloff);

    public SoundNode Event(uint id, int depth = 0)
    {
        if (registry.Find(id) is not { Record: { IsEvent: true } record } found)
        {
            return Missing(id, []);
        }

        var node = new SoundNode { Id = id, Kind = record.Layout!.Element, Bank = found.Bank };
        if (depth > MaxDepth)
        {
            node.Problem = "nests too deep";
            return node;
        }

        switch ((SpkEventType)record.Kind)
        {
            case SpkEventType.Leaf:
                uint rolloff = record.Word(SpkLayout.PlayRolloff);
                bool positioned = record.Word(SpkLayout.PlayPositioned) == 1;
                node.Detail = !positioned ? "unpositioned" : rolloff == SpkLayout.NoId ? "positioned, no rolloff" : $"rolloff 0x{rolloff:x8}";
                var placement = new Placement(positioned, rolloff == SpkLayout.NoId ? null : registry.Find(rolloff)?.Record.Points);
                node.Children.Add(Resource(record.Word(2), [], placement, depth + 1));
                break;
            case SpkEventType.Switch:
                uint group = record.Word(3);
                uint? value = switchValue(group);
                SpkEntry? match = value is null ? null : record.Entries.FirstOrDefault(e => e.Value == value);
                node.Detail = $"group 0x{group:x8} = {(value is { } v ? $"0x{v:x8}" : "unset")}{(match is null ? ", default" : "")}";
                node.Children.Add(Event(match?.Ref ?? record.Word(4), depth + 1));
                break;
            case SpkEventType.List:
                node.Children.AddRange(record.Entries.Select(e => Event(e.Ref, depth + 1)));
                break;
            case SpkEventType.NoOpLinked:
                node.Detail = $"plays nothing, names 0x{record.Word(2):x8}";
                break;
            default:
                node.Problem = $"event type {record.Kind} is not emulated";
                break;
        }
        return node;
    }

    private SoundNode Resource(uint id, IReadOnlyList<SpkCurve> curves, Placement placement, int depth)
    {
        if (registry.Find(id) is not { Record: { IsResource: true } record } found)
        {
            return Missing(id, curves);
        }

        float gainDb = (int)record.Word(SpkLayout.SampleGain) / 65536f;
        if (record.Layout == SpkLayout.Sample)
        {
            return Sample(id, record, found.Bank, gainDb, curves, placement);
        }

        var node = new SoundNode { Id = id, Kind = record.Layout!.Element, Bank = found.Bank, GainDb = gainDb, Curves = curves };
        if (depth > MaxDepth)
        {
            node.Problem = "nests too deep";
        }
        else if (record.Layout == SpkLayout.Multilayer)
        {
            node.Children.AddRange(record.Layers.Select(l => Resource(l.Resource, l.Curves, placement, depth + 1)));
        }
        else if (record.Layout == SpkLayout.Random)
        {
            SpkEntry? pick = picks.Pick(record);
            node.Detail = pick is null ? "picked silence" : $"picked 1 of {record.Entries.Count}";
            if (pick is not null)
            {
                node.Children.Add(Resource(pick.Ref, [], placement, depth + 1));
            }
        }
        else if (record.Layout == SpkLayout.Switch)
        {
            uint group = record.Word(8);
            uint value = switchValue(group) ?? record.Word(9);
            node.Detail = $"group 0x{group:x8} = 0x{value:x8}";
            if (record.Entries.FirstOrDefault(e => e.Value == value) is { } match)
            {
                node.Children.Add(Resource(match.Ref, [], placement, depth + 1));
            }
            else
            {
                node.Problem = "has no case for that value";
            }
        }
        else
        {
            node.Problem = $"resource kind {record.Kind} is not emulated";
        }
        return node;
    }

    private SoundNode Sample(uint id, SpkBankRecord record, string bank, float gainDb, IReadOnlyList<SpkCurve> curves, Placement placement)
    {
        uint audioId = record.Word(SpkLayout.SampleAudio);
        if (registry.Find(audioId) is not { Record.IsAudio: true } audio || registry.Audio(audioId) is not { } pcm)
        {
            return new SoundNode
            {
                Id = id, Kind = "Sample", Bank = bank, GainDb = gainDb, Curves = curves,
                Problem = $"plays audio 0x{audioId:x8}, which has not been loaded",
            };
        }

        int frames = pcm.Frames;
        long declared = record.Word(SpkLayout.SampleByteLength);
        long stream = audio.Record.Data.Length;
        string? problem = null;
        if (declared != stream)
        {
            problem = $"declares {declared:N0} bytes of a {stream:N0}-byte stream{(declared > stream ? ": the game plays noise past its end" : "")}";
            frames = (int)Math.Min(frames, frames * declared / Math.Max(1, stream));
        }

        bool loop = record.Word(SpkLayout.SampleLoop) == 1;
        bool positioned = placement.Positioned && pcm.Channels == 1;
        return new SoundNode
        {
            Id = id,
            Kind = "Sample",
            Bank = bank,
            GainDb = gainDb,
            Curves = curves,
            Detail = $"{pcm.Rate} Hz {(pcm.Channels == 2 ? "stereo" : "mono")}, {frames / (double)pcm.Rate:0.00} s{(loop ? ", loops" : "")}",
            Problem = problem,
            Voice = new Voice(pcm, loop, frames, positioned, placement.Rolloff),
        };
    }

    private static SoundNode Missing(uint id, IReadOnlyList<SpkCurve> curves) => new()
    {
        Id = id, Kind = "Missing", Curves = curves, Problem = "has not been loaded",
    };
}
