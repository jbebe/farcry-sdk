using System.IO;
using JackAll.Core.Format;
using JackAll.Tools.Audio;
using JackAll.Tools.Spk;
using JackAll.Tools.World;
using SoundBench.Game;

namespace SoundBench.Dare;

/// <summary>A record the registry holds, and the bank file it came from.</summary>
public sealed record Registered(SpkBankRecord Record, string Bank);

/// <summary>A WAV's audio, written into its bank's file and waiting to replace its record's.</summary>
public sealed record AudioSwap(string Wav, string Bank, SpkBankRecord Record, byte[] Stream, int Rate, Pcm? Decoded);

/// <summary>
/// The sound records the game has registered, by id. A bank loads by the id an event field names, and
/// with it the banks world1's depload lists under that bank; a record in no loaded bank stays missing,
/// as the game's "Atomic object has not been loaded". Audio is decoded as its bank loads.
/// </summary>
public sealed class SoundRegistry
{
    private static readonly uint SoundResource = DepLoadTypes.Hash("CSoundResource");

    private readonly GameFiles _files;
    private readonly Dictionary<uint, IReadOnlyList<DepLoadChild>> _children;
    private readonly Dictionary<uint, SpkBank> _banks = [];
    private readonly HashSet<uint> _unsaved = [];

    /// <summary>The bank file each record and multilayer curve sits in.</summary>
    private readonly Dictionary<object, uint> _bankOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<uint, Registered> _records = [];
    private readonly Dictionary<uint, Pcm> _audio = [];
    private readonly List<string> _problems = [];

    public SoundRegistry(GameFiles files)
    {
        _files = files;
        _children = files.Read(WorldEditDependencies.DepLoadPathOf("world1")) is { } depload
            ? DepLoadDocument.Decode(depload).Parents.ToDictionary(p => p.Hash, p => p.Children)
            : [];
        Hold(SpkBank.RolloffPackId);
    }

    /// <summary>The errors and warnings the bank lint finds in the banks loaded.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Loads the bank an id names, as <c>CSoundResource::GetFromSoundId</c> does.</summary>
    public void Hold(uint id)
    {
        if (id != SpkLayout.NoId)
        {
            Load(NameHash.Compute(SpkBank.PathOf(id)));
        }
    }

    public Registered? Find(uint id) => _records.GetValueOrDefault(id);

    public Pcm? Audio(uint id) => _audio.GetValueOrDefault(id);

    /// <summary>The banks edited since they were read or saved.</summary>
    public IEnumerable<string> Unsaved => _unsaved.Select(_files.Describe);

    /// <summary>Notes that the bank holding this curve no longer matches its file; true when it had no edits before.</summary>
    public bool Edited(SpkCurve curve) => Edited((object)curve);

    /// <summary>Notes that the bank holding this record no longer matches its file; true when it had no edits before.</summary>
    public bool Edited(SpkBankRecord record) => Edited((object)record);

    private bool Edited(object part) => _bankOf.TryGetValue(part, out uint bank) && _unsaved.Add(bank);

