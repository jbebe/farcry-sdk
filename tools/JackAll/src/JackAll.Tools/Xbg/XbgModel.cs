using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace JackAll.Tools.Xbg;

/// <summary>One drawable triangle list: one (LOD, part, material) slice of an .xbg, sharing a single
/// vertex buffer with its sibling primitives on the same part.</summary>
public sealed class XbgSubmesh
{
    public required int LodLevel { get; init; }
    public required int MaterialIndex { get; init; }
    public required string MaterialName { get; init; }

    /// <summary>The named part this belongs to - a wheel, a bumper, a wall - without its LOD
    /// suffix. Empty when the file names no part for it.</summary>
    public string PartName { get; init; } = "";

    /// <summary>What moves this part's vertices into model space: the world matrix of the bone they
    /// are stored relative to - the part's own bone when rigid, the skeleton root when skinned. Null
    /// when that bone is the identity and the vertices already sit in model space.</summary>
    public Matrix4x4? PartTransform { get; init; }

    public required Vector3[] Positions { get; init; }
    public Vector3[]? Normals { get; init; }

    /// <summary>A position from <see cref="Positions"/> moved out of pivot space into the model's.
    /// Positions stay unplaced because parts sharing a vertex buffer need different placements, so
    /// every consumer of the geometry goes through this.</summary>
    public Vector3 Place(Vector3 position)
        => PartTransform is { } m ? Vector3.Transform(position, m) : position;

    /// <summary>The placement is rigid, so a normal only needs its rotation.</summary>
    public Vector3 PlaceNormal(Vector3 normal)
        => PartTransform is { } m ? Vector3.TransformNormal(normal, m) : normal;
    /// <summary>UV channel 0 in the game's D3D space (V=0 is the top row); null when the file has
    /// none.</summary>
    public Vector2[]? Uvs { get; init; }

    /// <summary>The per-vertex colour the engine calls the vertex mask: its blue channel blends the
    /// material's two diffuse tints, red the speculars, alpha is occlusion. Null when the file
    /// carries none, which the engine treats as white.</summary>
    public Vector4[]? Colours { get; init; }
    /// <summary>Triangle list, indices local to <see cref="Positions"/>.</summary>
    public required int[] Indices { get; init; }
}

/// <summary>
/// Reads the static mesh geometry out of a Far Cry 2 .xbg for preview purposes: vertex
/// positions/normals/UVs and triangle lists per (LOD, part, material), enough to render the model -
/// not a full round-trippable parse (no skeleton/skinning; materials stay names).
///
/// Ported from <c>tools/XBG-Importer/modules/Far_Cry_2/{binary_fc2,chunks_fc2,import_mesh_fc2,
/// import_xbg_fc2}.py</c> - see research/knowledge.md §8 for the format's provenance.
/// </summary>
public sealed partial class XbgModel
{
    [GeneratedRegex(@"_LOD\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex LodSuffixRegex();

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
        var partNames = new List<string>();
        var bones = new List<Bone>();
        float vertPosScale = 1f;
        float uvTrans = 0f, uvScale = 1f;

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
                case "PMCU":
                    g.SkipI32(2);
                    float[] uvTransScale = g.ReadF32Array(2);
                    uvTrans = uvTransScale[0];
                    uvScale = uvTransScale[1];
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
                    subMeshList = TryParseDnks(g, partNames);
                    break;
                case "EDON":
                    bones = ParseEdon(g);
                    break;
                    // MB2O (bind matrices) and XOBB/HPSB (bounds) aren't needed for a geometry-only
                    // preview.
            }

            g.Seek(chunkStart + chunkSize);
        }

