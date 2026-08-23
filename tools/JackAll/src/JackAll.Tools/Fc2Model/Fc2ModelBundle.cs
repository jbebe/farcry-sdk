using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JackAll.Tools.Fc2Model;

/// <summary>What an entry carries, so a consumer need not sniff the file it points at.</summary>
public static class Fc2ModelKind
{
    public const string Mesh = "mesh";
    public const string Rig = "rig";
    public const string Material = "material";
    public const string Texture = "texture";
    public const string Clip = "clip";
    public const string Note = "note";
}

/// <summary>
/// One file in a pack: what it is in the game, where it sits in the zip, and whether it changed.
/// </summary>
public sealed class Fc2ModelEntry
{
    /// <summary>The game-relative path, which is this entry's identity.</summary>
    public required string Path { get; init; }

    /// <summary>Where the content sits inside the zip.</summary>
    public required string File { get; init; }

    public required string Kind { get; init; }

    /// <summary>Whether editing this changes only this model - see <see cref="Fc2ModelBundle"/>.</summary>
    public required string Role { get; init; }

    /// <summary>How many files reference this one, when that is known.</summary>
    public int? Usage { get; init; }

    /// <summary>Where <see cref="Usage"/> came from: an authoritative index, or a partial scan.</summary>
    public string? UsageSource { get; init; }

    public required string Sha256 { get; init; }

    /// <summary>
    /// The hash this arrived with, present only once an editor has changed the entry - so an entry
    /// is modified exactly when this is set.
    /// </summary>
    public string? OriginSha256 { get; set; }

    /// <summary>A texture's header bytes, which cannot be synthesized, as a file in the zip.</summary>
    public string? Header { get; init; }

    public string? CompanionHeader { get; init; }

    /// <summary>What to re-encode a texture as, so a trip through the pack cannot change it.</summary>
    public string? Codec { get; init; }

    public int? Levels { get; init; }

    [JsonIgnore]
    public bool Modified => OriginSha256 is not null;
}

public sealed class Fc2ModelLimits
{
    public int MaxClusterTriangles { get; init; } = 21845;

    public int MaxBufferVertices { get; init; } = 65535;

    public int MaxPaletteSlots { get; init; } = 48;

    public int MaxUvSets { get; init; } = 2;
}

/// <summary>
/// One animation bank in a pack, indexed so an editor can list what is carried without opening and
/// parsing every one of them.
/// </summary>
/// <remarks>
/// A hint, not truth: which clip in a bank's chain belongs to a given rig has to be re-derived from
/// the rig's bone ids on load, so a stale entry here can never mispose anything.
/// </remarks>
public sealed class Fc2ModelClip
{
    /// <summary>The bank's game path, which is the entry it indexes.</summary>
    public required string Path { get; init; }

    public required string File { get; init; }

    /// <summary>The bank's own name. Nothing in the format carries a friendlier one.</summary>
    public required string Label { get; init; }

    /// <summary>The last frame the bank keys, and the rate it plays at, or zero when it keys none.</summary>
    public int Frames { get; init; }

    public int Rate { get; init; }

    /// <summary>What this bank calls the model, and the bone it hangs it from.</summary>
    public string? Participant { get; init; }

    public string? Bone { get; init; }
}

public sealed class Fc2ModelManifest
{
    public string Format { get; init; } = Fc2ModelBundle.FormatName;

    public int Version { get; init; } = Fc2ModelBundle.CurrentVersion;

    /// <summary>The lowest reader that can make sense of this pack.</summary>
    public int RequiresReader { get; init; } = Fc2ModelBundle.CurrentVersion;

    public string Generator { get; init; } = "JackAll";

    /// <summary>The model this pack is about, by game path.</summary>
    public required string Model { get; init; }

    public string? Credits { get; init; }

    public Fc2ModelLimits Limits { get; init; } = new();

    public List<Fc2ModelEntry> Entries { get; init; } = [];

    /// <summary>An index over the entries of kind <c>clip</c>. See <see cref="Fc2ModelClip"/>.</summary>
    public List<Fc2ModelClip> Clips { get; init; } = [];
}

