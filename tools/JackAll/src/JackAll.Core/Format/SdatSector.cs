namespace JackAll.Core.Format;

/// <summary>
/// One 65x65-vertex grid cell: a packed 4-byte record from the terrain sector's height/material grid.
/// </summary>
/// <remarks>
/// Only <see cref="RawHeight"/> (confirmed against <c>CSector::GetZApr</c>'s bilinear height sampler)
/// and the low nibble of <see cref="RawByte3"/> (confirmed against the LOD triangle-index builders,
/// which use it to pick a hole/tessellation pattern per quad) are understood. Both raw bytes are kept
/// in full - not masked down to just the known bits - so a decode-then-encode round-trip with no edits
/// reproduces the source file exactly even though part of this record's meaning is still unknown.
/// </remarks>
public readonly record struct SdatGridCell(ushort RawHeight, byte RawByte2, byte RawByte3)
{
    /// <summary>Low nibble of <see cref="RawByte3"/> - a hole/tessellation-pattern selector (0-15) per quad.</summary>
    public byte MaterialIndex => (byte)(RawByte3 & 0x0F);

    /// <summary>Height in meters. Scale factor is provisional - see <see cref="SdatSectorFile.MetersPerUnit"/>.</summary>
    public float HeightMeters => RawHeight * SdatSectorFile.MetersPerUnit;
}

/// <summary>A decoded Far Cry 2 world-sector terrain file (.sdat).</summary>
public sealed record SdatSector
{
    public required uint SectorId { get; init; }
    public required uint Flags { get; init; }
    public required float X { get; init; }
    public required float Y { get; init; }

    /// <summary>Header field at metadata offset +0x10 (source: CSector+0x24). Identity untraced - preserved verbatim.</summary>
    public required uint UnknownHeaderField { get; init; }

    /// <summary>Header field at metadata offset +0x1C, always 1 in every export path traced. Preserved verbatim.</summary>
    public required uint FormatFlag { get; init; }

    /// <summary>538-byte snapshot of <c>GetEnvSettings()</c>, embedded verbatim per sector. Not decoded.</summary>
    public required byte[] EnvSettingsRaw { get; init; }

    /// <summary>2 trailing bytes of the metadata block, purpose unknown. Preserved verbatim.</summary>
    public required byte[] HeaderPadding { get; init; }

    /// <summary>[row, col] grid, <see cref="SdatSectorFile.GridSize"/> x <see cref="SdatSectorFile.GridSize"/>.</summary>
    public required SdatGridCell[,] Grid { get; init; }

    /// <summary>
    /// Multi-resolution hole/visibility bitmasks and per-node bounds built by
    /// <c>CTerrainSectorGenericCompiler::PreparePackedDataForExport</c>, immediately following the
    /// grid in the packed blob. Not decoded - preserved verbatim.
    /// </summary>
    public required byte[] MaskTablesRaw { get; init; }

    /// <summary>The variable-length "quad LOD/hole" record array (12 bytes each). Not decoded - preserved verbatim.</summary>
    public required byte[] RecordsRaw { get; init; }

    /// <summary>Trailing 4-byte field after the record array, purpose unknown. Preserved verbatim.</summary>
    public required uint TrailingField { get; init; }

    /// <summary>
    /// Final 16-byte block, round-trips into the live <c>CSceneTerrainSector</c> object's
    /// +0xc4/+0xc8/+0xcc/+0xd0 fields (shape suggests a bounding box or height range). Not decoded -
    /// preserved verbatim.
    /// </summary>
    public required byte[] TailBlockRaw { get; init; }
}