        // Submeshes routinely reference the same vertex region (one buffer, many materials);
        // decode each region once and share the arrays, so consumers can dedupe by reference.
        var decodedRegions = new Dictionary<(int Offset, int Count, int Stride, int Flags), MeshEntry>();
        foreach (MeshEntry mesh in meshes)
        {
            (int, int, int, int) region = (mesh.VertSectionOffset, mesh.VertCount, mesh.VertStride, mesh.VertFormatFlags);
            if (decodedRegions.TryGetValue(region, out MeshEntry? first))
            {
                mesh.Positions = first.Positions;
                mesh.Normals = first.Normals;
                mesh.Uvs = first.Uvs;
                mesh.Colours = first.Colours;
                continue;
            }

            ParseMeshVertices(g, mesh, vertPosScale, uvTrans, uvScale);
            decodedRegions[region] = mesh;
        }

        AssignParts(meshes, partNames, bones);
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
                    PartName = mesh.PartName,
                    PartTransform = mesh.PartTransform,
                    MaterialIndex = matId,
                    MaterialName = matName,
                    Positions = mesh.Positions,
                    Normals = mesh.Normals,
                    Uvs = mesh.Uvs,
                    Colours = mesh.Colours,
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
    // EDON - the bone hierarchy that places the named rigid parts
    // ============================================================

    private sealed class Bone
    {
        public required string Name;
        public required int Parent;
        public required Matrix4x4 Local;
        public Matrix4x4? World;
    }

    private static List<Bone> ParseEdon(Cursor g)
    {
        var bones = new List<Bone>();
        try
        {
            g.SkipI32(2);
            int count = g.ReadI32();
            if (count is < 0 or > 100_000)
            {
                return [];
            }

            for (int i = 0; i < count; i++)
            {
                g.SkipBytes(4);
                g.SkipI32(2);
                int parent = g.ReadI32();
                float[] pose = g.ReadF32Array(7); // quaternion xyzw then translation xyz
                g.SkipBytes(24); // unused floats/ints between the pose and the name
                int nameLen = g.ReadI32();
                if (nameLen is < 0 or > 256)
                {
                    return [];
                }

                string name = g.ReadWord(nameLen);
                g.SkipBytes(1);
                bones.Add(new Bone
                {
                    Name = name,
                    Parent = parent,
                    Local = Matrix4x4.CreateFromQuaternion(new Quaternion(pose[0], pose[1], pose[2], pose[3]))
                        * Matrix4x4.CreateTranslation(pose[4], pose[5], pose[6]),
                });
            }
        }
        catch (Exception)
        {
            return [];
        }

        return bones;
    }

    private static Matrix4x4 WorldOf(List<Bone> bones, int index, int depth = 0)
    {
        if (bones[index].World is { } cached)
        {
            return cached;
        }

        Bone bone = bones[index];
        Matrix4x4 world = bone.Parent >= 0 && bone.Parent < bones.Count && bone.Parent != index && depth < 64
            ? bone.Local * WorldOf(bones, bone.Parent, depth + 1)
            : bone.Local;
        bone.World = world;
        return world;
    }

    /// <summary>Geometry is stored relative to one bone, and needs that bone's world matrix to reach
    /// model space: a rigid part is modelled around its own pivot and named after its bone, while a
    /// skinned part sits in the skeleton root's bind space - which for a character puts the root at
    /// the waist, so leaving it alone sinks them to their hips.</summary>
    private static void AssignParts(List<MeshEntry> meshes, List<string> partNames, List<Bone> bones)
    {
        if (bones.Count == 0)
        {
            return;
        }

        var boneByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < bones.Count; i++)
        {
            boneByName.TryAdd(bones[i].Name, i);
        }

        int root = bones.FindIndex(b => b.Parent < 0 || b.Parent >= bones.Count);

