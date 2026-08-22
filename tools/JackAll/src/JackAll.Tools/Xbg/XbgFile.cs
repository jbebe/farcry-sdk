using JackAll.Core.Format;

namespace JackAll.Tools.Xbg;

/// <summary>An EDON node: a skinning bone, or the pivot a rigid part is modelled on.</summary>
public sealed class XbgNode
{
    public required string Name { get; set; }

    public required uint NameHash { get; set; }

    public required uint FirstChild { get; set; }

    public required uint NextSibling { get; set; }

    public required uint Parent { get; set; }

    public required float[] Rotation { get; init; }

    public required float[] Translation { get; init; }

    public required float[] Scale { get; init; }

    public required int SkinIndex { get; set; }

    public required float Weight { get; init; }

    public required float Extent { get; init; }
}

/// <summary>One LOD's vertex block: its format, its stride, and how many vertices it holds.</summary>
public sealed class XbgVertexBuffer
{
    public required uint Flags { get; init; }

    public required uint Stride { get; init; }

    public required uint VertexCount { get; set; }

    public required uint Offset { get; set; }
}

/// <summary>
/// Where one cluster's triangles live: which buffer, and where in the indices.
/// </summary>
/// <remarks>
/// <see cref="Part"/> indexes <see cref="XbgFile.Parts"/> and <see cref="Cluster"/> the cluster
/// inside it, which is how a draw call is paired with its material and bone palette. The three
/// trailing words are the cluster's last vertex index and its byte offset into the LOD's whole
/// vertex block, both derived from the layout.
/// </remarks>
public sealed class XbgSubmeshRef
{
    public required uint Buffer { get; init; }

    public required uint Part { get; init; }

    public required uint Cluster { get; init; }

    public required uint IndexOffset { get; set; }

    public required uint[] Trailing { get; set; }
}

public sealed class XbgLod
{
    public required float Distance { get; init; }

    public required List<XbgVertexBuffer> VertexBuffers { get; init; }

    public required List<XbgSubmeshRef> Submeshes { get; init; }

    public required byte[] VertexData { get; set; }

    public required byte[] IndexData { get; set; }
}

/// <summary>
/// One drawable block: material slot, counts, and its 48-slot bone palette.
/// </summary>
/// <remarks>
/// The face count is stored twice and the index count is three times it, so all three are derived
/// on write - editing one of them cannot desync the rest.
/// </remarks>
public sealed class XbgCluster
{
    public required ushort MaterialIndex { get; init; }

    public required ushort FaceCount { get; set; }

    public required ushort Stride { get; set; }

    public required ushort VertexCount { get; set; }

    public required ushort Flags { get; init; }

    public required short[] Palette { get; init; }

    public bool IsSkinned => (Flags & XbgFile.BoneWeights1) != 0;
}

/// <summary>
/// A named (part, damage state, LOD) group and the clusters drawing it.
/// </summary>
/// <remarks>
/// <see cref="Bounds"/> is a bounding sphere then an axis-aligned box: centre, radius, min, max.
/// The box matches the part's own vertices in every shipped part; the sphere is fitted tighter than
/// the box allows, so refitting cannot reproduce it. <see cref="Lod"/> matches the name's
/// <c>_LOD&lt;n&gt;</c> suffix in all of them.
/// </remarks>
public sealed class XbgPart
{
    public required string Name { get; set; }

    public required float LodMetric { get; init; }

    public required float[] Bounds { get; set; }

    public required int Lod { get; set; }

    public required uint Reserved { get; set; }

    public List<XbgCluster> Clusters { get; } = [];
}

/// <summary>
/// A DIKS entry: a part, by name hash, and the node that places it.
/// </summary>
/// <remarks>
/// The entry's own position in the table is stored alongside and regenerated on write, so only
/// these two fields carry information.
/// </remarks>
public sealed class XbgPartRef
{
    public required uint NameHash { get; set; }

    public required uint Node { get; init; }
}

/// <summary>Chunk identity, the header word nothing interprets, and any opaque body.</summary>
public sealed class XbgChunk
{
    public required string Tag { get; init; }

    public required uint Word0 { get; set; }

