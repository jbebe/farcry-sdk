using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace JackAll.Core.Format;

/// <summary>One drawable triangle list: one (LOD, part, material) slice of an .xbg, sharing a single
/// vertex buffer with its sibling primitives on the same part.</summary>
public sealed class XbgSubmesh
{
    public required int LodLevel { get; init; }
    public required int PartNumber { get; init; }
    public required int MaterialIndex { get; init; }
    public required string MaterialName { get; init; }
    public required Vector3[] Positions { get; init; }
    public Vector3[]? Normals { get; init; }
    /// <summary>Triangle list, indices local to <see cref="Positions"/>.</summary>
    public required int[] Indices { get; init; }
}

/// <summary>
/// Reads the static mesh geometry out of a Far Cry 2 .xbg for preview purposes: vertex
/// positions/normals and triangle lists per (LOD, part, material), enough to render the model - not a
/// full round-trippable parse (no skeleton/skinning/UVs/materials-as-textures).
///
/// Ported from <c>tools/XBG-Importer/modules/Far_Cry_2/{binary_fc2,chunks_fc2,import_mesh_fc2,
/// import_xbg_fc2}.py</c> - see research/knowledge.md §8 for the format's provenance.
/// </summary>
public sealed class XbgModel
{
    public required IReadOnlyList<string> Materials { get; init; }
    public required IReadOnlyList<XbgSubmesh> Submeshes { get; init; }
    public required IReadOnlyList<int> LodLevels { get; init; }

    public static XbgModel Parse(byte[] data)
    {
        bool bigEndian = DetectEndian(data);
        var g = new Cursor(data, bigEndian);

        byte[] magic = g.ReadBytes(4);
        if (magic is not [(byte)'H', (byte)'S', (byte)'E', (byte)'M'])
        {
            throw new InvalidDataException(
                "Not a Far Cry 2 .xbg (no \"HSEM\" header) - this viewer doesn't support this file's format.");
        }

        g.SkipI32(6);
        int chunkCount = g.ReadI32();

        var materials = new List<string>();
        var meshes = new List<MeshEntry>();
        List<List<SubMeshHeader>>? subMeshList = null;
        float vertPosScale = 1f;

        for (int m = 0; m < chunkCount; m++)
        {
            int chunkStart = g.Position;
            string chunkName = g.ReadChunkName();
            int[] ci = g.ReadI32Array(2);
            int chunkSize = ci[1];
            if (chunkSize < 12 || chunkStart + chunkSize > data.Length)
            {
                break; // corrupt/truncated - stop rather than seek off the end
            }

            switch (chunkName)
            {
                case "PMCP":
                    g.SkipI32(2);
                    vertPosScale = g.ReadF32Array(2)[1];
                    break;
                case "DIKS":
                    g.SkipI32(2);
                    int lodCount = g.ReadI32();
                    g.SkipBytes(lodCount * 4);
                    break;
                case "LTMR":
                    int[] w = g.ReadI32Array(4);
                    int mc = w[2];
                    for (int mi = 0; mi < mc; mi++)
                    {
                        int nl = g.ReadI32();
                        string full = g.ReadWord(nl);
                        g.SkipBytes(1);
                        string shortName = full.Split('/')[^1].Replace(".mat", "");
                        materials.Add(shortName.Length > 0 ? shortName : $"Material_{mi}");
                    }
                    break;
                case "SDOL":
                    ParseSdolChunk(g, meshes);
                    break;
                case "DNKS":
                    subMeshList = TryParseDnks(g);
                    break;
                    // PMCU (UVs), EDON (skeleton), MB2O (bind matrices), XOBB/HPSB (bounds) aren't
                    // needed for a geometry-only preview.
            }

            g.Seek(chunkStart + chunkSize);
        }

        foreach (MeshEntry mesh in meshes)
        {
            ParseMeshVertices(g, mesh, vertPosScale);
        }

        ProcessMeshFaces(g, meshes, subMeshList, materials);

        var submeshes = new List<XbgSubmesh>();
        foreach (MeshEntry mesh in meshes)
        {
            if (mesh.Positions is null)
            {
                continue;
            }

            foreach ((int[] indices, int matId, string matName) in mesh.Primitives)
            {
                submeshes.Add(new XbgSubmesh
                {
                    LodLevel = mesh.LodLevel,
                    PartNumber = mesh.PartNumber,
                    MaterialIndex = matId,
                    MaterialName = matName,
                    Positions = mesh.Positions,
                    Normals = mesh.Normals,
                    Indices = indices,
                });
            }
        }

        List<int> lodLevels = submeshes.Select(s => s.LodLevel).Distinct().OrderBy(x => x).ToList();
        return new XbgModel { Materials = materials, Submeshes = submeshes, LodLevels = lodLevels };
    }

