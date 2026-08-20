using JackAll.Core.Format;

namespace JackAll.Tools.Xbt;

/// <summary>
/// A DDS payload's dimensions, FourCC and per-mip data slices, for callers that upload the
/// compressed blocks to a GPU as-is instead of decoding them.
/// </summary>
public sealed class DdsSurface
{
    public const uint FourCcDxt1 = 0x31545844;
    public const uint FourCcDxt3 = 0x33545844;
    public const uint FourCcDxt5 = 0x35545844;

    public required int Width { get; init; }
    public required int Height { get; init; }
    public required uint FourCc { get; init; }
    public required IReadOnlyList<byte[]> Mips { get; init; }

    /// <summary>
    /// The same surface with <paramref name="top"/>'s level 0 in front, which is how a texture whose
    /// header names a <c>_mip0.xbt</c> companion is meant to be read: this payload starts one level
    /// down, and the companion carries the level it is missing.
    /// </summary>
    /// <returns>This surface unchanged when the companion isn't exactly twice its size in the same
    /// format - the pairing is then not the one the engine assumes and stacking them would lie about
    /// the chain.</returns>
    public DdsSurface WithTopLevel(DdsSurface top)
    {
        if (top.FourCc != FourCc || top.Width != Width * 2 || top.Height != Height * 2)
        {
            return this;
        }

        return new DdsSurface
        {
            Width = top.Width,
            Height = top.Height,
            FourCc = FourCc,
            Mips = [top.Mips[0], .. Mips],
        };
    }

    /// <summary>Null for anything but the block-compressed DXT1/3/5 the game's textures use -
    /// those callers fall back to a full BCn decode instead.</summary>
    public static DdsSurface? TryParse(byte[] dds)
    {
        if (dds.Length < 128 || ByteCursor.U32(dds, 0) != 0x20534444)
        {
            return null;
        }

        int height = (int)ByteCursor.U32(dds, 12);
        int width = (int)ByteCursor.U32(dds, 16);
        int mipCount = Math.Max(1, (int)ByteCursor.U32(dds, 28));
        uint fourCc = ByteCursor.U32(dds, 84);
        int blockBytes = fourCc switch
        {
            FourCcDxt1 => 8,
            FourCcDxt3 or FourCcDxt5 => 16,
            _ => 0,
        };
        if (blockBytes == 0 || width <= 0 || height <= 0)
        {
            return null;
        }

        var mips = new List<byte[]>(mipCount);
        int offset = 128;
        int w = width, h = height;
        for (int mip = 0; mip < mipCount; mip++)
        {
            int size = Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * blockBytes;
            if (offset + size > dds.Length)
            {
                break;
            }

            mips.Add(dds[offset..(offset + size)]);
            offset += size;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        return mips.Count > 0 ? new DdsSurface { Width = width, Height = height, FourCc = fourCc, Mips = mips } : null;
    }
}