    public byte[] Raw { get; set; } = [];
}

/// <summary>
/// Reader and writer for `.xbg`, the Dunia mesh container - and for `.xbm`, which is the same
/// container carrying a material instead of geometry.
/// </summary>
/// <remarks>
/// Layout is documented in docs/docs/file-formats/xbm-xbg.md. Tags are stored reversed, so EDON in
/// the file is NODE to the engine.
/// <para>
/// Two things every parser written from the community material gets wrong. A chunk header is
/// <b>20 bytes and its payload is addressed backwards</b>, at
/// <c>chunkStart + chunkSize - payloadSize</c>; DNKS is the only chunk with a sub-chunk, which is
/// why its own payload sits at the end rather than at a fixed offset. And alignment padding is a
/// <b>descending byte counter</b>, not zeros - a zero-filled file still loads but no longer matches
/// byte for byte.
/// </para>
/// <para>
/// This supersedes <see cref="XbgModel"/>, which walks 12-byte headers and reads DIKS as 4-byte
/// entries; that one stays until its preview and world consumers are migrated.
/// </para>
/// </remarks>
public sealed class XbgFile
{
    public const uint VersionFc2 = 0x0006002A;
    public const int ChunkHeader = 20;
    public const int NodeRecord = 0x44;
    public const int PaletteSlots = 48;
    public const short EmptySlot = -1;
    public const uint NoNode = 0xFFFFFFFF;

    /// <summary>A DIKS entry names the node that places its part, or this when none does.</summary>
    public const uint NoPlacement = 0xFFFF;

    /// <summary>
    /// Vertex component flags. The position is one of the first three; the rest are independent
    /// bits, and a buffer lays its components out in this fixed order.
    /// </summary>
    public const uint PosFloat = 0x0001;
    public const uint PosInt16 = 0x0002;
    public const uint PosHalf = 0x0004;
    public const uint Uv0 = 0x0008;
    public const uint BoneWeights1 = 0x0010;
    public const uint BoneWeights2 = 0x0020;
    public const uint Normal = 0x0040;
    public const uint Colour = 0x0080;
    public const uint Tangent = 0x0100;
    public const uint Binormal = 0x0200;
    public const uint Unk400 = 0x0400;
    public const uint Uv1 = 0x0800;
    public const uint Uv2 = 0x1000;

    /// <summary>The position encodings, in the order a buffer's flags are tested.</summary>
    private static readonly (uint Bit, string Name, int Size)[] PositionKinds =
    [
        (PosFloat, "pos_float", 12), (PosInt16, "pos_int16", 8), (PosHalf, "pos_half", 8),
    ];

    private static readonly (uint Bit, string Name, int Size)[] Components =
    [
        (Uv0, "uv0", 4), (Uv1, "uv1", 4), (Uv2, "uv2", 4),
        (BoneWeights1, "bone_wts1", 8), (BoneWeights2, "bone_wts2", 8),
        (Normal, "normal", 4), (Colour, "color", 4),
        (Tangent, "tangent", 4), (Binormal, "binormal", 4), (Unk400, "unk400", 4),
    ];

    /// <summary>
    /// Where each component sits inside one vertex, and the stride the flags imply.
    /// </summary>
    /// <remarks>
    /// The position is reported under the name <c>pos</c> whichever encoding it uses, so callers
    /// can find it without testing the flags again. Every shipped buffer stores int16 positions.
    /// </remarks>
    public static (List<(string Name, int Offset, int Size)> Layout, int Stride) VertexLayout(uint flags)
    {
        List<(string, int, int)> layout = [];
        int cursor = 0;
        foreach ((uint bit, _, int size) in PositionKinds)
        {
            if ((flags & bit) != 0)
            {
                layout.Add(("pos", cursor, size));
                cursor += size;
                break;
            }
        }
        foreach ((uint bit, string name, int size) in Components)
        {
            if ((flags & bit) != 0)
            {
                layout.Add((name, cursor, size));
                cursor += size;
            }
        }
        return (layout, cursor);
    }

    /// <summary>header_words[3] holds the byte count from <see cref="SizeOrigin"/> to end of file.</summary>
    public const int SizeField = 20;
    public const int SizeOrigin = 12;

