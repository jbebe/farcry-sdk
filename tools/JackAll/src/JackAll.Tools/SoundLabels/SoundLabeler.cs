using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Rml;
using JackAll.Core.Vfs;
using JackAll.Tools.Labels;
using JackAll.Tools.Spk;

namespace JackAll.Tools.SoundLabels;

/// <summary>
/// Labels every `.spk` file by where its sounds are used. A sound nothing names directly takes the
/// uses of the records that reach it (event links, container entries, layers); a subtitled sound is
/// dialogue, with its transcript as the detail.
/// </summary>
public static class SoundLabeler
{
    public const string FromUsage = "usage";
    public const string FromLinks = "linked";
    public const string FromFolder = "folder";

    /// <summary>Whether a game file is a sound bank this labels.</summary>
    public static bool Covers(VfsFile file) => file.Type.Extension == "spk";

    private const int MaxGroups = 6;
    private const int MaxSubjects = 5;
    private const int MaxEvidence = 3;

    /// <summary>Labels the retail banks of an install from the files the game reads; files only a mod
    /// adds are skipped, and so is every file the shipped reach list says is never read.</summary>
    public static IReadOnlyList<FileLabel> FromGame(GameVfs vfs, IFcbClassScope classes, IProgress<string>? progress = null)
    {
        var banks = new Dictionary<string, SpkBank>(StringComparer.OrdinalIgnoreCase);
        var uses = new List<SoundUse>();
        IReadOnlySet<uint> unused = BundledAssets.LoadUnusedFiles();
        VfsFile[] files = [.. vfs.Files.Values.Where(f => !f.IsSynthetic && f.NameIsKnown
            && (f.Type.Extension == "spk" || IsSource(f.Type.Extension) && !unused.Contains(f.EngineHash)))];

        for (int i = 0; i < files.Length; i++)
        {
            if (i % 5000 == 0)
            {
                progress?.Report($"Reading {i:N0} / {files.Length:N0} files");
            }
            VfsFile file = files[i];
            if (vfs.ReadOriginal(file.EngineHash) is not { } bytes)
            {
                continue;
            }
            switch (file.Type.Extension)
            {
                case "spk":
                    try
                    {
                        banks[file.Path] = SpkBank.Parse(bytes);
                    }
                    catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentException)
                    {
                    }
                    break;
                case "fcb":
                    uses.AddRange(SoundUses.InFcb(bytes, file.Path, classes));
                    break;
                case "mab":
                    uses.AddRange(SoundUses.InAnimation(bytes, file.Path));
                    break;
                case "xml":
                    uses.AddRange(SoundUses.InXml(bytes, file.Path));
                    break;
                case "lua":
                    uses.AddRange(SoundUses.InScript(bytes, file.Path));
                    break;
                case "bank":
                    uses.AddRange(SoundUses.InBarks(bytes, file.Path));
                    break;
            }
        }

