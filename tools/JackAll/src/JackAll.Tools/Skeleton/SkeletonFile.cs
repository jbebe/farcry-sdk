using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.Skeleton;

/// <summary>A bone's orientation or position constraint payload.</summary>
public sealed class SkeletonConstraint
{
    public required int Kind { get; init; }

    public int[] Bones { get; init; } = [];

    public float[] Weights { get; init; } = [];

    public float[] Offset { get; init; } = [];
}

/// <summary>One bone: its rest pose, its place in the tree, and how the engine may solve it.</summary>
public sealed class SkeletonBone
{
    public required string Name { get; init; }

    public required uint NameHash { get; init; }

    public required ushort Id { get; set; }

    public required ushort Parent { get; set; }

    public required ushort FirstChild { get; set; }

    public required ushort NextSibling { get; set; }

    public required float[] ChildToParent { get; init; }

    public required float[] LocalOffset { get; init; }

    public required float Length { get; init; }

    public required SkeletonConstraint Ori { get; init; }

    public required SkeletonConstraint Pos { get; init; }

    public required byte AnimatedTranslation { get; init; }

    public required byte BodyPart { get; init; }

    public required float ComWeight { get; init; }

    public uint Version { get; init; } = SkeletonFile.BoneVersion;
}

/// <summary>A weapon socket: a named frame hanging off a bone.</summary>
public sealed class SkeletonAnimHandle
{
    public required ushort Id { get; init; }

    public required string Name { get; init; }

    public required uint NameHash { get; init; }

    public required string ParentBone { get; init; }

    public required uint ParentBoneHash { get; init; }

    public required float[] ChildToParent { get; init; }

    public required float[] LocalOffset { get; init; }

    public required float[] ParentToChild { get; init; }

    public required float[] LocalOffsetInverted { get; init; }

    public required float[] ParentToChildRepeat { get; init; }

    public uint Version { get; init; } = SkeletonFile.HandleVersion;
}

/// <summary>
/// Reader and writer for `.skeleton` (magic <c>LKS\0</c>), the Dunia rig.
/// </summary>
/// <remarks>
/// Layout is documented in docs/docs/file-formats/skeleton.md. Only object version 7 is supported,
/// which is the only one any shipped file uses. The gate is that re-serialising every retail rig
/// returns its bytes - 81 of 81 - because a wrong field width here silently reinterprets every bone
/// after it rather than failing.
/// <para>
/// The bone tree here is not the one an `.xbg` carries: on <c>pelvis_ref</c> four mid-joint helpers
/// hang off different parents, and animating on the mesh's tree tears the mesh.
/// </para>
/// </remarks>
public sealed class SkeletonFile
{
    public const uint FileVersionDefault = 18;
    public const uint ObjectTag = 0x3ADE68B1;
    public const uint BoneVersion = 7;
    public const uint HandleVersion = 3;

    /// <summary>A slot in the common or translation bone lists that names no bone.</summary>
    public const ushort NoBone = 0xFFFF;

    public const int OriNone = 0;
    public const int OriLookAt = 1;
    public const int OriBlend = 2;
    public const int OriDependent = 3;
    public const int OriDamped = 4;

    private static ReadOnlySpan<byte> Magic => "LKS\0"u8;

    public uint FileVersion { get; set; } = FileVersionDefault;

    public uint Version { get; set; } = BoneVersion;

    public List<SkeletonBone> Bones { get; } = [];

    public ushort[] CommonBoneIds { get; set; } = [];

    public List<SkeletonAnimHandle> Handles { get; } = [];

    public float ScaleFactor { get; set; } = 1.0f;

    public ushort[] TranslationBoneIds { get; set; } = [];

    /// <summary>
    /// Three groups, stored zeroed; <c>CSkeleton::FillLODBitmask</c> regenerates them after load.
    /// </summary>
    public List<uint[]> LodMasks { get; } = [];

