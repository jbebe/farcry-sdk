using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using JackAll.Tools.Png;

namespace JackAll.Tests;

/// <summary>The PNG codec, over its own round trip and over files assembled by hand.</summary>
public class PngImageTests
{
    /// <summary>Gradients, a hard edge and a transparent block, so no filter wins every row.</summary>
    private static byte[] Source(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int at = ((y * width) + x) * 4;
                rgba[at] = (byte)(x * 7);
                rgba[at + 1] = (byte)(y * 13);
                rgba[at + 2] = (byte)((x ^ y) * 3);
                rgba[at + 3] = x < width / 2 ? (byte)255 : x < width * 3 / 4 ? (byte)0 : (byte)137;
            }
        }
        return rgba;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 23)]
    [InlineData(23, 1)]
    [InlineData(7, 3)]
    [InlineData(64, 64)]
    [InlineData(100, 63)]
    public void Writes_pixels_that_come_back_unchanged(int width, int height)
    {
        byte[] source = Source(width, height);

        (byte[] rgba, int decodedWidth, int decodedHeight) = PngImage.Decode(PngImage.Encode(source, width, height));

        Assert.Equal((width, height), (decodedWidth, decodedHeight));
        Assert.Equal(source, rgba);
    }

    /// <summary>The colour-keyed transparency an editor never writes and a reader still owes.</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    public void Reads_a_colour_key_as_transparent(byte colourType, int channels)
    {
        // Four levels, 0 to 3, as grey or as grey repeated across RGB, with level 2 keyed out.
        byte[] samples = colourType == 0 ? [0, 1, 2, 3] : [0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3];
        var key = new byte[channels * 2];
        for (int channel = 0; channel < channels; channel++)
        {
            key[(channel * 2) + 1] = 2;
        }

        byte[] png = Assemble(4, 1, 8, colourType, [0, .. samples], ("tRNS", key));

        (byte[] rgba, _, _) = PngImage.Decode(png);

        Assert.Equal([0, 0, 0, 255, 1, 1, 1, 255, 2, 2, 2, 0, 3, 3, 3, 255], rgba);
    }

    [Fact]
    public void Refuses_a_damaged_critical_chunk()
    {
        byte[] png = PngImage.Encode(Source(16, 16), 16, 16);
        png[DataOf(png, "IDAT")] ^= 0xFF;

        Assert.Throws<InvalidDataException>(() => PngImage.Decode(png));
    }

    /// <summary>An ancillary chunk is skipped whole, so a bad checksum in one has damaged nothing.</summary>
    [Fact]
    public void Reads_past_a_damaged_ancillary_chunk()
    {
        byte[] png = Assemble(1, 1, 8, 0, [0, 200], ("tEXt", "Author\0nobody"u8.ToArray()));
        png[DataOf(png, "tEXt")] ^= 0xFF;

        Assert.Equal([200, 200, 200, 255], PngImage.Decode(png).Rgba);
    }

    /// <summary>Where a chunk's data starts, so a test can damage it without counting bytes.</summary>
    private static int DataOf(byte[] png, string type)
    {
        int at = 8;
        while (!png.AsSpan(at + 4, 4).SequenceEqual(System.Text.Encoding.ASCII.GetBytes(type)))
        {
            at += 12 + BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
        }
        return at + 8;
    }

    [Fact]
    public void Refuses_bytes_that_are_not_a_png()
    {
        Assert.Throws<InvalidDataException>(() => PngImage.Decode("DDS "u8.ToArray()));
    }

    [Fact]
    public void Refuses_pixels_that_do_not_fill_the_image()
    {
        Assert.Throws<ArgumentException>(() => PngImage.Encode(new byte[16], 4, 4));
    }

    /// <summary>A PNG built by hand, for the chunks and layouts our own writer never emits.</summary>
    private static byte[] Assemble(
        int width, int height, byte bitDepth, byte colourType, byte[] scanlines,
        params (string Type, byte[] Data)[] ahead)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = bitDepth;
        ihdr[9] = colourType;

        var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(scanlines);
        }

        var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Chunk(png, "IHDR", ihdr);
        foreach ((string type, byte[] data) in ahead)
        {
            Chunk(png, type, data);
        }
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        byte[] name = System.Text.Encoding.ASCII.GetBytes(type);
        var framing = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(framing, data.Length);
        png.Write(framing);
        png.Write(name);
        png.Write(data);

        var crc = new Crc32();
        crc.Append(name);
        crc.Append(data);
        BinaryPrimitives.WriteUInt32BigEndian(framing, crc.GetCurrentHashAsUInt32());
        png.Write(framing);
    }
}