/// <summary>
/// Decodes/encodes a Far Cry 2 multiplayer world-sector terrain file (.sdat) - one of 64 (8x8 grid,
/// <c>sd0.sdat</c> .. <c>sd63.sdat</c>) generic engine "chunk" files whose sole reader/writer in the
/// shipped engine is <c>CSector::Load</c> / <c>CSector::ExportSectorDataChunk</c>.
/// </summary>
/// <remarks>
/// Reverse-engineered against <c>FarCry2_server</c> (the Linux dedicated-server ELF, which carries far
/// richer symbols than the Windows <c>Dunia.dll</c>): the container framing was confirmed by
/// decompiling both <c>CSector::ExportSectorDataChunk</c> (the writer) and <c>CSector::Load</c> (the
/// reader) and checking every offset/size constant matches between the two; the height encoding was
/// pinned down separately via <c>CSector::GetZApr</c> (the server's terrain-collision height query).
///
/// This supersedes an earlier community-sourced guess (a raw, headerless 513x513 <c>u16</c> grid, no
/// header) that turned out to be wrong: the real per-sector native resolution is <b>65x65 vertices
/// (64x64 quads)</b> wrapped in a generic chunked container - 513 is the *whole multiplayer map's*
/// vertex count (8 sectors x 64 quads + 1 shared edge), not one file's.
///
/// Confirmed byte layout (all little-endian):
/// <code>
/// 0x0000  u32   Magic = 0xE9001052 (hardcoded literal, not a runtime hash)
/// 0x0004  u32   Version = 7
/// 0x0008  u32   TotalSize    = 0x14 + OwnDataSize
/// 0x000C  u32   OwnDataSize  = 0x5BAC + RecordCount*12
/// 0x0010  u32   ChildChunkCount = 0 (always, for this file)
/// 0x0014  572   metadata block (id, flags, x/y, RecordCount, a constant echo of PackedBlobSize, a
///                538-byte env-settings snapshot, 2 bytes of unaccounted padding)
/// 0x0250  22876 packed blob: a 65x65 grid of 4-byte cells (row-major, row stride 0x104) followed by
///                mip-level hole-mask/bounds tables
/// 0x5BAC  N*12  "quad LOD/hole" record array (N = RecordCount)
/// +N      4     trailing packed-data field
/// +N+4    16    tail block
/// </code>
/// </remarks>
public static class SdatSectorFile
{
    public const uint Magic = 0xE9001052;
    public const uint ExpectedVersion = 7;

    public const int GridSize = 65;
    private const int GridCellSize = 4;
    private const int GridByteSize = GridSize * GridSize * GridCellSize; // 0x4204 = 16,900

    private const int ChunkHeaderSize = 0x14;
    private const int MetadataSize = 0x23C;
    private const int EnvSettingsSize = 0x21A;
    private const int HeaderPaddingSize = MetadataSize - (0x20 + EnvSettingsSize); // 2
    private const int PackedBlobSize = 0x595C; // 22,876
    private const int MaskTablesSize = PackedBlobSize - GridByteSize; // 0x1758 = 5,976
    private const int RecordSize = 12;
    private const int TrailingFieldSize = 4;
    private const int TailBlockSize = 16;

    /// <summary>
    /// In-game meters per raw height unit, per <c>CSector::GetZApr</c>'s scale constant. The constant's
    /// address was confirmed via PIC-relative disassembly, but its byte value wasn't independently read
    /// (no raw-memory-read tool was available) - this is the community-sourced figure, carried over
    /// provisionally.
    /// </summary>
    public const float MetersPerUnit = 1f / 128f;