    /// <summary>The second word of DOL\0, constant across every shipped mesh.</summary>
    public const uint LodWord1 = 98;

    public const string TagNode = "EDON";
    public const string TagBindMatrices = "MB2O";
    public const string TagMaterials = "LTMR";
    public const string TagPartRefs = "DIKS";
    public const string TagParts = "DNKS";
    public const string TagLods = "SDOL";
    public const string TagBox = "XOBB";
    public const string TagSphere = "HPSB";
    public const string TagLod = "DOL\0";
    public const string TagPosCompress = "PMCP";
    public const string TagUvCompress = "PMCU";
    public const string TagClusters = "SULC";
    public const string TagMaterialBody = "LTMD";

    private static ReadOnlySpan<byte> Magic => "HSEM"u8;

    public uint Version { get; set; } = VersionFc2;

    public uint[] HeaderWords { get; set; } = new uint[5];

    public List<XbgChunk> Chunks { get; } = [];

    public List<XbgNode> Nodes { get; } = [];

    public List<float[]> BindMatrices { get; } = [];

    public List<string> Materials { get; set; } = [];

    public uint? MaterialWord { get; set; }

    public List<XbgPartRef> PartRefs { get; } = [];

    public List<XbgLod> Lods { get; } = [];

    public List<XbgPart> Parts { get; } = [];

    public uint ClusterWord0 { get; set; }

    public float[] Box { get; set; } = [];

    public float[] Sphere { get; set; } = [];

    public uint[] LodWords { get; set; } = [];

    public float[] PosCompress { get; set; } = [];

    public float[] UvCompress { get; set; } = [];

    public float PosScale => PosCompress[1];

    public static XbgFile Parse(byte[] data)
    {
        if (!ByteCursor.Matches(data, 0, Magic))
        {
            throw new InvalidDataException("Not an .xbg file.");
        }

        var self = new XbgFile();
        var r = new ByteCursor(data) { Position = 4 };
        self.Version = r.ReadU32();
        self.HeaderWords = r.ReadU32Array(5);
        uint chunkCount = r.ReadU32();

        int pos = 32;
        for (uint i = 0; i < chunkCount; i++)
        {
            string tag = Tag(data, pos);
            var header = new ByteCursor(data) { Position = pos + 4 };
            uint word0 = header.ReadU32();
            int size = (int)header.ReadU32();
            int payloadSize = (int)header.ReadU32();
            uint subCount = header.ReadU32();
            if (size < ChunkHeader)
            {
                throw new InvalidDataException($"Chunk '{tag}' at {pos} has size {size}.");
            }

            var chunk = new XbgChunk { Tag = tag, Word0 = word0 };
            self.ReadChunk(data, chunk, pos, size, pos + size - payloadSize, subCount);
            self.Chunks.Add(chunk);
            pos += size;
        }

        if (pos != data.Length)
        {
            throw new InvalidDataException($"Trailing bytes: consumed {pos} of {data.Length}.");
        }
        return self;
    }

    public byte[] Write()
    {
        var w = new ByteWriter();
        w.WriteRaw(Magic);
        w.WriteU32(Version);
        w.WriteU32Array(HeaderWords);
        w.WriteU32((uint)Chunks.Count);
        foreach (XbgChunk chunk in Chunks)
        {
            WriteChunk(w, chunk);
        }
        w.PatchU32(SizeField, (uint)(w.Length - SizeOrigin));
        return w.ToArray();
    }

    /// <summary>The chunk carrying this tag, or null when the file has none.</summary>
    public XbgChunk? Chunk(string tag)
        => Chunks.FirstOrDefault(chunk => chunk.Tag == tag);

    /// <summary>Recompute sibling links and skin indices after nodes have been edited.</summary>
    public void RebuildHierarchy()
    {
        foreach (XbgNode node in Nodes)
        {
            node.FirstChild = NoNode;
            node.NextSibling = NoNode;
        }
        for (int index = Nodes.Count - 1; index >= 0; index--)
        {
            XbgNode node = Nodes[index];
            if (node.Parent < Nodes.Count)
            {
                XbgNode parent = Nodes[(int)node.Parent];
                node.NextSibling = parent.FirstChild;
                parent.FirstChild = (uint)index;
            }
        }
        int skinning = 0;
        foreach (XbgNode node in Nodes)
        {
            if (node.SkinIndex != EmptySlot)
            {
                node.SkinIndex = skinning++;
            }
        }
    }

