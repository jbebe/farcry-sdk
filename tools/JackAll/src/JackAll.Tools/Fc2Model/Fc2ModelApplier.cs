using System.Text.Json;
using JackAll.Tools.Skeleton;
using JackAll.Tools.Xbt;

namespace JackAll.Tools.Fc2Model;

/// <summary>One game file a pack wants written, and where it goes.</summary>
public sealed class Fc2ModelOutput
{
    public required string Path { get; init; }

    public required byte[] Content { get; init; }
}

/// <summary>
/// Turns a pack's decoded contents back into the files the game reads.
/// </summary>
/// <remarks>
/// Only entries an editor actually changed are produced. That is not an optimisation: a texture
/// travels as PNG, so re-encoding one on every apply would compress it again each time and decay it
/// across saves for no reason. An untouched entry is left alone and the install keeps what it had.
/// </remarks>
public static class Fc2ModelApplier
{
    private static readonly JsonSerializerOptions Json = Fc2ModelJson.Compact;

    /// <summary>What applying this pack would write.</summary>
    public static List<Fc2ModelOutput> Outputs(Fc2ModelBundle bundle, bool onlyModified = true)
    {
        List<Fc2ModelOutput> outputs = [];
        foreach (Fc2ModelEntry entry in bundle.Manifest.Entries)
        {
            if (onlyModified && !entry.Modified)
            {
                continue;
            }

            // A bank is shared by the directory rule and that rule is too blunt for one: what
            // matters is which clip inside it changed, and an editor can only rewrite the clip
            // belonging to this pack's own rig - every other clip in the chain carries its sections
            // verbatim and goes back byte for byte.
            if (entry.Role == Fc2ModelBundle.Shared && entry.Modified
                && entry.Kind != Fc2ModelKind.Clip)
            {
                throw new InvalidOperationException(
                    $"'{entry.Path}' is shared with other models and was edited. Applying it would "
                    + "change every one of them.");
            }

            outputs.AddRange(Produce(bundle, entry));
        }
        return outputs;
    }

    private static IEnumerable<Fc2ModelOutput> Produce(Fc2ModelBundle bundle, Fc2ModelEntry entry)
    {
        byte[] content = bundle.Content(entry);
        switch (entry.Kind)
        {
            case Fc2ModelKind.Mesh:
                yield return new Fc2ModelOutput
                {
                    Path = entry.Path,
                    Content = Deserialize<MeshDocument>(content).ToXbg().Write(),
                };
                break;

            case Fc2ModelKind.Material:
                yield return new Fc2ModelOutput
                {
                    Path = entry.Path,
                    Content = Deserialize<MaterialDocument>(content).ToXbm(),
                };
                break;

            case Fc2ModelKind.Rig:
                yield return new Fc2ModelOutput
                {
                    Path = entry.Path,
                    Content = Deserialize<SkeletonFile>(content).Write(),
                };
                break;

            case Fc2ModelKind.Clip:
                yield return new Fc2ModelOutput
                {
                    Path = entry.Path,
                    Content = Deserialize<BankDocument>(content).ToMab(),
                };
                break;

            case Fc2ModelKind.Texture:
                foreach (Fc2ModelOutput output in Texture(bundle, entry, content))
                {
                    yield return output;
                }
                break;

            default:
                throw new InvalidDataException($"Nothing writes a '{entry.Kind}' entry back yet.");
        }
    }

    /// <summary>
    /// A texture, split back into the pair the engine streams.
    /// </summary>
    /// <remarks>
    /// The pack holds one image at full resolution; the base file takes the chain from one level
    /// down and the companion takes level zero on its own. Inverted, the texture is half or double
    /// resolution in game only.
    /// </remarks>
    private static IEnumerable<Fc2ModelOutput> Texture(
        Fc2ModelBundle bundle, Fc2ModelEntry entry, byte[] png)
    {
        byte[] rgba = TextureDocument.RgbaFromPng(png, out int width, out int height);
        var document = new TextureDocument
        {
            Width = width,
            Height = height,
            Codec = entry.Codec ?? throw new InvalidDataException($"'{entry.Path}' names no codec."),
            Levels = entry.Levels ?? 1,
            Header = bundle.Files[entry.Header ?? throw new InvalidDataException($"'{entry.Path}' carries no header.")],
            CompanionHeader = entry.CompanionHeader is { } companion ? bundle.Files[companion] : null,
            Rgba = rgba,
        };

        (byte[] baseFile, byte[]? companionFile) = document.ToXbt();
        yield return new Fc2ModelOutput { Path = entry.Path, Content = baseFile };
        if (companionFile is not null)
        {
            yield return new Fc2ModelOutput
            {
                Path = XbtTexture.CompanionPath(document.Header)
                    ?? throw new InvalidDataException($"'{entry.Path}' has a companion its header does not name."),
                Content = companionFile,
            };
        }
    }

    private static T Deserialize<T>(byte[] content)
        => JsonSerializer.Deserialize<T>(content, Json)
           ?? throw new InvalidDataException($"Unreadable {typeof(T).Name} in the pack.");
}