    /// <summary>
    /// Writes each WAV whose audio differs from its record's into the bank's file, where nothing else changes:
    /// edits not yet saved stay so. <see cref="Apply"/> then swaps it in here.
    /// </summary>
    /// <param name="wavs">16-bit PCM WAVs by the audio record each stands for; <see cref="WavSources"/> finds them.</param>
    /// <param name="problems">Gets a line for each WAV refused.</param>
    public IReadOnlyList<AudioSwap> Refresh(IReadOnlyDictionary<uint, string> wavs, List<string> problems)
    {
        List<AudioSwap> swaps = [];
        List<(uint Bank, SpkBank File)> written = [];
        var byBank = wavs.Select(w => (Wav: w.Value, Record: Find(w.Key)?.Record))
            .Where(w => w.Record is { IsAudio: true })
            .GroupBy(w => _bankOf[w.Record!]);
        foreach (var bank in byBank)
        {
            SpkBank? file = null;
            foreach ((string wav, SpkBankRecord? record) in bank)
            {
                if (Encode(wav, record!, problems) is not { } audio || audio.Stream.AsSpan().SequenceEqual(record!.Data))
                {
                    continue;
                }
                file ??= SpkBank.Parse(_files.Read(bank.Key)!);
                SpkBankRecord saved = file.Find(record.Id)!;
                file.ReplaceAudio(saved, audio.Stream, audio.Rate);
                swaps.Add(new AudioSwap(Path.GetFileName(wav), _files.Describe(bank.Key), record, audio.Stream, audio.Rate, Pcm.Decode(saved)));
            }
            if (file is not null)
            {
                written.Add((bank.Key, file));
            }
        }
        // Every bank is read before any is saved: a save has the next read build the files again.
        foreach ((uint bank, SpkBank file) in written)
        {
            _files.Save(bank, file.Write());
        }
        return swaps;
    }

    /// <summary>Swaps in the audio <see cref="Refresh"/> wrote. The plays already started keep what they had.</summary>
    public void Apply(IEnumerable<AudioSwap> swaps)
    {
        foreach (AudioSwap swap in swaps)
        {
            _banks[_bankOf[swap.Record]].ReplaceAudio(swap.Record, swap.Stream, swap.Rate);
            if (swap.Decoded is not null)
            {
                _audio[swap.Record.Id] = swap.Decoded;
            }
        }
    }

    /// <summary>A WAV as the stream an IMA-ADPCM record holds, or null, with a line in <paramref name="problems"/>.</summary>
    private static (byte[] Stream, int Rate)? Encode(string wav, SpkBankRecord record, List<string> problems)
    {
        string name = Path.GetFileName(wav);
        if (SpkBank.DescribeAudio(record) is { Ogg: true })
        {
            problems.Add($"{name}: its record holds Ogg Vorbis, which the bench does not encode");
            return null;
        }
        try
        {
            WavAudio.Pcm16Audio pcm = WavAudio.ReadPcm16(File.ReadAllBytes(wav));
            return (ImaAdpcm.Encode(pcm.Samples, pcm.Channels), pcm.SampleRate);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            problems.Add($"{name}: {ex.Message} Export it as 16-bit PCM WAV.");
            return null;
        }
    }

    /// <summary>Writes every edited bank into the layers.</summary>
    /// <returns>Each edited bank, with the layer folder it went to, or null where there was none; those stay unsaved.</returns>
    public IReadOnlyList<(string Bank, string? Layer)> Save()
    {
        List<(string, string?)> saved = [];
        foreach (uint hash in _unsaved.ToList())
        {
            string? layer = _files.Save(hash, _banks[hash].Write());
            saved.Add((_files.Describe(hash), layer));
            if (layer is not null)
            {
                _unsaved.Remove(hash);
            }
        }
        return saved;
    }

    private void Load(uint hash)
    {
        if (_banks.ContainsKey(hash) || _files.Read(hash) is not { } bytes)
        {
            return;
        }

        string name = _files.Describe(hash);
        SpkBank bank = _banks[hash] = SpkBank.Parse(bytes);
        foreach (SpkBankRecord record in bank.Records)
        {
            _bankOf[record] = hash;
            foreach (SpkCurve curve in record.Layers.SelectMany(l => l.Curves))
            {
                _bankOf[curve] = hash;
            }
            if (_records.TryAdd(record.Id, new Registered(record, name)) && record.IsAudio && Pcm.Decode(record) is { } pcm)
            {
                _audio[record.Id] = pcm;
            }
        }
        _problems.AddRange(SpkBankLint.Check(bank, null)
            .Where(p => p.Severity != SpkProblemSeverity.Note)
            .Select(p => $"{name}: {p.Message}"));

        foreach (DepLoadChild child in _children.GetValueOrDefault(hash, []).Where(c => c.TypeHash == SoundResource))
        {
            Load(child.Hash);
        }
    }
}