    /// <summary>LTMR gained a trailing word after mesh version 41.3; FC2 ships 42.6.</summary>
    public static bool HasMaterialWord(uint version)
    {
        uint major = version & 0xFFFF;
        uint minor = version >> 16;
        return major > 0x29 || (major == 0x29 && minor > 3);
    }

    private static string Tag(ReadOnlySpan<byte> data, int offset)
    {
        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            chars[i] = (char)data[offset + i];
        }
        return new string(chars);
    }

    private void ReadChunk(byte[] data, XbgChunk chunk, int start, int size, int payload, uint subCount)
    {
        var r = new ByteCursor(data) { Position = payload };
        switch (chunk.Tag)
        {
            case TagNode:
                for (uint i = r.ReadU32(); i > 0; i--)
                {
                    Nodes.Add(ReadNode(ref r));
                }
                break;
            case TagBindMatrices:
                for (uint i = r.ReadU32(); i > 0; i--)
                {
                    BindMatrices.Add(r.ReadF32Array(16));
                }
                break;
            case TagMaterials:
                uint materialCount = r.ReadU32();
                MaterialWord = HasMaterialWord(Version) ? r.ReadU32() : null;
                Materials = [];
                for (uint i = 0; i < materialCount; i++)
                {
                    Materials.Add(r.ReadCString());
                }
                break;
            case TagPartRefs:
                for (uint i = r.ReadU32(); i > 0; i--)
                {
                    uint hash = r.ReadU32();
                    PartRefs.Add(new XbgPartRef { NameHash = hash, Node = r.ReadU32() >> 16 });
                }
                break;
            case TagLods:
                ReadLods(ref r);
                break;
            case TagParts:
                ReadParts(data, start, payload, subCount);
                break;
            case TagBox:
                Box = r.ReadF32Array(6);
                break;
            case TagSphere:
                Sphere = r.ReadF32Array(4);
                break;
            case TagLod:
                LodWords = r.ReadU32Array(2);
                break;
            case TagPosCompress:
                PosCompress = r.ReadF32Array(2);
                break;
            case TagUvCompress:
                UvCompress = r.ReadF32Array(2);
                break;
            default:
                chunk.Raw = data[(start + ChunkHeader)..(start + size)];
                return;
        }

        if (r.Position > start + size)
        {
            throw new InvalidDataException(
                $"Chunk '{chunk.Tag}' overran by {r.Position - start - size} bytes.");
        }
    }

    /// <summary>DNKS names the parts; its SULC sub-chunk holds their bone clusters.</summary>
    private void ReadParts(byte[] data, int start, int payload, uint subCount)
    {
        if (subCount != 1)
        {
            throw new InvalidDataException($"DNKS with {subCount} sub-chunks.");
        }

        int sub = start + ChunkHeader;
        if (Tag(data, sub) != TagClusters)
        {
            throw new InvalidDataException("DNKS sub-chunk is not SULC.");
        }

        var subHeader = new ByteCursor(data) { Position = sub + 4 };
        ClusterWord0 = subHeader.ReadU32();
        int subSize = (int)subHeader.ReadU32();
        int subPayloadSize = (int)subHeader.ReadU32();

        var names = new ByteCursor(data) { Position = payload };
        for (uint i = names.ReadU32(); i > 0; i--)
        {
            Parts.Add(ReadPart(ref names));
        }

        var clusters = new ByteCursor(data) { Position = sub + subSize - subPayloadSize };
        foreach (XbgPart part in Parts)
        {
            for (uint i = clusters.ReadU32(); i > 0; i--)
            {
                part.Clusters.Add(ReadCluster(ref clusters));
            }
        }
    }

    private static XbgPart ReadPart(ref ByteCursor r)
    {
        float lodMetric = r.ReadF32();
        float[] bounds = r.ReadF32Array(10);
        int lod = r.ReadI32();
        uint reserved = r.ReadU32();
        return new XbgPart
        {
            LodMetric = lodMetric,
            Bounds = bounds,
            Lod = lod,
            Reserved = reserved,
            Name = r.ReadCString(),
        };
    }

    private static XbgCluster ReadCluster(ref ByteCursor r)
    {
        ushort[] words = r.ReadU16Array(7);
        return new XbgCluster
        {
            MaterialIndex = words[0],
            FaceCount = words[1],
            Stride = words[4],
            VertexCount = words[5],
            Flags = words[6],
            Palette = r.ReadI16Array(PaletteSlots),
        };
    }

    private static XbgNode ReadNode(ref ByteCursor r)
    {
        int start = r.Position;
        uint[] links = r.ReadU32Array(4);
        var node = new XbgNode
        {
            Name = string.Empty,
            NameHash = links[0],
            FirstChild = links[1],
            NextSibling = links[2],
            Parent = links[3],
            Rotation = r.ReadF32Array(4),
            Translation = r.ReadF32Array(3),
            Scale = r.ReadF32Array(3),
            SkinIndex = r.ReadI32(),
            Weight = r.ReadF32(),
            Extent = r.ReadF32(),
        };
        r.Position = start + NodeRecord;
        node.Name = r.ReadCString();
        return node;
    }

    private void ReadLods(ref ByteCursor r)
    {
        for (uint i = r.ReadU32(); i > 0; i--)
        {
            float distance = r.ReadF32();
            List<XbgVertexBuffer> buffers = [];
            for (uint b = r.ReadU32(); b > 0; b--)
            {
                uint[] words = r.ReadU32Array(4);
                buffers.Add(new XbgVertexBuffer
                {
                    Flags = words[0], Stride = words[1], VertexCount = words[2], Offset = words[3],
                });
            }

            List<XbgSubmeshRef> submeshes = [];
            for (uint s = r.ReadU32(); s > 0; s--)
            {
                uint[] words = r.ReadU32Array(7);
                submeshes.Add(new XbgSubmeshRef
                {
                    Buffer = words[0], Part = words[1], Cluster = words[2],
                    IndexOffset = words[3], Trailing = words[4..],
                });
            }

            int vertexSize = (int)r.ReadU32();
            r.Align(16);
            byte[] vertexData = r.ReadBytes(vertexSize);
            int indexCount = (int)r.ReadU32();
            r.Align(16);
            Lods.Add(new XbgLod
            {
                Distance = distance,
                VertexBuffers = buffers,
                Submeshes = submeshes,
                VertexData = vertexData,
                IndexData = r.ReadBytes(indexCount * 2),
            });
        }
    }

    private void WriteChunk(ByteWriter w, XbgChunk chunk)
    {
        int start = w.Length;
        foreach (char c in chunk.Tag)
        {
            w.WriteU8((byte)c);
        }
        w.WriteU32(chunk.Word0);
        w.WriteU32(0);
        w.WriteU32(0);
        w.WriteU32(0);

        uint subCount = chunk.Tag == TagParts ? 1u : 0u;
        int payload;
        if (chunk.Tag == TagParts)
        {
            WriteClusters(w);
            payload = w.Length;
            w.WriteU32((uint)Parts.Count);
            foreach (XbgPart part in Parts)
            {
                WritePart(w, part);
            }
        }
        else
        {
            payload = w.Length;
            WritePayload(w, chunk);
        }

        w.PatchU32(start + 8, (uint)(w.Length - start));
        w.PatchU32(start + 12, (uint)(w.Length - payload));
        w.PatchU32(start + 16, subCount);
    }

    private void WritePayload(ByteWriter w, XbgChunk chunk)
    {
        switch (chunk.Tag)
        {
            case TagNode:
                w.WriteU32((uint)Nodes.Count);
                foreach (XbgNode node in Nodes)
                {
                    WriteNode(w, node);
                }
                break;
            case TagBindMatrices:
                w.WriteU32((uint)BindMatrices.Count);
                foreach (float[] matrix in BindMatrices)
                {
                    w.WriteF32Array(matrix);
                }
                break;
            case TagMaterials:
                w.WriteU32((uint)Materials.Count);
                if (MaterialWord is { } word)
                {
                    w.WriteU32(word);
                }
                foreach (string name in Materials)
                {
                    w.WriteCString(name);
                }
                break;
            case TagPartRefs:
                w.WriteU32((uint)PartRefs.Count);
                for (int position = 0; position < PartRefs.Count; position++)
                {
                    w.WriteU32(PartRefs[position].NameHash);
                    w.WriteU32((PartRefs[position].Node << 16) | (uint)position);
                }
                break;
            case TagLods:
                WriteLods(w);
                break;
            case TagBox:
                w.WriteF32Array(Box);
                break;
            case TagSphere:
                w.WriteF32Array(Sphere);
                break;
            case TagLod:
                w.WriteU32Array(LodWords);
                break;
            case TagPosCompress:
                w.WriteF32Array(PosCompress);
                break;
            case TagUvCompress:
                w.WriteF32Array(UvCompress);
                break;
            default:
                w.WriteRaw(chunk.Raw);
                break;
        }
    }

    private static void WriteNode(ByteWriter w, XbgNode node)
    {
        w.WriteU32Array([node.NameHash, node.FirstChild, node.NextSibling, node.Parent]);
        w.WriteF32Array(node.Rotation);
        w.WriteF32Array(node.Translation);
        w.WriteF32Array(node.Scale);
        w.WriteI32(node.SkinIndex);
        w.WriteF32(node.Weight);
        w.WriteF32(node.Extent);
        w.WriteCString(node.Name);
    }

    private static void WritePart(ByteWriter w, XbgPart part)
    {
        w.WriteF32(part.LodMetric);
        w.WriteF32Array(part.Bounds);
        w.WriteI32(part.Lod);
        w.WriteU32(part.Reserved);
        w.WriteCString(part.Name);
    }

    private void WriteLods(ByteWriter w)
    {
        w.WriteU32((uint)Lods.Count);
        foreach (XbgLod lod in Lods)
        {
            w.WriteF32(lod.Distance);
            w.WriteU32((uint)lod.VertexBuffers.Count);
            foreach (XbgVertexBuffer buffer in lod.VertexBuffers)
            {
                w.WriteU32Array([buffer.Flags, buffer.Stride, buffer.VertexCount, buffer.Offset]);
            }
            w.WriteU32((uint)lod.Submeshes.Count);
            foreach (XbgSubmeshRef submesh in lod.Submeshes)
            {
                w.WriteU32Array([submesh.Buffer, submesh.Part, submesh.Cluster, submesh.IndexOffset]);
                w.WriteU32Array(submesh.Trailing);
            }
            w.WriteU32((uint)lod.VertexData.Length);
            w.Align(16, AlignFill.DescendingCounter);
            w.WriteRaw(lod.VertexData);
            w.WriteU32((uint)(lod.IndexData.Length / 2));
            w.Align(16, AlignFill.DescendingCounter);
            w.WriteRaw(lod.IndexData);
        }
    }

    /// <summary>Emit the SULC sub-chunk, whose payload is also addressed from its end.</summary>
    private void WriteClusters(ByteWriter w)
    {
        int start = w.Length;
        foreach (char c in TagClusters)
        {
            w.WriteU8((byte)c);
        }
        w.WriteU32(ClusterWord0);
        w.WriteU32(0);
        w.WriteU32(0);
        w.WriteU32(0);

        int payload = w.Length;
        foreach (XbgPart part in Parts)
        {
            w.WriteU32((uint)part.Clusters.Count);
            foreach (XbgCluster cluster in part.Clusters)
            {
                w.WriteU16Array([
                    cluster.MaterialIndex, cluster.FaceCount, cluster.FaceCount,
                    (ushort)(cluster.FaceCount * 3), cluster.Stride, cluster.VertexCount,
                    cluster.Flags,
                ]);
                w.WriteI16Array(cluster.Palette);
            }
        }

        w.PatchU32(start + 8, (uint)(w.Length - start));
        w.PatchU32(start + 12, (uint)(w.Length - payload));
    }
}
