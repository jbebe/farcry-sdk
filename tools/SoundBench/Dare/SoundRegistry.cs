using JackAll.Core.Format;
using JackAll.Tools.Spk;
using SoundBench.Game;

namespace SoundBench.Dare;

/// <summary>A record the registry holds, and the bank file it came from.</summary>
public sealed record Registered(SpkBankRecord Record, string Bank);

/// <summary>
/// The sound records the game has registered, by id. A bank loads by the id an event field names, and
/// with it the banks a world's depload lists under that bank; a record in no loaded bank stays missing,
/// as the game's "Atomic object has not been loaded".
/// </summary>
public sealed class SoundRegistry
{
    private static readonly uint SoundResource = DepLoadTypes.Hash("CSoundResource");

    private readonly GameFiles _files;
    private readonly Dictionary<uint, IReadOnlyList<DepLoadChild>> _children;
    private readonly HashSet<uint> _banks = [];
    private readonly Dictionary<uint, Registered> _records = [];
    private readonly Dictionary<uint, Pcm> _audio = [];

    public SoundRegistry(GameFiles files, string world = "world1")
    {
        _files = files;
        _children = files.Read($@"worlds\{world}\generated\{world}_depload.dat") is { } depload
            ? DepLoadDocument.Decode(depload).Parents.ToDictionary(p => p.Hash, p => p.Children)
            : [];
        Hold(RolloffPack);
    }

    /// <summary>The bank of every retail rolloff curve, <see cref="SpkBank.RolloffPackPath"/>.</summary>
    private const uint RolloffPack = 0x2FFFFFFF;

    /// <summary>Loads the bank an id names, as <c>CSoundResource::GetFromSoundId</c> does.</summary>
    public void Hold(uint id)
    {
        if (id != SpkLayout.NoId)
        {
            Load(NameHash.Compute($@"soundbinary\{id:x8}.spk"));
        }
    }

    public Registered? Find(uint id) => _records.GetValueOrDefault(id);

    /// <summary>The audio a sample plays, decoded once.</summary>
    public Pcm? Audio(uint id)
    {
        if (_audio.TryGetValue(id, out Pcm? pcm))
        {
            return pcm;
        }
        if (Find(id) is not { Record.IsAudio: true } audio)
        {
            return null;
        }
        return _audio[id] = Pcm.Decode(audio.Record);
    }

    /// <summary>Decodes every audio record loaded so far, so a play never waits on a decode.</summary>
    public void DecodeAll()
    {
        foreach (uint id in _records.Values.Where(r => r.Record.IsAudio).Select(r => r.Record.Id).ToList())
        {
            Audio(id);
        }
    }

    private void Load(uint hash)
    {
        if (!_banks.Add(hash) || _files.Read(hash) is not { } bytes)
        {
            return;
        }

        string bank = _files.Describe(hash);
        foreach (SpkBankRecord record in SpkBank.Parse(bytes).Records)
        {
            _records.TryAdd(record.Id, new Registered(record, bank));
        }
        foreach (DepLoadChild child in _children.GetValueOrDefault(hash, []).Where(c => c.TypeHash == SoundResource))
        {
            Load(child.Hash);
        }
    }
}
