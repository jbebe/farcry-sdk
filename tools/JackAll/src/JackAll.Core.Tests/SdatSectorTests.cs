using JackAll.Core.Format;

namespace JackAll.Core.Tests;

/// <summary>
/// No real .sdat sample exists in this repo, so these only prove the codec is internally consistent -
/// correct offsets/endianness/round-tripping against a synthetic sector - not that it matches a real
/// shipped sector file byte-for-byte the way the .fcb round-trip tests do. The byte layout itself was
/// confirmed by decompiling both CSector::ExportSectorDataChunk and CSector::Load in the
/// FarCry2_server binary and checking every offset/size constant matches between the two, which is a
/// stronger basis than the community guess this class replaced, but "internally consistent" and
/// "matches retail files" are still two different claims.
/// </summary>
public class SdatSectorTests
{
    private static SdatSector SyntheticSector(int recordCount = 3)
    {
        var grid = new SdatGridCell[SdatSectorFile.GridSize, SdatSectorFile.GridSize];
        for (int row = 0; row < SdatSectorFile.GridSize; row++)
        {
            for (int col = 0; col < SdatSectorFile.GridSize; col++)
            {
                // A pattern with no accidental symmetry, so a row/column or byte-order transposition
                // bug would actually change the result instead of getting lucky.
                var height = (ushort)((row * 37 + col * 3) % ushort.MaxValue);
                var byte2 = (byte)((row + col * 5) % 256);
                var byte3 = (byte)((row * 7 + col) % 256);
                grid[row, col] = new SdatGridCell(height, byte2, byte3);
            }
        }

        var envSettings = new byte[538];
        for (int i = 0; i < envSettings.Length; i++) envSettings[i] = (byte)(i * 3);

        var records = new byte[recordCount * 12];
        for (int i = 0; i < records.Length; i++) records[i] = (byte)(i + 1);

        return new SdatSector
        {
            SectorId = 0x12345678,
            Flags = 0x03,
            X = 128.5f,
            Y = -64.25f,
            UnknownHeaderField = 42,
            FormatFlag = 1,
            EnvSettingsRaw = envSettings,
            HeaderPadding = [0xAA, 0xBB],
            Grid = grid,
            MaskTablesRaw = Enumerable.Range(0, 5976).Select(i => (byte)i).ToArray(),
            RecordsRaw = records,
            TrailingField = 0xDEADBEEF,
            TailBlockRaw = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16],
        };
    }

    [Fact]
    public void Encode_then_decode_reproduces_the_exact_same_sector()
    {
        SdatSector original = SyntheticSector();

        byte[] encoded = SdatSectorFile.Encode(original);
        SdatSector decoded = SdatSectorFile.Decode(encoded);

        Assert.Equal(original.SectorId, decoded.SectorId);
        Assert.Equal(original.Flags, decoded.Flags);
        Assert.Equal(original.X, decoded.X);
        Assert.Equal(original.Y, decoded.Y);
        Assert.Equal(original.UnknownHeaderField, decoded.UnknownHeaderField);
        Assert.Equal(original.FormatFlag, decoded.FormatFlag);
        Assert.Equal(original.EnvSettingsRaw, decoded.EnvSettingsRaw);
        Assert.Equal(original.HeaderPadding, decoded.HeaderPadding);
        Assert.Equal(original.MaskTablesRaw, decoded.MaskTablesRaw);
        Assert.Equal(original.RecordsRaw, decoded.RecordsRaw);
        Assert.Equal(original.TrailingField, decoded.TrailingField);
        Assert.Equal(original.TailBlockRaw, decoded.TailBlockRaw);
        for (int row = 0; row < SdatSectorFile.GridSize; row++)
        {
            for (int col = 0; col < SdatSectorFile.GridSize; col++)
            {
                Assert.Equal(original.Grid[row, col], decoded.Grid[row, col]);
            }
        }
    }

    [Fact]
    public void Encode_produces_the_size_the_header_declares()
    {
        byte[] encoded = SdatSectorFile.Encode(SyntheticSector(recordCount: 5));

        uint totalSize = (uint)(encoded[8] | (encoded[9] << 8) | (encoded[10] << 16) | (encoded[11] << 24));
        Assert.Equal((uint)encoded.Length, totalSize);
    }

    [Fact]
    public void Grid_height_is_little_endian()
    {
        SdatSector sector = SyntheticSector();
        sector.Grid[0, 0] = new SdatGridCell(0x1234, 0, 0);

        byte[] encoded = SdatSectorFile.Encode(sector);

        // Chunk header (0x14) + metadata block (0x23C) = start of the packed grid.
        int gridOffset = 0x14 + 0x23C;
        Assert.Equal(0x34, encoded[gridOffset]);
        Assert.Equal(0x12, encoded[gridOffset + 1]);
    }

    [Fact]
    public void Decode_rejects_wrong_magic()
    {
        byte[] encoded = SdatSectorFile.Encode(SyntheticSector());
        encoded[0] = (byte)~encoded[0];

        Assert.Throws<InvalidDataException>(() => SdatSectorFile.Decode(encoded));
    }

    [Fact]
    public void Decode_rejects_wrong_version()
    {
        byte[] encoded = SdatSectorFile.Encode(SyntheticSector());
        encoded[4] = 6;

        Assert.Throws<InvalidDataException>(() => SdatSectorFile.Decode(encoded));
    }

    [Fact]
    public void Decode_rejects_a_truncated_file()
    {
        Assert.Throws<InvalidDataException>(() => SdatSectorFile.Decode(new byte[100]));
    }

    [Fact]
    public void Encode_rejects_a_mis_sized_grid()
    {
        SdatSector sector = SyntheticSector();
        SdatSector broken = sector with { Grid = new SdatGridCell[10, 10] };

        Assert.Throws<ArgumentException>(() => SdatSectorFile.Encode(broken));
    }

    [Theory]
    [InlineData((ushort)0, 0f)]
    [InlineData((ushort)128, 1f)]
    [InlineData((ushort)12800, 100f)]
    public void HeightMeters_divides_by_128(ushort raw, float expectedMeters)
    {
        var cell = new SdatGridCell(raw, 0, 0);
        Assert.Equal(expectedMeters, cell.HeightMeters, precision: 3);
    }

    [Fact]
    public void MaterialIndex_is_the_low_nibble_of_RawByte3()
    {
        var cell = new SdatGridCell(0, 0, 0xF7);
        Assert.Equal(7, cell.MaterialIndex);
    }
}
