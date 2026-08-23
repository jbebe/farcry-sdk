using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;

namespace JackAll.Tools.Png;

/// <summary>
/// PNG in and out of straight RGBA, four bytes per pixel, row-major from the top.
/// </summary>
/// <remarks>
/// Writes 8-bit RGBA and reads every colour type, bit depth and interlace the format allows, so
/// whatever an editor hands back loads. Gamma and colour-profile chunks are read past on purpose:
/// a texture has to come out carrying the samples it went in with.
/// </remarks>
public static class PngImage
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    private const byte ColourGrey = 0;
    private const byte ColourRgb = 2;
    private const byte ColourPalette = 3;
    private const byte ColourGreyAlpha = 4;
    private const byte ColourRgba = 6;

    /// <summary>Adam7's seven passes: the pixel each starts at, and the grid it steps over.</summary>
    private static readonly (int X, int Y, int Dx, int Dy)[] Adam7 =
        [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    /// <summary>
    /// The passes an image is stored in, with the shape of each - a plain image is the single pass
    /// over every pixel, and a pass that lands on none of them is not stored at all.
    /// </summary>
    private static IEnumerable<(int X, int Y, int Dx, int Dy, int Columns, int Rows, int Stride)> Passes(PngHeader header)
    {
        foreach ((int x, int y, int dx, int dy) in header.Interlaced ? Adam7 : [(0, 0, 1, 1)])
        {
            int columns = Math.Max(0, header.Width - x + dx - 1) / dx;
            int rows = Math.Max(0, header.Height - y + dy - 1) / dy;
            if (columns > 0 && rows > 0)
            {
                yield return (x, y, dx, dy, columns, rows, header.Stride(columns));
            }
        }
    }

    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgba.Length != (long)width * height * 4)
        {
            throw new ArgumentException(
                $"{width}x{height} RGBA is {(long)width * height * 4} bytes, but {rgba.Length} were given.",
                nameof(rgba));
        }

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;
        ihdr[9] = ColourRgba;

        var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR"u8, ihdr);
        WriteChunk(png, "IDAT"u8, Compress(rgba, width, height));
        WriteChunk(png, "IEND"u8, default);
        return png.ToArray();
    }

    /// <summary>Tightly packed RGBA, four bytes per pixel, row-major from the top.</summary>
    public static (byte[] Rgba, int Width, int Height) Decode(ReadOnlySpan<byte> png)
    {
        (PngHeader header, byte[]? palette, byte[]? transparency, byte[] compressed) = ReadChunks(png);
        byte[] rgba = new byte[header.Width * header.Height * 4];
        byte[] raw = Inflate(compressed, header.RawLength);

        int at = 0;
        foreach ((int x0, int y0, int dx, int dy, int columns, int rows, int stride) in Passes(header))
        {
            byte[] row = new byte[stride];
            byte[] prior = new byte[stride];
            for (int y = 0; y < rows; y++)
            {
                byte filter = raw[at];
                raw.AsSpan(at + 1, stride).CopyTo(row);
                at += stride + 1;

                Unfilter(filter, row, prior, header.BytesPerPixel);
                Expand(header, palette, transparency, row, columns,
                       rgba.AsSpan(((y0 + (y * dy)) * header.Width) * 4), x0, dx);
                (prior, row) = (row, prior);
            }
        }

        return (rgba, header.Width, header.Height);
    }

    /// <summary>What IHDR said, and the sizes the rest of the decode derives from it.</summary>
    private readonly record struct PngHeader(int Width, int Height, int BitDepth, byte ColourType, bool Interlaced)
    {
        public int Channels => ColourType switch
        {
            ColourRgb => 3,
            ColourGreyAlpha => 2,
            ColourRgba => 4,
            _ => 1,
        };

        /// <summary>A whole pixel, rounded up to one byte - the offset a filter looks back by.</summary>
        public int BytesPerPixel => Math.Max(1, Channels * BitDepth / 8);

        public int Stride(int pixels) => ((pixels * Channels * BitDepth) + 7) / 8;

        /// <summary>Every pass's rows and their filter bytes, which is the whole inflated stream.</summary>
        public long RawLength => Passes(this).Sum(pass => (long)pass.Rows * (pass.Stride + 1));
    }

    private static (PngHeader Header, byte[]? Palette, byte[]? Transparency, byte[] Compressed) ReadChunks(
        ReadOnlySpan<byte> png)
    {
        if (!png.StartsWith(Signature))
        {
            throw new InvalidDataException("Not a PNG: the eight-byte signature is missing.");
        }

        PngHeader? header = null;
        byte[]? palette = null;
        byte[]? transparency = null;
        var idat = new MemoryStream();

        int at = Signature.Length;
        while (at + 12 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png[at..]);
            if (length < 0 || (long)at + 12 + length > png.Length)
            {
                throw new InvalidDataException($"A chunk at offset 0x{at:X} claims {length} bytes, past the end.");
            }

            ReadOnlySpan<byte> type = png.Slice(at + 4, 4);
            ReadOnlySpan<byte> data = png.Slice(at + 8, length);

            // Only critical chunks are checked. Ancillary ones are skipped whole, so a tool that
            // wrote a bad checksum into one has not damaged anything this reads.
            if ((type[0] & 0x20) == 0)
            {
                VerifyCrc(type, data, BinaryPrimitives.ReadUInt32BigEndian(png[(at + 8 + length)..]));
            }
            at += 12 + length;

            if (type.SequenceEqual("IHDR"u8))
            {
                header = ReadHeader(data);
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                palette = data.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                transparency = data.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                idat.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (header is not { } parsed)
        {
            throw new InvalidDataException("The PNG carries no IHDR.");
        }
        if (parsed.ColourType == ColourPalette && palette is null)
        {
            throw new InvalidDataException("A palettized PNG carries no PLTE.");
        }
        if (idat.Length == 0)
        {
            throw new InvalidDataException("The PNG carries no IDAT.");
        }
        return (parsed, palette, transparency, idat.ToArray());
    }

    private static PngHeader ReadHeader(ReadOnlySpan<byte> ihdr)
    {
        if (ihdr.Length < 13)
        {
            throw new InvalidDataException($"IHDR is {ihdr.Length} bytes, not 13.");
        }

        int width = BinaryPrimitives.ReadInt32BigEndian(ihdr);
        int height = BinaryPrimitives.ReadInt32BigEndian(ihdr[4..]);
        var header = new PngHeader(width, height, ihdr[8], ihdr[9], ihdr[12] == 1);

        if (width <= 0 || height <= 0 || (long)width * height * 4 > int.MaxValue)
        {
            throw new InvalidDataException($"{width}x{height} is not an image this can hold.");
        }
        if (ihdr[10] != 0 || ihdr[11] != 0 || ihdr[12] > 1)
        {
            throw new InvalidDataException(
                $"IHDR asks for compression {ihdr[10]}, filter {ihdr[11]}, interlace {ihdr[12]}; only 0, 0 and 0 or 1 exist.");
        }

        bool depthAllowed = header.ColourType switch
        {
            ColourGrey => header.BitDepth is 1 or 2 or 4 or 8 or 16,
            ColourPalette => header.BitDepth is 1 or 2 or 4 or 8,
            ColourRgb or ColourGreyAlpha or ColourRgba => header.BitDepth is 8 or 16,
            _ => false,
        };
        if (!depthAllowed)
        {
            throw new InvalidDataException(
                $"Colour type {header.ColourType} at {header.BitDepth} bits is not a combination the format has.");
        }
        return header;
    }

    private static byte[] Inflate(byte[] compressed, long expected)
    {
        if (expected > int.MaxValue)
        {
            throw new InvalidDataException($"The image needs {expected} bytes of scanlines, more than one array holds.");
        }

        using var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
        byte[] raw = new byte[expected];
        zlib.ReadExactly(raw);
        return raw;
    }

    /// <summary>Rebuild one scanline in place from the row above it, already rebuilt.</summary>
    private static void Unfilter(byte filter, Span<byte> row, ReadOnlySpan<byte> prior, int bytesPerPixel)
    {
        for (int i = 0; i < row.Length; i++)
        {
            byte a = i >= bytesPerPixel ? row[i - bytesPerPixel] : (byte)0;
            byte b = prior[i];
            byte c = i >= bytesPerPixel ? prior[i - bytesPerPixel] : (byte)0;
            row[i] += filter switch
            {
                0 => (byte)0,
                1 => a,
                2 => b,
                3 => (byte)((a + b) / 2),
                4 => Paeth(a, b, c),
                _ => throw new InvalidDataException($"Filter type {filter} is not one of the five."),
            };
        }
    }

    /// <summary>Whichever of the three neighbours the gradient a+b-c lands nearest.</summary>
    private static byte Paeth(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int da = Math.Abs(p - a);
        int db = Math.Abs(p - b);
        int dc = Math.Abs(p - c);
        return da <= db && da <= dc ? a : db <= dc ? b : c;
    }

    /// <summary>
    /// One pass's scanline, written out as RGBA - every colour type and bit depth lands here.
    /// </summary>
    private static void Expand(
        in PngHeader header, byte[]? palette, byte[]? transparency,
        ReadOnlySpan<byte> row, int columns, Span<byte> destination, int x0, int dx)
    {
        for (int i = 0; i < columns; i++)
        {
            Span<byte> pixel = destination.Slice((x0 + (i * dx)) * 4, 4);
            int first = i * header.Channels;

            if (header.ColourType == ColourPalette)
            {
                int index = Sample(row, first, header.BitDepth);
                if ((index * 3) + 2 >= palette!.Length)
                {
                    throw new InvalidDataException($"Palette index {index} is past the {palette.Length / 3} colours PLTE holds.");
                }
                palette.AsSpan(index * 3, 3).CopyTo(pixel);
                pixel[3] = transparency is not null && index < transparency.Length ? transparency[index] : (byte)255;
                continue;
            }

            bool grey = header.ColourType is ColourGrey or ColourGreyAlpha;
            for (int channel = 0; channel < 3; channel++)
            {
                pixel[channel] = Scale(Sample(row, first + (grey ? 0 : channel), header.BitDepth), header.BitDepth);
            }

            pixel[3] = header.ColourType switch
            {
                ColourGreyAlpha => Scale(Sample(row, first + 1, header.BitDepth), header.BitDepth),
                ColourRgba => Scale(Sample(row, first + 3, header.BitDepth), header.BitDepth),
                _ => Transparent(header, transparency, row, first) ? (byte)0 : (byte)255,
            };
        }
    }

    /// <summary>tRNS on a type that has no alpha channel names one colour that is fully clear.</summary>
    private static bool Transparent(in PngHeader header, byte[]? transparency, ReadOnlySpan<byte> row, int first)
    {
        if (transparency is null)
        {
            return false;
        }

        int channels = header.ColourType == ColourRgb ? 3 : 1;
        if (transparency.Length < channels * 2)
        {
            return false;
        }

        for (int channel = 0; channel < channels; channel++)
        {
            if (Sample(row, first + channel, header.BitDepth)
                != BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(channel * 2)))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>One channel of one pixel, at whatever width the file stores it, MSB first.</summary>
    private static int Sample(ReadOnlySpan<byte> row, int index, int bitDepth) => bitDepth switch
    {
        16 => BinaryPrimitives.ReadUInt16BigEndian(row[(index * 2)..]),
        8 => row[index],
        _ => (row[index * bitDepth / 8] >> (8 - bitDepth - (index * bitDepth % 8))) & ((1 << bitDepth) - 1),
    };

    /// <summary>A sample widened or narrowed to eight bits, keeping black black and white white.</summary>
    private static byte Scale(int sample, int bitDepth) => bitDepth switch
    {
        16 => (byte)(((sample * 255) + 32895) >> 16),
        8 => (byte)sample,
        _ => (byte)(sample * 255 / ((1 << bitDepth) - 1)),
    };

    /// <summary>Every scanline behind the filter that predicts it best, deflated as it is produced.</summary>
    /// <remarks>
    /// Best is the smallest sum of the filtered bytes read as signed, which is what every encoder
    /// uses: deflate spends the fewest bits on the run of small numbers that leaves.
    /// </remarks>
    private static byte[] Compress(ReadOnlySpan<byte> rgba, int width, int height)
    {
        const int bytesPerPixel = 4;
        int stride = width * bytesPerPixel;
        byte[] scanline = new byte[stride + 1];
        var idat = new MemoryStream();

        using (var zlib = new ZLibStream(idat, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                ReadOnlySpan<byte> row = rgba.Slice(y * stride, stride);
                ReadOnlySpan<byte> prior = y == 0 ? default : rgba.Slice((y - 1) * stride, stride);

                byte best = 0;
                long lowest = long.MaxValue;
                for (byte filter = 0; filter < 5; filter++)
                {
                    long score = 0;
                    for (int i = 0; i < stride; i++)
                    {
                        score += Math.Abs((int)(sbyte)Filtered(filter, row, prior, i, bytesPerPixel));
                    }
                    if (score < lowest)
                    {
                        (lowest, best) = (score, filter);
                    }
                }

                scanline[0] = best;
                for (int i = 0; i < stride; i++)
                {
                    scanline[i + 1] = Filtered(best, row, prior, i, bytesPerPixel);
                }
                zlib.Write(scanline);
            }
        }
        return idat.ToArray();
    }

    private static byte Filtered(byte filter, ReadOnlySpan<byte> row, ReadOnlySpan<byte> prior, int i, int bytesPerPixel)
    {
        byte a = i >= bytesPerPixel ? row[i - bytesPerPixel] : (byte)0;
        byte b = prior.IsEmpty ? (byte)0 : prior[i];
        byte c = i >= bytesPerPixel && !prior.IsEmpty ? prior[i - bytesPerPixel] : (byte)0;
        return (byte)(row[i] - filter switch
        {
            1 => a,
            2 => b,
            3 => (a + b) / 2,
            4 => Paeth(a, b, c),
            _ => 0,
        });
    }

    private static void WriteChunk(Stream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> framing = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(framing, data.Length);
        png.Write(framing);
        png.Write(type);
        png.Write(data);

        BinaryPrimitives.WriteUInt32BigEndian(framing, Checksum(type, data));
        png.Write(framing);
    }

    private static void VerifyCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data, uint stored)
    {
        if (Checksum(type, data) != stored)
        {
            throw new InvalidDataException(
                $"The {System.Text.Encoding.ASCII.GetString(type)} chunk fails its checksum; the file is damaged.");
        }
    }

    private static uint Checksum(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = new Crc32();
        crc.Append(type);
        crc.Append(data);
        return crc.GetCurrentHashAsUInt32();
    }
}