    public static SkeletonFile Parse(byte[] data)
    {
        var r = new ByteCursor(data);
        if (!r.ReadSpan(4).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a .skeleton file.");
        }

        var self = new SkeletonFile { FileVersion = r.ReadU32() };
        uint tag = r.ReadU32();
        self.Version = r.ReadU32();
        if (tag != ObjectTag)
        {
            throw new InvalidDataException($"Bad skeleton object tag 0x{tag:X}.");
        }

        ushort boneCount = r.ReadU16();
        ushort commonCount = r.ReadU16();
        for (int i = 0; i < boneCount; i++)
        {
            self.Bones.Add(ReadBone(ref r));
        }

        self.CommonBoneIds = r.ReadU16Array(commonCount);
        ushort handleCount = r.ReadU16();
        for (int i = 0; i < handleCount; i++)
        {
            self.Handles.Add(ReadHandle(ref r));
        }

        self.ScaleFactor = r.ReadF32();
        self.TranslationBoneIds = r.ReadU16Array(r.ReadU16());
        for (int i = 0; i < 3; i++)
        {
            self.LodMasks.Add(r.ReadU32Array((int)r.ReadU32()));
        }

        if (r.Position != data.Length)
        {
            throw new InvalidDataException(
                $"Trailing bytes: consumed {r.Position} of {data.Length}.");
        }
        return self;
    }

    public byte[] Write()
    {
        var w = new ByteWriter();
        w.WriteRaw(Magic);
        w.WriteU32(FileVersion);
        w.WriteU32(ObjectTag);
        w.WriteU32(Version);
        w.WriteU16((ushort)Bones.Count);
        w.WriteU16((ushort)CommonBoneIds.Length);

        foreach (SkeletonBone bone in Bones)
        {
            WriteBone(w, bone);
        }

        w.WriteU16Array(CommonBoneIds);
        w.WriteU16((ushort)Handles.Count);
        foreach (SkeletonAnimHandle handle in Handles)
        {
            WriteHandle(w, handle);
        }

        w.WriteF32(ScaleFactor);
        w.WriteU16((ushort)TranslationBoneIds.Length);
        w.WriteU16Array(TranslationBoneIds);
        foreach (uint[] mask in LodMasks)
        {
            w.WriteU32((uint)mask.Length);
            w.WriteU32Array(mask);
        }
        return w.ToArray();
    }

    /// <summary>The bone whose exact-case name hashes to this one, the way the engine matches.</summary>
    public SkeletonBone? BoneByName(string name)
    {
        uint wanted = FcbClassDefinitions.Crc32Ascii(name);
        return Bones.FirstOrDefault(bone => bone.NameHash == wanted);
    }

    /// <summary>Recompute first-child and next-sibling links from each bone's parent.</summary>
    public void RebuildHierarchy()
    {
        Dictionary<ushort, SkeletonBone> byId = Bones.ToDictionary(bone => bone.Id);
        foreach (SkeletonBone bone in Bones)
        {
            bone.FirstChild = NoBone;
            bone.NextSibling = NoBone;
        }
        for (int i = Bones.Count - 1; i >= 0; i--)
        {
            SkeletonBone bone = Bones[i];
            if (byId.TryGetValue(bone.Parent, out SkeletonBone? parent))
            {
                bone.NextSibling = parent.FirstChild;
                parent.FirstChild = bone.Id;
            }
        }
    }

    private static SkeletonBone ReadBone(ref ByteCursor r)
    {
        uint tag = r.ReadU32();
        uint version = r.ReadU32();
        if (tag != ObjectTag || version < BoneVersion)
        {
            throw new InvalidDataException($"Unsupported bone encoding 0x{tag:X} v{version}.");
        }

        float[] childToParent = r.ReadF32Array(4);
        float[] localOffset = r.ReadF32Array(3);
        float length = r.ReadF32();
        ushort[] ids = r.ReadU16Array(4);
        SkeletonConstraint ori = ReadConstraint(ref r, r.ReadU8(), isOri: true);
        SkeletonConstraint pos = ReadConstraint(ref r, r.ReadU8(), isOri: false);
        (uint hash, string name) = r.ReadStringId();
        return new SkeletonBone
        {
            Name = name,
            NameHash = hash,
            Id = ids[0],
            Parent = ids[1],
            FirstChild = ids[2],
            NextSibling = ids[3],
            ChildToParent = childToParent,
            LocalOffset = localOffset,
            Length = length,
            Ori = ori,
            Pos = pos,
            AnimatedTranslation = r.ReadU8(),
            BodyPart = r.ReadU8(),
            ComWeight = r.ReadF32(),
            Version = version,
        };
    }

