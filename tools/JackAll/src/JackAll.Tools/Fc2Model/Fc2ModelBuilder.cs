using System.Text.Json;
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

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// Build a pack for one model.
    /// </summary>
    /// <param name="usage">How many files reference a path, when an authoritative index can say.
    /// Without one, ownership falls back to the directory rule, which is the safe direction.</param>
    public static Fc2ModelBundle Build(
        string modelPath, Func<string, byte[]?> readByPath, Func<string, int>? usage = null)
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

        string rigPath = modelPath[..^4] + RigSuffix;
        if (readByPath(rigPath) is { } rigBytes)
        {
            Add(bundle, rigPath, "model/rig.json", Fc2ModelKind.Rig,
                JsonSerializer.SerializeToUtf8Bytes(SkeletonFile.Parse(rigBytes), Json), modelPath, usage);
        }
        return bundle;
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
