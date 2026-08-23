using System.Text.Json;
using JackAll.Tools.Mab;
using JackAll.Tools.Skeleton;
using JackAll.Tools.Xbg;
using JackAll.Tools.Xbm;
using JackAll.Tools.Xbt;

namespace JackAll.Tools.Fc2Model;

/// <summary>
/// Collects a model and everything it references into a pack, decoding each on the way.
/// </summary>
/// <remarks>
/// The closure is the model, the materials it names, the textures those name, the top-mip companion
/// half of them keep in a sibling file, and the rig beside it. A material that an `.xbg` embeds
/// rather than names travels with the mesh, so only the rest is fetched.
/// </remarks>
public static class Fc2ModelBuilder
{
    /// <summary>A rig sits beside its model under this suffix.</summary>
    public const string RigSuffix = "_ref.skeleton";

    // Not indented: a document is mostly flat float arrays, and a line per element makes the pack
    // several times its own size for no reader's benefit. The manifest, which is the part a person
    // reads, is indented where it is written.
    private static readonly JsonSerializerOptions Json = Fc2ModelJson.Compact;

    /// <summary>
    /// Build a pack for one model.
    /// </summary>
    /// <param name="usage">How many files reference a path, when an authoritative index can say.
    /// Without one, ownership falls back to the directory rule, which is the safe direction.</param>
    /// <param name="clips">Animation banks to carry along, by game path. A weapon's motion lives in
    /// a bank filed under the character animations rather than beside the model, so nothing in the
    /// mesh names them - the caller decides which belong.</param>
    /// <param name="rig">The rig to carry, by game path. Defaults to the one beside the model, which
    /// is where a weapon's and a vehicle's sit. A character has none: 74 of the 78 skinned meshes
    /// have no sibling rig and share <c>characters_commonpelvis_ref.skeleton</c> - the best name
    /// match for 70 of them, and a tie for five - so which one is the caller's to say rather than
    /// something to guess at.</param>
    public static Fc2ModelBundle Build(
        string modelPath, Func<string, byte[]?> readByPath, Func<string, int>? usage = null,
        IEnumerable<string>? clips = null, string? rig = null)
    {
        byte[] modelBytes = readByPath(modelPath)
            ?? throw new InvalidDataException($"No model at {modelPath}.");

        XbgFile model = XbgFile.Parse(modelBytes);
        var bundle = new Fc2ModelBundle
        {
            Manifest = new Fc2ModelManifest { Model = modelPath },
        };

        Add(bundle, modelPath, "model/mesh.json", Fc2ModelKind.Mesh,
            JsonSerializer.SerializeToUtf8Bytes(MeshDocument.From(model), Json), modelPath, usage);

        // A material the mesh embeds is already inside it; only a named one is a file of its own.
        Dictionary<string, XbmFile> inline = XbmFile.InlineMaterials(model);
        foreach (string materialPath in model.Materials.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (inline.ContainsKey(materialPath) || readByPath(materialPath) is not { } materialBytes)
            {
                continue;
            }

            MaterialDocument material = MaterialDocument.Parse(materialBytes);
            Add(bundle, materialPath, $"materials/{Name(materialPath)}.json", Fc2ModelKind.Material,
                JsonSerializer.SerializeToUtf8Bytes(material, Json), modelPath, usage);

            foreach (MaterialTexture texture in material.Textures)
            {
                AddTexture(bundle, texture.Path, readByPath, modelPath, usage);
            }
        }

        string rigPath = rig ?? modelPath[..^4] + RigSuffix;
        if (readByPath(rigPath) is { } rigBytes)
        {
            Add(bundle, rigPath, "model/rig.json", Fc2ModelKind.Rig,
                JsonSerializer.SerializeToUtf8Bytes(SkeletonFile.Parse(rigBytes), Json), modelPath, usage);
        }

        foreach (string clipPath in clips ?? [])
        {
            if (readByPath(clipPath) is not { } clipBytes || bundle.Entry(clipPath) is not null)
            {
                continue;
            }

            MabFile bank = MabFile.Parse(clipBytes);
            string file = $"clips/{Name(clipPath)}.json";
            Add(bundle, clipPath, file, Fc2ModelKind.Clip,
                JsonSerializer.SerializeToUtf8Bytes(BankDocument.From(bank), Json), modelPath, usage);
            bundle.Manifest.Clips.Add(Index(bank, clipPath, file, Name(modelPath)));
        }
        return bundle;
    }