    /// <summary>Chunk count lives at byte offset 28 as a 32-bit int; a real file's is always small
    /// (&lt; 256). Whichever endianness yields a sane value wins - mirrors
    /// <c>binary_fc2.detect_endian_from_bytes</c>.</summary>
    private static bool DetectEndian(byte[] data)
    {
        if (data.Length < 32)
        {
            return false;
        }

        int le = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(28));
        int be = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(28));
        bool leOk = le is > 0 and < 256;
        bool beOk = be is > 0 and < 256;
        return beOk && !leOk;
    }

    // ============================================================
    // SDOL - vertex buffer layout + per-LOD/part/material index-range table
    // ============================================================

    private sealed class MeshEntry
    {
        public int LodLevel;
        public int PartNumber;
        public int VbIndex;
        public int IndiceSectionOffset;
        public int NameIndex;
        public int VertFormatFlags;
        public int VertStride;
        public int VertSectionOffset;
        public int VertCount;
        public readonly List<(int VbIdx, int LodGrp, int SubIdx, int IdxOffset, int IdxCount)> MatListInfo = new();
        public Vector3[]? Positions;
        public Vector3[]? Normals;
        public readonly List<(int[] Indices, int MaterialId, string MaterialName)> Primitives = new();
    }

    private static void ParseSdolChunk(Cursor g, List<MeshEntry> meshes)
    {
        g.SkipI32(2);
        int lodCount = g.ReadI32();
        if (lodCount == 0)
        {
            return;
        }

        var meshDict = new Dictionary<(int Lod, int SmIdx), MeshEntry>();

        for (int currentLod = 0; currentLod < lodCount; currentLod++)
        {
            g.SkipI32(1); // lod switch distance
            int vbCount = g.ReadI32();
            var vbInfo = new List<(int Flags, int Stride, int Offset)>();
            for (int vb = 0; vb < vbCount; vb++)
            {
                int flags = g.ReadI32();
                int stride = g.ReadI32();
                g.SkipI32(1); // unknown
                int offset = g.ReadI32();
                vbInfo.Add((flags, stride, offset));
            }

            int submeshCount = g.ReadI32();
            var submeshInfo = new List<(int VbIdx, int LodGrp, int SubIdx, int IdxOffset)>();
            for (int sm = 0; sm < submeshCount; sm++)
            {
                int vbIdx = g.ReadI32();
                int lodGrp = g.ReadI32();
                int subIdx = g.ReadI32();
                int idxOffset = g.ReadI32();
                g.SkipI32(3); // vert_marker, unk1, unk2
                submeshInfo.Add((vbIdx, lodGrp, subIdx, idxOffset));
            }

            var submeshData = new List<(int VbIdx, int LodGrp, int SubIdx, int IdxOffset, int IdxCount)>();
            for (int i = 0; i < submeshInfo.Count; i++)
            {
                (int vbIdx, int lodGrp, int subIdx, int idxOffset) = submeshInfo[i];
                int idxCount = i + 1 < submeshInfo.Count ? submeshInfo[i + 1].IdxOffset - idxOffset : -1;
                submeshData.Add((vbIdx, lodGrp, subIdx, idxOffset, idxCount));
            }

            uint vertSectionSize = g.ReadU32();
            g.SeekPad(16);
            int vertSectionBase = g.Position;
            g.Seek(vertSectionBase + (int)vertSectionSize);

            uint indiceSectionSize = g.ReadU32();
            g.SeekPad(16);
            int indiceSectionOffset = g.Position;
            g.Seek(indiceSectionOffset + (int)(indiceSectionSize * 2));

            if (submeshData.Count > 0 && submeshData[^1].IdxCount == -1)
            {
                var last = submeshData[^1];
                last.IdxCount = (int)indiceSectionSize - last.IdxOffset;
                submeshData[^1] = last;
            }

            for (int smIdx = 0; smIdx < submeshData.Count; smIdx++)
            {
                (int vbIdx, int lodGrp, int subIdx, int idxOffset, int idxCount) = submeshData[smIdx];
                var mesh = new MeshEntry
                {
                    LodLevel = currentLod,
                    PartNumber = subIdx,
                    VbIndex = vbIdx,
                    IndiceSectionOffset = indiceSectionOffset,
                    NameIndex = smIdx,
                };
                if (vbIdx < vbInfo.Count)
                {
                    (int flags, int stride, int offset) = vbInfo[vbIdx];
                    mesh.VertFormatFlags = flags;
                    mesh.VertStride = stride;
                    mesh.VertSectionOffset = vertSectionBase + offset;
                    mesh.VertCount = stride > 0
                        ? (vbIdx + 1 < vbInfo.Count ? vbInfo[vbIdx + 1].Offset - offset : (int)vertSectionSize - offset) / stride
                        : 0;
                }

                mesh.MatListInfo.Add((vbIdx, lodGrp, subIdx, idxOffset, idxCount));
                meshDict[(currentLod, smIdx)] = mesh;
            }
        }

        meshes.AddRange(meshDict.Values);
    }

    // ============================================================
    // Vertex buffer decode - VertexFlags bitmask (see import_mesh_fc2.VertexFlags)
    // ============================================================

    private const int PosFloat = 0x0001, PosInt16 = 0x0002, PosHalf = 0x0004, Uv0 = 0x0008,
        BoneWts1 = 0x0010, Normal = 0x0040;

    /// <summary>Component order fixed by the format: Position -> UV0 -> UV1 -> UV2 -> BoneWts1 ->
    /// BoneWts2 -> Normal -> Color -> Tangent -> Binormal -> Unk400. Only the offsets this preview
    /// actually consumes (position, normal) are tracked; everything else just contributes to stride.</summary>
    private static (int Stride, int PosOffset, int? NormalOffset) ComputeLayout(int flags)
    {
        int stride = 0;
        int posOffset = 0, normalOffset = -1;
        bool posHandled = false;

        void Take(int flag, int size, bool isPos, bool isNormal)
        {
            if (isPos)
            {
                if (posHandled || (flags & flag) == 0)
                {
                    return;
                }

                posHandled = true;
                posOffset = stride;
            }
            else
            {
                if ((flags & flag) == 0)
                {
                    return;
                }

                if (isNormal)
                {
                    normalOffset = stride;
                }
            }

            stride += size;
        }

        Take(PosFloat, 12, isPos: true, isNormal: false);
        Take(PosInt16, 8, isPos: true, isNormal: false);
        Take(PosHalf, 8, isPos: true, isNormal: false);
        Take(Uv0, 4, isPos: false, isNormal: false);
        Take(0x0800, 4, isPos: false, isNormal: false); // UV1
        Take(0x1000, 4, isPos: false, isNormal: false); // UV2
        Take(BoneWts1, 8, isPos: false, isNormal: false);
        Take(0x0020, 8, isPos: false, isNormal: false); // BoneWts2
        Take(Normal, 4, isPos: false, isNormal: true);
        Take(0x0080, 4, isPos: false, isNormal: false); // Color
        Take(0x0100, 4, isPos: false, isNormal: false); // Tangent
        Take(0x0200, 4, isPos: false, isNormal: false); // Binormal
        Take(0x0400, 4, isPos: false, isNormal: false); // Unk400

        return (stride, posOffset, normalOffset >= 0 ? normalOffset : null);
    }

    private static void ParseMeshVertices(Cursor g, MeshEntry mesh, float vertPosScale)
    {
        int count = mesh.VertCount;
        int stride = mesh.VertStride;
        if (count <= 0 || stride <= 0 || mesh.VertSectionOffset + (long)count * stride > g.Length)
        {
            mesh.Positions = [];
            return;
        }

        bool hasPosFloat = (mesh.VertFormatFlags & PosFloat) != 0;
        bool hasNormal = (mesh.VertFormatFlags & Normal) != 0;
        (_, int posOffset, int? normalOffset) = ComputeLayout(mesh.VertFormatFlags);

        g.Seek(mesh.VertSectionOffset);
        byte[] buf = g.ReadBytes(count * stride);
        bool be = g.BigEndian;

        var positions = new Vector3[count];
        Vector3[]? normals = hasNormal ? new Vector3[count] : null;

        for (int v = 0; v < count; v++)
        {
            int b = v * stride + posOffset;
            float x, y, z;
            if (hasPosFloat)
            {
                x = ReadF32(buf, b, be);
                y = ReadF32(buf, b + 4, be);
                z = ReadF32(buf, b + 8, be);
            }
            else
            {
                x = ReadI16(buf, b, be);
                y = ReadI16(buf, b + 2, be);
                z = ReadI16(buf, b + 4, be);
            }

            positions[v] = new Vector3(x * vertPosScale, y * vertPosScale, z * vertPosScale);

            if (normals is not null && normalOffset is int no)
            {
                int nb = v * stride + no;
                // D3DCOLOR-encoded: unsigned-normalised bytes, BGRA order (xyz = byte2,byte1,byte0).
                normals[v] = new Vector3(Unsign(buf[nb + 2]), Unsign(buf[nb + 1]), Unsign(buf[nb]));
            }
        }

        mesh.Positions = positions;
        mesh.Normals = normals;
    }

    private static float Unsign(byte b) => b / 255f * 2f - 1f;

    // ============================================================
    // DNKS - per-submesh material id + face count (deterministic layout only; see
    // chunks_fc2.parse_dnks_for_palette / import_mesh_fc2.parse_dnks_chunk for the full model
    // including the legacy heuristic fallback this preview doesn't need)
    // ============================================================

    private sealed class SubMeshHeader
    {
        public required ushort[] Header; // [0]=material id, [1]=face count
        public int FaceCount => Header.Length > 1 ? Header[1] : 0;
    }

    private static List<List<SubMeshHeader>>? TryParseDnks(Cursor g)
    {
        int start = g.Position;
        try
        {
            int[] pp = g.ReadI32Array(2);
            g.SkipBytes(4); // 'SULC' sub-tag
            int[] qq = g.ReadI32Array(4);
            int trailSize = pp[0];
            int blocksBytes = qq[2];
            if (blocksBytes <= 0 || blocksBytes > (1 << 28) || trailSize < 4)
            {
                throw new InvalidDataException("implausible DNKS preamble");
            }

            var subMeshList = new List<List<SubMeshHeader>>();
            int consumed = 0;
            while (consumed < blocksBytes)
            {
                int cnt = g.ReadI32();
                if (cnt is < 0 or > 100_000)
                {
                    throw new InvalidDataException("bad DNKS block count");
                }

                consumed += 4;
                var block = new List<SubMeshHeader>(cnt);
                for (int i = 0; i < cnt; i++)
                {
                    ushort[] header = g.ReadU16Array(7);
                    g.SkipBytes(96); // 48 x int16 bone palette - not needed for a geometry preview
                    block.Add(new SubMeshHeader { Header = header });
                }

                consumed += cnt * 110;
                subMeshList.Add(block);
            }

            if (consumed != blocksBytes)
            {
                throw new InvalidDataException("DNKS block region overrun");
            }

            int blockCount = (int)g.ReadU32();
            if (blockCount != subMeshList.Count)
            {
                throw new InvalidDataException("DNKS name count mismatch");
            }

            for (int k = 0; k < blockCount; k++)
            {
                g.SkipBytes(52); // metric/bbox/lod meta
                uint nameLen = g.ReadU32();
                if (nameLen is < 1 or > 256)
                {
                    throw new InvalidDataException("bad DNKS name length");
                }

                g.SkipBytes((int)nameLen);
                g.SkipBytes(1); // NUL terminator
            }

            return subMeshList;
        }
        catch (Exception)
        {
            g.Seek(start);
            return null;
        }
    }

    private static int? ResolveDnksPos(int lodGrp, int subIdx, int nameIndex, List<List<SubMeshHeader>>? subMeshList)
    {
        if (subMeshList is null || lodGrp < 0 || lodGrp >= subMeshList.Count)
        {
            return null;
        }

        int n = subMeshList[lodGrp].Count;
        if (subIdx >= 0 && subIdx < n)
        {
            return subIdx;
        }

        return nameIndex >= 0 && nameIndex < n ? nameIndex : null;
    }

    private static void ProcessMeshFaces(
        Cursor g, List<MeshEntry> meshes, List<List<SubMeshHeader>>? subMeshList, List<string> materials)
    {
        foreach (MeshEntry mesh in meshes)
        {
            foreach ((int _, int lodGrp, int subIdxVal, int idxOffset, int idxCount) in mesh.MatListInfo)
            {
                int? dnksPos = ResolveDnksPos(lodGrp, subIdxVal, mesh.NameIndex, subMeshList);
                if (dnksPos is null)
                {
                    continue;
                }

                SubMeshHeader sm = subMeshList![lodGrp][dnksPos.Value];
                int matId = sm.Header[0];
                string matName = matId < materials.Count ? materials[matId] : $"Material_{matId}";
                int faceCount = sm.FaceCount;
                if (faceCount <= 0)
                {
                    continue;
                }

                int byteOffset = mesh.IndiceSectionOffset + idxOffset * 2;
                int rawCount = faceCount * 3;
                if (byteOffset < 0 || byteOffset + (long)rawCount * 2 > g.Length)
                {
                    continue;
                }

                g.Seek(byteOffset);
                byte[] rawBuf = g.ReadBytes(rawCount * 2);
                bool be = g.BigEndian;

                var idx = new List<int>(rawCount);
                for (int i = 0; i < rawCount; i += 3)
                {
                    ushort a = ReadU16(rawBuf, i * 2, be);
                    ushort b = ReadU16(rawBuf, (i + 1) * 2, be);
                    ushort c = ReadU16(rawBuf, (i + 2) * 2, be);
                    if (a != 65535 && b != 65535 && c != 65535)
                    {
                        idx.Add(a);
                        idx.Add(b);
                        idx.Add(c);
                    }
                }

                if (idx.Count > 0)
                {
                    mesh.Primitives.Add((idx.ToArray(), matId, matName));
                }
            }
        }
    }

    private static float ReadF32(byte[] buf, int offset, bool be) =>
        be ? BinaryPrimitives.ReadSingleBigEndian(buf.AsSpan(offset)) : BinaryPrimitives.ReadSingleLittleEndian(buf.AsSpan(offset));

    private static short ReadI16(byte[] buf, int offset, bool be) =>
        be ? BinaryPrimitives.ReadInt16BigEndian(buf.AsSpan(offset)) : BinaryPrimitives.ReadInt16LittleEndian(buf.AsSpan(offset));

    private static ushort ReadU16(byte[] buf, int offset, bool be) =>
        be ? BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(offset)) : BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(offset));

    /// <summary>Forward-only byte cursor, endian-aware per <see cref="DetectEndian"/> (PC Far Cry 2 is
    /// little-endian, the PS3 release big-endian - see binary_fc2.py's endianness note).</summary>
    private sealed class Cursor(byte[] data, bool bigEndian)
    {
        public int Position { get; private set; }
        public bool BigEndian => bigEndian;
        public int Length => data.Length;

        public void Seek(int pos) => Position = pos;
        public void SkipBytes(int n) => Position += n;
        public void SkipI32(int n) => Position += n * 4;

        public void SeekPad(int pad)
        {
            int rem = (pad - Position % pad) % pad;
            Position += rem;
        }

        public byte[] ReadBytes(int n)
        {
            EnsureAvailable(n);
            byte[] slice = data[Position..(Position + n)];
            Position += n;
            return slice;
        }

        public int ReadI32()
        {
            EnsureAvailable(4);
            int v = bigEndian
                ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(Position))
                : BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(Position));
            Position += 4;
            return v;
        }

        public uint ReadU32()
        {
            EnsureAvailable(4);
            uint v = bigEndian
                ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(Position))
                : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Position));
            Position += 4;
            return v;
        }

        public int[] ReadI32Array(int n)
        {
            var arr = new int[n];
            for (int i = 0; i < n; i++)
            {
                arr[i] = ReadI32();
            }

            return arr;
        }

        public float[] ReadF32Array(int n)
        {
            var arr = new float[n];
            for (int i = 0; i < n; i++)
            {
                EnsureAvailable(4);
                arr[i] = bigEndian
                    ? BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(Position))
                    : BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(Position));
                Position += 4;
            }

            return arr;
        }

        public ushort[] ReadU16Array(int n)
        {
            var arr = new ushort[n];
            for (int i = 0; i < n; i++)
            {
                EnsureAvailable(2);
                arr[i] = bigEndian
                    ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(Position))
                    : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(Position));
                Position += 2;
            }

            return arr;
        }

        public string ReadWord(int length)
        {
            byte[] raw = ReadBytes(length);
            int nul = Array.IndexOf(raw, (byte)0);
            return Encoding.UTF8.GetString(raw, 0, nul >= 0 ? nul : raw.Length);
        }

        /// <summary>4-byte chunk magic; reversed on a big-endian (PS3) file so callers can always
        /// switch on the canonical PC name ("SDOL", "EDON", ...).</summary>
        public string ReadChunkName()
        {
            byte[] raw = ReadBytes(4);
            if (bigEndian)
            {
                Array.Reverse(raw);
            }

            int nul = Array.IndexOf(raw, (byte)0);
            return Encoding.ASCII.GetString(raw, 0, nul >= 0 ? nul : raw.Length);
        }

        private void EnsureAvailable(int count)
        {
            if (Position < 0 || (long)Position + count > data.Length)
            {
                throw new InvalidDataException(
                    $"Ran out of bytes at offset 0x{Position:X} (needed {count}, only " +
                    $"{Math.Max(0, data.Length - Position)} left).");
            }
        }
    }
}
