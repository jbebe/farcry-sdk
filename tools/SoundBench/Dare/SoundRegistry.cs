using JackAll.Core.Format;
using JackAll.Tools.Spk;
using JackAll.Tools.World;
using SoundBench.Game;

namespace SoundBench.Dare;

/// <summary>A record the registry holds, and the bank file it came from.</summary>
public sealed record Registered(SpkBankRecord Record, string Bank);

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

    /// <summary>The bank file each multilayer curve sits in.</summary>
    private readonly Dictionary<SpkCurve, uint> _curveBanks = new(ReferenceEqualityComparer.Instance);
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

    /// <summary>Notes that a curve's bank no longer matches its file; true when that bank had no edits before.</summary>
    public bool Edited(SpkCurve curve) => _curveBanks.TryGetValue(curve, out uint bank) && _unsaved.Add(bank);

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
            foreach (SpkCurve curve in record.Layers.SelectMany(l => l.Curves))
            {
                _curveBanks[curve] = hash;
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