    /// <summary>
    /// What an editor needs to list a bank without opening it.
    /// </summary>
    /// <remarks>
    /// The timing is the root clip's, which is the character's - a bank plays as one thing, so that
    /// is the length to show. The participant record is the model's own: it says which bone the bank
    /// hangs the model from, which is the fact that decides where geometry belongs.
    /// </remarks>
    private static Fc2ModelClip Index(MabFile bank, string clipPath, string file, string model)
    {
        (int Tracks, int LastFrame, int Rate)? timing = null;
        foreach (int slot in (int[])
                 [MabClip.SectionKeyframeRotation, MabClip.SectionAnimatedTranslation,
                  MabClip.SectionRootTranslation, MabClip.SectionRootRotation])
        {
            timing ??= bank.TrackHeaderOf(slot);
        }

        MabParticipant? mine = bank.Participants()
            .FirstOrDefault(p => p.Name.Equals(model, StringComparison.OrdinalIgnoreCase));
        return new Fc2ModelClip
        {
            Path = clipPath,
            File = file,
            Label = Name(clipPath),
            Frames = timing?.LastFrame ?? 0,
            Rate = timing?.Rate ?? 0,
            Participant = mine?.Name,
            Bone = mine?.Parent,
        };
    }

    private static void AddTexture(
        Fc2ModelBundle bundle, string texturePath, Func<string, byte[]?> readByPath,
        string modelPath, Func<string, int>? usage)
    {
        if (texturePath.Length == 0 || bundle.Entry(texturePath) is not null
            || readByPath(texturePath) is not { } bytes)
        {
            return;
        }

        TextureDocument texture;
        try
        {
            texture = TextureDocument.From(bytes, readByPath);
        }
        catch (InvalidDataException)
        {
            return;
        }

        string name = Name(texturePath);
        string headerFile = $"textures/{name}.header.bin";
        bundle.Files[headerFile] = texture.Header;
        string? companionFile = null;
        if (texture.CompanionHeader is { } companion)
        {
            companionFile = $"textures/{name}_mip0.header.bin";
            bundle.Files[companionFile] = companion;
        }

        Add(bundle, texturePath, $"textures/{name}.png", Fc2ModelKind.Texture,
            texture.ToPng(), modelPath, usage,
            headerFile, companionFile, texture.Codec, texture.Levels);
    }

    private static void Add(
        Fc2ModelBundle bundle, string gamePath, string file, string kind, byte[] content,
        string modelPath, Func<string, int>? usage,
        string? header = null, string? companionHeader = null, string? codec = null, int? levels = null)
    {
        if (bundle.Entry(gamePath) is not null)
        {
            return;
        }

        int? count = usage?.Invoke(gamePath);
        bundle.Files[file] = content;
        bundle.Manifest.Entries.Add(new Fc2ModelEntry
        {
            Path = gamePath,
            File = file,
            Kind = kind,
            Role = Fc2ModelBundle.RoleOf(gamePath, modelPath, count, usage is not null),
            Usage = count,
            UsageSource = usage is not null ? "xref" : null,
            Sha256 = Fc2ModelBundle.Hash(content),
            Header = header,
            CompanionHeader = companionHeader,
            Codec = codec,
            Levels = levels,
        });
    }

    private static string Name(string gamePath)
    {
        string normalised = gamePath.Replace('\\', '/');
        int at = normalised.LastIndexOf('/');
        string file = at < 0 ? normalised : normalised[(at + 1)..];
        int dot = file.LastIndexOf('.');
        return dot < 0 ? file : file[..dot];
    }
}