/// <summary>
/// A model and everything it needs, decoded, in one zip.
/// </summary>
/// <remarks>
/// Nothing in the game reads this. JackAll writes it, JackAll applies it, and an editor is the only
/// other thing that opens it - which is why no Dunia format survives inside: the mesh is JSON and
/// flat buffers, materials are JSON, textures are PNG. See docs/docs/file-formats/fc2model.md.
/// <para>
/// Ownership decides what an editor may change. A file in the model's own directory is its own by
/// construction; anything else is shared until a usage count says only this model references it. The
/// count only ever promotes, so the rule cannot become less safe as evidence improves.
/// </para>
/// </remarks>
public sealed class Fc2ModelBundle
{
    public const string FormatName = "fc2model";
    public const int CurrentVersion = 2;
    public const string ManifestFile = "manifest.json";
    public const string Extension = ".fc2model";

    public const string Owned = "owned";
    public const string Shared = "shared";

    private static readonly JsonSerializerOptions Json = Fc2ModelJson.Readable;

    public required Fc2ModelManifest Manifest { get; init; }

    /// <summary>The zip's contents, keyed by the path inside it.</summary>
    public Dictionary<string, byte[]> Files { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Fc2ModelEntry? Entry(string gamePath)
        => Manifest.Entries.FirstOrDefault(
            entry => entry.Path.Equals(gamePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>The bytes an entry points at.</summary>
    public byte[] Content(Fc2ModelEntry entry) => Files[entry.File];

    /// <summary>Entries an editor changed, which are the only ones worth writing back.</summary>
    public IEnumerable<Fc2ModelEntry> Modified => Manifest.Entries.Where(entry => entry.Modified);

    /// <summary>
    /// An entry's content hash, lowercase - which is what <c>hashlib.sha256().hexdigest()</c> gives.
    /// </summary>
    /// <remarks>
    /// A pack is written by one language and read by another, and a hash that only matches when both
    /// sides remember to fold case is a trap that shows up as "every entry looks modified".
    /// </remarks>
    public static string Hash(ReadOnlySpan<byte> content)
        => Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>
    /// Whether a file backs only this model, by directory or by an authoritative count.
    /// </summary>
    /// <remarks>
    /// The directory half stands alone because a file beside the model exists for it. A count may
    /// promote a file elsewhere to owned, but only a count that saw every kind of reference - a
    /// graphics-only scan is a lower bound, and under-counting is the direction that does damage.
    /// </remarks>
    public static string RoleOf(string gamePath, string modelPath, int? usage, bool usageIsAuthoritative)
    {
        string directory = Directory(gamePath);
        if (directory.Equals(Directory(modelPath), StringComparison.OrdinalIgnoreCase))
        {
            return Owned;
        }
        return usage == 1 && usageIsAuthoritative ? Owned : Shared;
    }

    public void Save(string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        Write(archive, ManifestFile, JsonSerializer.SerializeToUtf8Bytes(Manifest, Json));
        foreach ((string name, byte[] content) in Files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Write(archive, name, content);
        }
    }

    public static Fc2ModelBundle Load(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        ZipArchiveEntry manifestEntry = archive.GetEntry(ManifestFile)
            ?? throw new InvalidDataException($"{path} carries no {ManifestFile}.");

        Fc2ModelManifest manifest = JsonSerializer.Deserialize<Fc2ModelManifest>(Read(manifestEntry), Json)
            ?? throw new InvalidDataException($"{path} has an unreadable manifest.");
        if (manifest.Format != FormatName)
        {
            throw new InvalidDataException($"{path} is not an {FormatName} pack.");
        }
        if (manifest.RequiresReader > CurrentVersion)
        {
            throw new InvalidDataException(
                $"{path} needs a reader of version {manifest.RequiresReader}; this is {CurrentVersion}.");
        }

        var bundle = new Fc2ModelBundle { Manifest = manifest };
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!entry.FullName.Equals(ManifestFile, StringComparison.OrdinalIgnoreCase))
            {
                bundle.Files[entry.FullName] = Read(entry);
            }
        }
        return bundle;
    }

    private static string Directory(string gamePath)
    {
        string normalised = gamePath.Replace('\\', '/');
        int at = normalised.LastIndexOf('/');
        return at < 0 ? string.Empty : normalised[..at];
    }

    private static void Write(ZipArchive archive, string name, byte[] content)
    {
        using Stream stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(content);
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