        foreach (MeshEntry mesh in meshes)
        {
            int block = mesh.MatListInfo.Count > 0 ? mesh.MatListInfo[0].LodGrp : -1;
            if (block >= 0 && block < partNames.Count)
            {
                mesh.PartName = LodSuffixRegex().Replace(partNames[block], "");
            }

            int bone = (mesh.VertFormatFlags & BoneWts1) != 0
                ? root
                : boneByName.GetValueOrDefault(mesh.PartName, -1);
            if (bone < 0)
            {
                continue;
            }

            Matrix4x4 world = WorldOf(bones, bone);
            if (!world.IsIdentity)
            {
                mesh.PartTransform = world;
            }
        }
    }

    // ============================================================
    // SDOL - vertex buffer layout + per-LOD/part/material index-range table
    // ============================================================

    private sealed class MeshEntry
    {
        public int LodLevel;
        public int IndiceSectionOffset;
        public int NameIndex;
        public int VertFormatFlags;
        public int VertStride;
        public int VertSectionOffset;
        public int VertCount;
        public string PartName = "";
        public Matrix4x4? PartTransform;
        public readonly List<(int LodGrp, int SubIdx, int IdxOffset)> MatListInfo = new();
        public Vector3[]? Positions;
        public Vector3[]? Normals;
        public Vector2[]? Uvs;
        public Vector4[]? Colours;
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

            uint vertSectionSize = g.ReadU32();
            g.SeekPad(16);
            int vertSectionBase = g.Position;
            g.Seek(vertSectionBase + (int)vertSectionSize);

            uint indiceSectionSize = g.ReadU32();
            g.SeekPad(16);
            int indiceSectionOffset = g.Position;
            g.Seek(indiceSectionOffset + (int)(indiceSectionSize * 2));

            for (int smIdx = 0; smIdx < submeshInfo.Count; smIdx++)
            {
                (int vbIdx, int lodGrp, int subIdx, int idxOffset) = submeshInfo[smIdx];
                var mesh = new MeshEntry
                {
                    LodLevel = currentLod,
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

                mesh.MatListInfo.Add((lodGrp, subIdx, idxOffset));
                meshDict[(currentLod, smIdx)] = mesh;
            }
        }

        meshes.AddRange(meshDict.Values);
    }

    // ============================================================
    // Vertex buffer decode - VertexFlags bitmask (see import_mesh_fc2.VertexFlags)
    // ============================================================

    private const int PosFloat = 0x0001, PosInt16 = 0x0002, PosHalf = 0x0004, Uv0 = 0x0008,
        BoneWts1 = 0x0010, BoneWts2 = 0x0020, Normal = 0x0040, Color = 0x0080, Tangent = 0x0100,
        Binormal = 0x0200, Unk400 = 0x0400, Uv1 = 0x0800, Uv2 = 0x1000;

    /// <summary>Component order fixed by the format: Position -> UV0 -> UV1 -> UV2 -> BoneWts1 ->
    /// BoneWts2 -> Normal -> Color -> Tangent -> Binormal -> Unk400. Only the offsets this preview
    /// actually consumes (position, UV0, normal) are tracked; everything else just contributes to
    /// stride.</summary>
    private static (int Stride, int PosOffset, int? Uv0Offset, int? NormalOffset, int? ColourOffset)
        ComputeLayout(int flags)
    {
        int stride = 0;
        int posOffset = 0, uv0Offset = -1, normalOffset = -1, colourOffset = -1;
        bool posHandled = false;

        void Take(int flag, int size, bool isPos = false, bool isUv0 = false, bool isNormal = false,
            bool isColour = false)
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

                if (isUv0)
                {
                    uv0Offset = stride;
                }
                if (isNormal)
                {
                    normalOffset = stride;
                }
                if (isColour)
                {
                    colourOffset = stride;
                }
            }

            stride += size;
        }

        Take(PosFloat, 12, isPos: true);
        Take(PosInt16, 8, isPos: true);
        Take(PosHalf, 8, isPos: true);
        Take(Uv0, 4, isUv0: true);
        Take(Uv1, 4);
        Take(Uv2, 4);
        Take(BoneWts1, 8);
        Take(BoneWts2, 8);
        Take(Normal, 4, isNormal: true);
        Take(Color, 4, isColour: true);
        Take(Tangent, 4);
        Take(Binormal, 4);
        Take(Unk400, 4);

        return (stride, posOffset, uv0Offset >= 0 ? uv0Offset : null,
            normalOffset >= 0 ? normalOffset : null, colourOffset >= 0 ? colourOffset : null);
    }

    private static void ParseMeshVertices(Cursor g, MeshEntry mesh, float vertPosScale, float uvTrans, float uvScale)
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
        (_, int posOffset, int? uv0Offset, int? normalOffset, int? colourOffset) =
            ComputeLayout(mesh.VertFormatFlags);

        g.Seek(mesh.VertSectionOffset);
        byte[] buf = g.ReadBytes(count * stride);
        bool be = g.BigEndian;

        var positions = new Vector3[count];
        Vector3[]? normals = hasNormal ? new Vector3[count] : null;
        Vector2[]? uvs = uv0Offset is not null ? new Vector2[count] : null;
        Vector4[]? colours = colourOffset is not null ? new Vector4[count] : null;

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

            if (uvs is not null && uv0Offset is int uo)
            {
                int ub = v * stride + uo;
                // 2x int16 through PMCU's translate+scale. Left in the game's D3D space, where V=0
                // is the texture's top row - the same row a .dds hands GL first.
                uvs[v] = new Vector2(
                    uvTrans + ReadI16(buf, ub, be) * uvScale,
                    uvTrans + ReadI16(buf, ub + 2, be) * uvScale);
            }

            if (normals is not null && normalOffset is int no)
            {
                int nb = v * stride + no;
                // D3DCOLOR-encoded: unsigned-normalised bytes, BGRA order (xyz = byte2,byte1,byte0).
                normals[v] = new Vector3(Unsign(buf[nb + 2]), Unsign(buf[nb + 1]), Unsign(buf[nb]));
            }

            if (colours is not null && colourOffset is int co)
            {
                int cb = v * stride + co;
                // BGRA bytes, straight 0..1 rather than the normals' signed remap.
                colours[v] = new Vector4(
                    buf[cb + 2] / 255f, buf[cb + 1] / 255f, buf[cb] / 255f, buf[cb + 3] / 255f);
            }
        }

        mesh.Positions = positions;
        mesh.Normals = normals;
        mesh.Uvs = uvs;
        mesh.Colours = colours;
    }

    private static float Unsign(byte b) => b / 255f * 2f - 1f;

    /// <summary>Axis-aligned extent over the vertices the given submeshes actually draw, placed;
    /// (0,0)..(0,0) when empty. Sibling parts share a vertex buffer and place it differently, so
    /// this walks each submesh's own triangles rather than the whole buffer.</summary>
    public static (Vector3 Min, Vector3 Max) Bounds(IEnumerable<XbgSubmesh> submeshes)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (XbgSubmesh submesh in submeshes)
        {
            foreach (int i in submesh.Indices)
            {
                Vector3 placed = submesh.Place(submesh.Positions[i]);
                min = Vector3.Min(min, placed);
                max = Vector3.Max(max, placed);
            }
        }

        return min.X > max.X ? (Vector3.Zero, Vector3.Zero) : (min, max);
    }

    /// <summary>Fallback for files with no NORMAL vertex component: accumulate each triangle's face
    /// normal into its three vertices and normalise, so shading still reads as a solid rather than
    /// flat per-face facets.</summary>
    public static Vector3[] ComputeSmoothNormals(Vector3[] positions, int[] indices)
    {
        var normals = new Vector3[positions.Length];
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            Vector3 faceNormal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += faceNormal;
            normals[b] += faceNormal;
            normals[c] += faceNormal;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i] == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(normals[i]);
        }

        return normals;
    }

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

    private static List<List<SubMeshHeader>>? TryParseDnks(Cursor g, List<string> partNames)
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

                partNames.Add(g.ReadWord((int)nameLen));
                g.SkipBytes(1); // NUL terminator
            }

            return subMeshList;
        }
        catch (Exception)
        {
            g.Seek(start);
            partNames.Clear();
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
            // The submesh header's own FaceCount drives the read below - the SDOL index range is
            // never consulted.
            foreach ((int lodGrp, int subIdxVal, int idxOffset) in mesh.MatListInfo)
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
