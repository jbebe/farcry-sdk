using CommunityToolkit.HighPerformance;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using JackAll.Tools.Xbt;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace JackAll.Tools.Fc2Model;

/// <summary>
/// A texture with no Dunia bytes in it: one PNG at full resolution, plus the header bytes nothing
/// can synthesize.
/// </summary>
/// <remarks>
/// Around half of all textures split their top level into a sibling <c>_mip0.xbt</c>, and the two
/// are not what they look like - the base file holds the complete mip chain of the
/// <b>half-resolution</b> image and the companion holds a <b>single level at twice</b> the
/// dimensions. Inverted, a texture is half or double resolution in game only, because an editor
/// loads the companion and shows the right thing either way. So the pack carries the merged image
/// and the split is rebuilt here.
/// <para>
/// The header travels verbatim because <c>Reserved</c> is a bitfield the streaming loader consumes
/// and <c>Hash</c> is a per-asset id nothing derives - see docs/docs/file-formats/xbt.md.
/// </para>
/// </remarks>
public sealed class TextureDocument
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>What to re-encode as, so a trip through the pack cannot change compression.</summary>
    public required string Codec { get; init; }

    /// <summary>How many levels the chain held, counting the companion's when there is one.</summary>
    public required int Levels { get; init; }

    /// <summary>The base file's header, verbatim.</summary>
    public required byte[] Header { get; init; }

    /// <summary>The companion's header, or null when the texture is a single file.</summary>
    public byte[]? CompanionHeader { get; init; }

    /// <summary>The merged image, RGBA, four bytes per pixel.</summary>
    public required byte[] Rgba { get; init; }

    public static string NameOf(uint fourCc) => fourCc switch
    {
        DdsSurface.FourCcDxt1 => "DXT1",
        DdsSurface.FourCcDxt3 => "DXT3",
        DdsSurface.FourCcDxt5 => "DXT5",
        _ => throw new NotSupportedException($"Unsupported texture codec 0x{fourCc:X8}."),
    };

    public static uint FourCcOf(string codec) => codec switch
    {
        "DXT1" => DdsSurface.FourCcDxt1,
        "DXT3" => DdsSurface.FourCcDxt3,
        "DXT5" => DdsSurface.FourCcDxt5,
        _ => throw new NotSupportedException($"Unsupported texture codec '{codec}'."),
    };

    public static CompressionFormat FormatOf(string codec) => codec switch
    {
        "DXT1" => CompressionFormat.Bc1,
        "DXT3" => CompressionFormat.Bc2,
        "DXT5" => CompressionFormat.Bc3,
        _ => throw new NotSupportedException($"Unsupported texture codec '{codec}'."),
    };

    /// <summary>
    /// Decode a texture and its companion into one image.
    /// </summary>
    /// <param name="readByPath">Resolves the companion the header names; a companion that cannot be
    /// read simply leaves the smaller chain, which is what the engine would draw anyway.</param>
    /// <summary>A payload stored as plain pixels rather than blocks, which cannot be re-encoded.</summary>
    public const string UncompressedCodec = "uncompressed";

    public bool IsUncompressed => Codec == UncompressedCodec;

    public static TextureDocument From(byte[] xbt, Func<string, byte[]?> readByPath)
    {
        (byte[] header, byte[] payload) = XbtTexture.Split(xbt);
        if (XbtSurface.TryRead(xbt, readByPath) is not { } surface)
        {
            // Two shipped model textures are plain 32-bit pixels, both sky domes. They decode, so
            // they can travel and be looked at; they just cannot be compressed back.
            if (XbtPixels.TryDecodeDds(payload) is not { } plain)
            {
                throw new InvalidDataException("Not a readable .xbt, and its payload is not pixels either.");
            }

            return new TextureDocument
            {
                Width = plain.Width,
                Height = plain.Height,
                Codec = UncompressedCodec,
                Levels = 1,
                Header = header,
                Rgba = plain.Rgba,
            };
        }

        byte[]? companionHeader = null;
        if (XbtTexture.CompanionPath(header) is { } path && readByPath(path) is { } companion)
        {
            (companionHeader, _) = XbtTexture.Split(companion);
        }

        // The merged chain's level 0 is the full-resolution image, whichever file it came from.
        byte[] dds = Rebuild(surface);
        if (XbtPixels.TryDecodeDds(dds) is not { } decoded)
        {
            throw new InvalidDataException("The merged payload did not decode.");
        }

        return new TextureDocument
        {
            Width = decoded.Width,
            Height = decoded.Height,
            Codec = NameOf(surface.FourCc),
            Levels = surface.Mips.Count,
            Header = header,
            CompanionHeader = companionHeader,
            Rgba = decoded.Rgba,
        };
    }

    public byte[] ToPng()
    {
        using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(Rgba, Width, Height);
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    public static byte[] RgbaFromPng(byte[] png, out int width, out int height)
    {
        using Image<Rgba32> image = Image.Load<Rgba32>(png);
        width = image.Width;
        height = image.Height;
        var rgba = new byte[width * height * 4];
        image.CopyPixelDataTo(rgba);
        return rgba;
    }

    /// <summary>
    /// The pair of files this texture ships as: the base, and its companion when it had one.
    /// </summary>
    /// <remarks>
    /// The split is the inverse of the merge: level 0 goes to the companion on its own, and
    /// everything below it becomes the base's whole chain.
    /// </remarks>
    public (byte[] Base, byte[]? Companion) ToXbt()
    {
        if (IsUncompressed)
        {
            throw new NotSupportedException(
                "This texture ships as plain pixels rather than blocks, and writing that form back "
                + "is not implemented - only two shipped model textures use it, both sky domes. "
                + "Applying a pack skips entries the editor did not change, so this only bites if "
                + "one of them was edited.");
        }

        var encoder = new BcEncoder(FormatOf(Codec))
        {
            OutputOptions = { GenerateMipMaps = true, MaxMipMapLevel = Levels },
        };
        byte[][] levels = [.. encoder.EncodeToRawBytes(Pixels())];

        if (CompanionHeader is null)
        {
            return (XbtTexture.Combine(Header, Dds(levels, Width, Height, FourCcOf(Codec), 0)), null);
        }

        // The base starts one level down, and the companion carries the level it is missing.
        return (
            XbtTexture.Combine(Header, Dds(levels, Width / 2, Height / 2, FourCcOf(Codec), 1)),
            XbtTexture.Combine(CompanionHeader, Dds(levels, Width, Height, FourCcOf(Codec), 0, 1)));
    }

    /// <summary>The image as the encoder wants it, rows first.</summary>
    private ReadOnlyMemory2D<ColorRgba32> Pixels()
    {
        var pixels = new ColorRgba32[Height, Width];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int at = ((y * Width) + x) * 4;
                pixels[y, x] = new ColorRgba32(Rgba[at], Rgba[at + 1], Rgba[at + 2], Rgba[at + 3]);
            }
        }
        return pixels;
    }

    private static byte[] Rebuild(DdsSurface surface)
        => Dds([.. surface.Mips], surface.Width, surface.Height, surface.FourCc, 0);

    private static byte[] Dds(byte[][] levels, int width, int height, uint fourCc, int from, int? count = null)
    {
        int take = Math.Min(count ?? levels.Length - from, levels.Length - from);
        var dds = new byte[128 + levels.Skip(from).Take(take).Sum(level => level.Length)];

        // A DDS header is 128 bytes; only the fields DdsSurface.TryParse reads are filled, which is
        // also everything a decoder needs to walk the chain.
        System.Text.Encoding.ASCII.GetBytes("DDS ").CopyTo(dds, 0);
        BitConverter.GetBytes(124).CopyTo(dds, 4);
        BitConverter.GetBytes(0x000A1007).CopyTo(dds, 8);
        BitConverter.GetBytes(height).CopyTo(dds, 12);
        BitConverter.GetBytes(width).CopyTo(dds, 16);
        BitConverter.GetBytes(take).CopyTo(dds, 28);
        BitConverter.GetBytes(32).CopyTo(dds, 76);
        BitConverter.GetBytes(0x00000004).CopyTo(dds, 80);
        BitConverter.GetBytes(fourCc).CopyTo(dds, 84);

        int at = 128;
        foreach (byte[] level in levels.Skip(from).Take(take))
        {
            level.CopyTo(dds, at);
            at += level.Length;
        }
        return dds;
    }
}