    private static void WriteBone(ByteWriter w, SkeletonBone bone)
    {
        w.WriteU32(ObjectTag);
        w.WriteU32(bone.Version);
        w.WriteF32Array(bone.ChildToParent);
        w.WriteF32Array(bone.LocalOffset);
        w.WriteF32(bone.Length);
        w.WriteU16Array([bone.Id, bone.Parent, bone.FirstChild, bone.NextSibling]);
        w.WriteU8((byte)bone.Ori.Kind);
        WriteConstraint(w, bone.Ori, isOri: true);
        w.WriteU8((byte)bone.Pos.Kind);
        WriteConstraint(w, bone.Pos, isOri: false);
        w.WriteStringId(bone.Name, bone.NameHash);
        w.WriteU8(bone.AnimatedTranslation);
        w.WriteU8(bone.BodyPart);
        w.WriteF32(bone.ComWeight);
    }

    /// <summary>Constraint payloads are fixed per kind; see the SerializeBone switch.</summary>
    private static SkeletonConstraint ReadConstraint(ref ByteCursor r, int kind, bool isOri)
    {
        if (isOri)
        {
            switch (kind)
            {
                case OriLookAt:
                    return new SkeletonConstraint
                    {
                        Kind = kind, Bones = [r.ReadI32()], Offset = r.ReadF32Array(3),
                    };
                case OriBlend:
                    int first = r.ReadI32();
                    float firstWeight = r.ReadF32();
                    int second = r.ReadI32();
                    return new SkeletonConstraint
                    {
                        Kind = kind,
                        Bones = [first, second],
                        Weights = [firstWeight, r.ReadF32()],
                    };
                case OriDependent:
                case OriDamped:
                    return new SkeletonConstraint
                    {
                        Kind = kind, Bones = [r.ReadI32()], Weights = [r.ReadF32()],
                    };
            }
        }
        else if (kind is >= 1 and <= 3)
        {
            return new SkeletonConstraint
            {
                Kind = kind, Bones = [r.ReadI32()], Offset = r.ReadF32Array(3),
            };
        }
        return new SkeletonConstraint { Kind = kind };
    }

    private static void WriteConstraint(ByteWriter w, SkeletonConstraint c, bool isOri)
    {
        if (isOri)
        {
            switch (c.Kind)
            {
                case OriLookAt:
                    w.WriteI32(c.Bones[0]);
                    w.WriteF32Array(c.Offset);
                    break;
                case OriBlend:
                    w.WriteI32(c.Bones[0]);
                    w.WriteF32(c.Weights[0]);
                    w.WriteI32(c.Bones[1]);
                    w.WriteF32(c.Weights[1]);
                    break;
                case OriDependent:
                case OriDamped:
                    w.WriteI32(c.Bones[0]);
                    w.WriteF32(c.Weights[0]);
                    break;
            }
        }
        else if (c.Kind is >= 1 and <= 3)
        {
            w.WriteI32(c.Bones[0]);
            w.WriteF32Array(c.Offset);
        }
    }

    private static SkeletonAnimHandle ReadHandle(ref ByteCursor r)
    {
        uint tag = r.ReadU32();
        uint version = r.ReadU32();
        if (tag != ObjectTag)
        {
            throw new InvalidDataException($"Bad anim handle tag 0x{tag:X}.");
        }

        ushort id = r.ReadU16();
        (uint nameHash, string name) = r.ReadStringId();
        (uint parentHash, string parent) = r.ReadStringId();
        return new SkeletonAnimHandle
        {
            Id = id,
            Name = name,
            NameHash = nameHash,
            ParentBone = parent,
            ParentBoneHash = parentHash,
            ChildToParent = r.ReadF32Array(4),
            LocalOffset = r.ReadF32Array(3),
            ParentToChild = r.ReadF32Array(4),
            LocalOffsetInverted = r.ReadF32Array(3),
            ParentToChildRepeat = r.ReadF32Array(4),
            Version = version,
        };
    }

    private static void WriteHandle(ByteWriter w, SkeletonAnimHandle handle)
    {
        w.WriteU32(ObjectTag);
        w.WriteU32(handle.Version);
        w.WriteU16(handle.Id);
        w.WriteStringId(handle.Name, handle.NameHash);
        w.WriteStringId(handle.ParentBone, handle.ParentBoneHash);
        w.WriteF32Array(handle.ChildToParent);
        w.WriteF32Array(handle.LocalOffset);
        w.WriteF32Array(handle.ParentToChild);
        w.WriteF32Array(handle.LocalOffsetInverted);
        w.WriteF32Array(handle.ParentToChildRepeat);
    }
}