    public static SdatSector Decode(byte[] sdat)
    {
        if (sdat.Length < ChunkHeaderSize + MetadataSize)
        {
            throw new InvalidDataException("File too small to be a .sdat sector chunk.");
        }

        uint magic = ReadU32(sdat, 0);
        if (magic != Magic)
        {
            throw new InvalidDataException($"Not a .sdat sector chunk (magic 0x{magic:X8}, expected 0x{Magic:X8}).");
        }

        uint version = ReadU32(sdat, 4);
        if (version != ExpectedVersion)
        {
            throw new InvalidDataException($"Unsupported .sdat chunk version {version} (expected {ExpectedVersion}).");
        }

        uint totalSize = ReadU32(sdat, 8);
        uint ownDataSize = ReadU32(sdat, 0xC);
        uint childChunkCount = ReadU32(sdat, 0x10);
        if (childChunkCount != 0)
        {
            throw new InvalidDataException("This .sdat has nested chunks (ChildChunkCount != 0) - not supported.");
        }
        if (totalSize != ChunkHeaderSize + ownDataSize)
        {
            throw new InvalidDataException("Chunk TotalSize/OwnDataSize mismatch - corrupt .sdat header.");
        }
        if (sdat.Length != totalSize)
        {
            throw new InvalidDataException(
                $"File length ({sdat.Length}) doesn't match the chunk's declared TotalSize ({totalSize}).");
        }

        const int metaOffset = ChunkHeaderSize;
        uint sectorId = ReadU32(sdat, metaOffset + 0x00);
        uint flags = ReadU32(sdat, metaOffset + 0x04);
        float x = ReadF32(sdat, metaOffset + 0x08);
        float y = ReadF32(sdat, metaOffset + 0x0C);
        uint unknownHeaderField = ReadU32(sdat, metaOffset + 0x10);
        uint packedBlobSizeField = ReadU32(sdat, metaOffset + 0x14);
        uint recordCount = ReadU32(sdat, metaOffset + 0x18);
        uint formatFlag = ReadU32(sdat, metaOffset + 0x1C);
        byte[] envSettingsRaw = sdat[(metaOffset + 0x20)..(metaOffset + 0x20 + EnvSettingsSize)];
        byte[] headerPadding = sdat[(metaOffset + 0x23A)..(metaOffset + MetadataSize)];

        if (packedBlobSizeField != PackedBlobSize)
        {
            throw new InvalidDataException(
                $"Header's packed-blob-size field (0x{packedBlobSizeField:X}) doesn't match the expected 0x{PackedBlobSize:X} - corrupt .sdat header.");
        }
        long expectedOwnDataSize = MetadataSize + PackedBlobSize + (long)recordCount * RecordSize + TrailingFieldSize + TailBlockSize;
        if (ownDataSize != expectedOwnDataSize)
        {
            throw new InvalidDataException("OwnDataSize doesn't match RecordCount - corrupt .sdat header.");
        }

        int blobOffset = metaOffset + MetadataSize;
        var grid = new SdatGridCell[GridSize, GridSize];
        for (int row = 0; row < GridSize; row++)
        {
            for (int col = 0; col < GridSize; col++)
            {
                int cellOffset = blobOffset + (row * GridSize + col) * GridCellSize;
                ushort height = (ushort)(sdat[cellOffset] | (sdat[cellOffset + 1] << 8));
                grid[row, col] = new SdatGridCell(height, sdat[cellOffset + 2], sdat[cellOffset + 3]);
            }
        }

        byte[] maskTablesRaw = sdat[(blobOffset + GridByteSize)..(blobOffset + PackedBlobSize)];

        int recordsOffset = blobOffset + PackedBlobSize;
        int recordsByteLength = (int)recordCount * RecordSize;
        byte[] recordsRaw = sdat[recordsOffset..(recordsOffset + recordsByteLength)];

        int trailingOffset = recordsOffset + recordsByteLength;
        uint trailingField = ReadU32(sdat, trailingOffset);

        int tailOffset = trailingOffset + TrailingFieldSize;
        byte[] tailBlockRaw = sdat[tailOffset..(tailOffset + TailBlockSize)];

        return new SdatSector
        {
            SectorId = sectorId,
            Flags = flags,
            X = x,
            Y = y,
            UnknownHeaderField = unknownHeaderField,
            FormatFlag = formatFlag,
            EnvSettingsRaw = envSettingsRaw,
            HeaderPadding = headerPadding,
            Grid = grid,
            MaskTablesRaw = maskTablesRaw,
            RecordsRaw = recordsRaw,
            TrailingField = trailingField,
            TailBlockRaw = tailBlockRaw,
        };
    }