        OasisStringTable.UseSource(path => vfs.ReadOriginal(NameHash.Compute(path)));
        return Label(banks, uses, id => OasisStringTable.Resolve($"Subtitles;{id}"));
    }

    /// <summary>The formats that name sounds, as the switch above reads them.</summary>
    private static bool IsSource(string extension) => extension is "fcb" or "mab" or "xml" or "lua" or "bank";

    /// <summary>One label per bank that any use reaches, plus a bare "dialogue" for every unreached
    /// bank in a localized folder.</summary>
    public static IReadOnlyList<FileLabel> Label(
        IReadOnlyDictionary<string, SpkBank> banks, IEnumerable<SoundUse> uses, Func<uint, string?> subtitle)
    {
        var links = new Dictionary<uint, HashSet<uint>>();
        foreach (SpkBankRecord record in banks.Values.SelectMany(b => b.Records))
        {
            foreach ((uint target, SpkReference kind) in record.References())
            {
                if (kind != SpkReference.Rolloff)
                {
                    Add(links, record.Id, target);
                }
            }
        }

        var direct = new Dictionary<uint, HashSet<SoundUse>>();
        foreach (SoundUse use in uses)
        {
            Add(direct, use.Id, use);
        }
        foreach (uint id in banks.Values.SelectMany(b => b.Records).Select(r => r.Id).Distinct())
        {
            if (subtitle(id) is { } text)
            {
                Add(direct, id, new SoundUse(id, SoundCategories.Dialogue, "", $"\"{text}\"", OasisStringTable.RmlPath));
            }
        }

        HashSet<uint> spoken = [.. banks.Where(b => IsLocalized(b.Key)).SelectMany(b => b.Value.Records).Select(r => r.Id)];
        Dictionary<uint, HashSet<SoundUse>> effective = Propagate(direct, links);
        return [.. banks.Select(b => Describe(b.Key, b.Value, direct, effective, spoken)).OfType<FileLabel>()];
    }

    /// <summary>A bank in a <c>loc\</c> folder holds recorded speech.</summary>
    private static bool IsLocalized(string path) => path.Contains(@"\loc\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Each id's own uses, or, for an id with none, every use of the ids that play it.</summary>
    private static Dictionary<uint, HashSet<SoundUse>> Propagate(
        Dictionary<uint, HashSet<SoundUse>> direct, Dictionary<uint, HashSet<uint>> links)
    {
        var effective = new Dictionary<uint, HashSet<SoundUse>>(direct);
        var queue = new Queue<uint>(direct.Keys);
        while (queue.TryDequeue(out uint id))
        {
            if (!links.TryGetValue(id, out HashSet<uint>? targets))
            {
                continue;
            }
            foreach (uint target in targets.Where(t => !direct.ContainsKey(t)))
            {
                if (!effective.TryGetValue(target, out HashSet<SoundUse>? inherited))
                {
                    effective[target] = inherited = [];
                }
                int before = inherited.Count;
                inherited.UnionWith(effective[id]);
                if (inherited.Count > before)
                {
                    queue.Enqueue(target);
                }
            }
        }
        return effective;
    }

    /// <summary>A bank is dialogue when any use says so or its sound is recorded speech (an id a
    /// localized bank holds, which the event playing it shares).</summary>
    private static FileLabel? Describe(string path, SpkBank bank, Dictionary<uint, HashSet<SoundUse>> direct,
        Dictionary<uint, HashSet<SoundUse>> effective, HashSet<uint> spoken)
    {
        uint[] ids = [.. bank.Records.Select(r => r.Id).Distinct().Order()];
        List<SoundUse> all = [.. ids.SelectMany(id => effective.GetValueOrDefault(id) ?? []).Distinct()];
        bool speech = ids.Any(spoken.Contains);
        if (all.Count == 0)
        {
            return speech ? new FileLabel(path, SoundCategories.Dialogue, "", FromFolder, "") : null;
        }

        string source = ids.Any(direct.ContainsKey) ? FromUsage : FromLinks;
        string evidence = Evidence(all.Select(u => u.File));
        if (speech || all.Any(u => u.Category == SoundCategories.Dialogue))
        {
            string lines = Lines(ids, effective);
            return new FileLabel(path, SoundCategories.Dialogue, lines.Length > 0 ? lines : Groups(all), source, evidence);
        }

        string category = all.GroupBy(u => u.Category)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => SoundCategories.Rank(g.Key))
            .First().Key;
        return new FileLabel(path, category, Groups(all.Where(u => u.Category == category)), source, evidence);
    }

    /// <summary>Each line as <c>Speaker: "transcript"</c>, or <c>Speaker (bark tag)</c> without a
    /// subtitle; empty when no line has a speaker or a transcript.</summary>
    private static string Lines(uint[] ids, Dictionary<uint, HashSet<SoundUse>> effective)
    {
        var lines = new List<string>();
        foreach (HashSet<SoundUse> uses in ids.Select(id => effective.GetValueOrDefault(id)).OfType<HashSet<SoundUse>>())
        {
            string text = string.Join(" / ", uses.Where(u => u.File == OasisStringTable.RmlPath).Select(u => u.Action).Distinct());
            SoundUse[] spoken = [.. uses.Where(u => u.Speaker is not null)];
            string speakers = string.Join(", ", spoken.Select(u => u.Speaker).Distinct());
            string tags = string.Join(", ", spoken.Select(u => u.Action).Where(a => a.Length > 0).Distinct());
            string line = (speakers, text, tags) switch
            {
                ("", _, _) => text,
                (_, "", "") => speakers,
                (_, "", _) => $"{speakers} ({tags})",
                _ => $"{speakers}: {text}",
            };
            if (line.Length > 0 && !lines.Contains(line))
            {
                lines.Add(line);
            }
        }
        return string.Join(" / ", lines);
    }

    /// <summary>Uses grouped by action, each with the subjects that share it:
    /// <c>empty weapon (AK47, AK47 Gold)</c>.</summary>
    private static string Groups(IEnumerable<SoundUse> uses)
    {
        var groups = uses
            .GroupBy(u => u.Action, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Action: g.Key, Subjects: g.Select(u => u.Subject).Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderByDescending(g => g.Subjects.Count)
            .ThenBy(g => g.Action, StringComparer.Ordinal)
            .ToList();

        string text = string.Join("; ", groups.Take(MaxGroups).Select(g => g.Subjects.Count == 0
            ? g.Action
            : $"{g.Action} ({string.Join(", ", g.Subjects.Take(MaxSubjects))}{LabelFormat.More(g.Subjects.Count, MaxSubjects, ", +")})"));
        return text + LabelFormat.More(groups.Count, MaxGroups, "; +", " more");
    }

    private static string Evidence(IEnumerable<string> files)
    {
        List<string> distinct = [.. files.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        return string.Join(", ", distinct.Take(MaxEvidence)) + LabelFormat.More(distinct.Count, MaxEvidence, " +");
    }

    private static void Add<TKey, TValue>(Dictionary<TKey, HashSet<TValue>> map, TKey key, TValue value) where TKey : notnull
    {
        if (!map.TryGetValue(key, out HashSet<TValue>? set))
        {
            map[key] = set = [];
        }
        set.Add(value);
    }
}
