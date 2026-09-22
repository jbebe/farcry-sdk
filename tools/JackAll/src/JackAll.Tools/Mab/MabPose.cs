using System.Numerics;
using JackAll.Tools.Skeleton;

namespace JackAll.Tools.Mab;

/// <summary>
/// One clip on the rig it was authored for: every bone's world transform at a frame.
/// </summary>
/// <remarks>
/// A clip's value for a bone replaces the rig's rest value - a keyed or constant rotation stands in
/// for <c>m_ChildToParent</c>, an offset for <c>m_LocalOffset</c> - and a bone the clip leaves out
/// keeps its rest pose.
/// </remarks>
public sealed class MabPose
{
    private readonly SkeletonFile _rig;
    private readonly Dictionary<int, (int[] Frames, Quaternion[] Values)> _rotations;
    private readonly Dictionary<int, (int[] Frames, Vector3[] Values)> _offsets;

    public int LastFrame { get; }

    public int Rate { get; }

    public MabPose(SkeletonFile rig, MabClip clip)
    {
        _rig = rig;
        (LastFrame, Rate) = clip.Timing() ?? (0, 30);
        _rotations = Tracks(clip.ConstantRotations(), clip.KeyframeTracks(), Quat);
        _offsets = Tracks(clip.ConstantTranslations(), clip.TranslationTracks(), Vec);
    }

    /// <summary>Whether the clip names only bones this rig has.</summary>
    public static bool Fits(SkeletonFile rig, MabClip clip)
        => clip.BoneIds().All(id => id < rig.Bones.Count);

    /// <summary>Each bone's world transform at a fractional frame, indexed by bone id.</summary>
    public Matrix4x4[] WorldAt(float frame)
    {
        frame = Math.Clamp(frame, 0, LastFrame);
        var world = new Matrix4x4[_rig.Bones.Count];
        for (int id = 0; id < world.Length; id++)
        {
            Resolve(id, frame, world);
        }
        return world;
    }

    /// <summary>A constant is a one-key track, so every bone samples the same way.</summary>
    private static Dictionary<int, (int[] Frames, T[] Values)> Tracks<T>(
        Dictionary<int, float[]> constants, Dictionary<int, List<(int Frame, float[]? Value)>> keyed,
        Func<float[], T> make)
    {
        Dictionary<int, (int[], T[])> tracks = constants.ToDictionary(pair => pair.Key, pair => (new[] { 0 }, new[] { make(pair.Value) }));
        foreach ((int bone, List<(int Frame, float[]? Value)> track) in keyed)
        {
            var keys = track.Where(k => k.Value is not null).ToList();
            tracks[bone] = ([.. keys.Select(k => k.Frame)], [.. keys.Select(k => make(k.Value!))]);
        }
        return tracks;
    }

    private void Resolve(int id, float frame, Matrix4x4[] world)
    {
        // A resolved transform has M44 = 1; an untouched slot is all zero.
        if (world[id].M44 != 0)
        {
            return;
        }

        SkeletonBone bone = _rig.Bones[id];
        Quaternion rotation = _rotations.TryGetValue(id, out var keyed)
            ? Sample(keyed.Frames, keyed.Values, frame, Quaternion.Slerp)
            : Quat(bone.ChildToParent);
        Vector3 offset = _offsets.TryGetValue(id, out var moved)
            ? Sample(moved.Frames, moved.Values, frame, Vector3.Lerp)
            : Vec(bone.LocalOffset);

        Matrix4x4 local = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(offset);
        if (bone.Parent < world.Length)
        {
            Resolve(bone.Parent, frame, world);
            local *= world[bone.Parent];
        }
        world[id] = local;
    }

    /// <summary>The value at a fractional frame, blended between the keys either side of it.</summary>
    private static T Sample<T>(int[] frames, T[] values, float frame, Func<T, T, float, T> blend)
    {
        int next = Array.BinarySearch(frames, (int)Math.Ceiling(frame));
        if (next < 0)
        {
            next = ~next;
        }
        if (next == 0)
        {
            return values[0];
        }
        if (next >= frames.Length)
        {
            return values[^1];
        }

        int previous = next - 1;
        float t = (frame - frames[previous]) / (frames[next] - frames[previous]);
        return blend(values[previous], values[next], t);
    }

    private static Quaternion Quat(float[] xyzw) => new(xyzw[0], xyzw[1], xyzw[2], xyzw[3]);

    private static Vector3 Vec(float[] xyz) => new(xyz[0], xyz[1], xyz[2]);
}