    public static byte[] Encode(SdatSector sector)
    {
        if (sector.Grid.GetLength(0) != GridSize || sector.Grid.GetLength(1) != GridSize)
        {
            throw new ArgumentException($"Grid must be {GridSize}x{GridSize}.", nameof(sector));
        }
        if (sector.EnvSettingsRaw.Length != EnvSettingsSize)
        {
            throw new ArgumentException($"EnvSettingsRaw must be {EnvSettingsSize} bytes.", nameof(sector));
        }
        if (sector.HeaderPadding.Length != HeaderPaddingSize)
        {
            throw new ArgumentException($"HeaderPadding must be {HeaderPaddingSize} bytes.", nameof(sector));
        }
        if (sector.MaskTablesRaw.Length != MaskTablesSize)
        {
            throw new ArgumentException($"MaskTablesRaw must be {MaskTablesSize} bytes.", nameof(sector));
        }
        if (sector.RecordsRaw.Length % RecordSize != 0)
        {
            throw new ArgumentException($"RecordsRaw length must be a multiple of {RecordSize}.", nameof(sector));
        }
        if (sector.TailBlockRaw.Length != TailBlockSize)
        {
            throw new ArgumentException($"TailBlockRaw must be {TailBlockSize} bytes.", nameof(sector));
        }

        uint recordCount = (uint)(sector.RecordsRaw.Length / RecordSize);
        uint ownDataSize = (uint)(MetadataSize + PackedBlobSize + sector.RecordsRaw.Length + TrailingFieldSize + TailBlockSize);
        uint totalSize = ChunkHeaderSize + ownDataSize;

        var result = new byte[totalSize];
        WriteU32(result, 0, Magic);
        WriteU32(result, 4, ExpectedVersion);
        WriteU32(result, 8, totalSize);
        WriteU32(result, 0xC, ownDataSize);
        WriteU32(result, 0x10, 0);

        const int metaOffset = ChunkHeaderSize;
        WriteU32(result, metaOffset + 0x00, sector.SectorId);
        WriteU32(result, metaOffset + 0x04, sector.Flags);
        WriteF32(result, metaOffset + 0x08, sector.X);
        WriteF32(result, metaOffset + 0x0C, sector.Y);
        WriteU32(result, metaOffset + 0x10, sector.UnknownHeaderField);
        WriteU32(result, metaOffset + 0x14, PackedBlobSize);
        WriteU32(result, metaOffset + 0x18, recordCount);
        WriteU32(result, metaOffset + 0x1C, sector.FormatFlag);
        sector.EnvSettingsRaw.CopyTo(result, metaOffset + 0x20);
        sector.HeaderPadding.CopyTo(result, metaOffset + 0x23A);

        int blobOffset = metaOffset + MetadataSize;
        for (int row = 0; row < GridSize; row++)
        {
            for (int col = 0; col < GridSize; col++)
            {
                int cellOffset = blobOffset + (row * GridSize + col) * GridCellSize;
                SdatGridCell cell = sector.Grid[row, col];
                result[cellOffset] = (byte)(cell.RawHeight & 0xFF);
                result[cellOffset + 1] = (byte)(cell.RawHeight >> 8);
                result[cellOffset + 2] = cell.RawByte2;
                result[cellOffset + 3] = cell.RawByte3;
            }
        }

        sector.MaskTablesRaw.CopyTo(result, blobOffset + GridByteSize);

        int recordsOffset = blobOffset + PackedBlobSize;
        sector.RecordsRaw.CopyTo(result, recordsOffset);

        int trailingOffset = recordsOffset + sector.RecordsRaw.Length;
        WriteU32(result, trailingOffset, sector.TrailingField);

        sector.TailBlockRaw.CopyTo(result, trailingOffset + TrailingFieldSize);

        return result;
    }

    private static uint ReadU32(byte[] data, int offset)
        => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static float ReadF32(byte[] data, int offset)
        => BitConverter.Int32BitsToSingle((int)ReadU32(data, offset));

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteF32(byte[] data, int offset, float value)
        => WriteU32(data, offset, (uint)BitConverter.SingleToInt32Bits(value));
}
