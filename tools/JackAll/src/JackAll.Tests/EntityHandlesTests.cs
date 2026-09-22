using System.Numerics;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>The trigger and light handles: what each grabs, and what a drag writes back.</summary>
public class EntityHandlesTests
{
    private static readonly Vector3 At = new(100f, 200f, 10f);
    private static readonly uint Trigger = FcbClassDefinitions.Crc32Ascii("CProximityTriggerComponent");
    private static readonly uint Light = FcbClassDefinitions.Crc32Ascii("CDynamicLightComponent");
    private static readonly uint VectorSize = FcbClassDefinitions.Crc32Ascii("vectorSize");
    private static readonly uint Radius = FcbClassDefinitions.Crc32Ascii("fRadius");
    private static readonly uint Outer = FcbClassDefinitions.Crc32Ascii("fOuterAngle");
    private static readonly uint Inner = FcbClassDefinitions.Crc32Ascii("fInnerAngle");

    /// <summary>A fixed screen size of one metre wherever the handle is.</summary>
    private static float OneMetre(Vector3 _) => 1f;

    private static (WorldEntity Entity, FcbObject Component) With(uint component, Action<FcbObject> fill, Vector3 angles = default)
    {
        var comp = new FcbObject { TypeHash = component };
        fill(comp);
        var components = new FcbObject { TypeHash = WorldHashes.Components };
        components.Children.Add(comp);
        var node = new FcbObject { TypeHash = WorldHashes.Entity };
        node.Children.Add(components);
        var entity = new WorldEntity
        {
            Node = node,
            HomeSector = new WorldSectorDocument { SourcePath = "s", SectorId = 0, PristineRoot = new FcbObject() },
            LayerPathId = MissionLayers.MainName,
            Position = At,
            Angles = angles,
        };
        return (entity, comp);
    }

    private static (WorldEntity, FcbObject) Box(Vector3 size, float yaw = 0f)
        => With(Trigger, c => c.Values[VectorSize] = FcbEntityFields.Vector3Bytes(size), new Vector3(0f, 0f, yaw));

    private static (WorldEntity, FcbObject) Spot(float radius, float outer, float inner)
        => With(Light, c =>
        {
            c.Values[FcbClassDefinitions.Crc32Ascii("hidType")] = BitConverter.GetBytes(3u);
            c.Values[Radius] = BitConverter.GetBytes(radius);
            c.Values[Outer] = BitConverter.GetBytes(outer);
            c.Values[Inner] = BitConverter.GetBytes(inner);
        }, new Vector3(-90f, 0f, 0f));

    private static (Vector3, Vector3) RayAt(Vector3 point, Vector3 eye) => (eye, Vector3.Normalize(point - eye));

    [Fact]
    public void A_box_offers_one_handle_per_face_on_its_surface()
    {
        (WorldEntity entity, _) = Box(new Vector3(4f, 6f, 2f), yaw: 90f);
        IReadOnlyList<EntityHandle> handles = EntityHandles.For(entity, entity.Node);

        Assert.Equal(6, handles.Count);
        EntityHandle plusX = handles[0];
        Assert.Equal(At.X, plusX.Point.X, 3);
        Assert.Equal(At.Y + 2f, plusX.Point.Y, 3);
    }

    [Fact]
    public void Dragging_a_face_outwards_grows_the_box_along_that_axis_by_twice_as_much()
    {
        (WorldEntity entity, FcbObject trigger) = Box(new Vector3(4f, 6f, 2f));
        IReadOnlyList<EntityHandle> handles = EntityHandles.For(entity, entity.Node);
        Vector3 eye = At + new Vector3(3f, -30f, 5f);

        (Vector3 o, Vector3 d) = RayAt(handles[0].Point, eye);
        HandleGrab grab = EntityHandles.Grab(handles, o, d, OneMetre)!.Value;
        Assert.Equal(4f, EntityHandles.Follow(grab, o, d)!.Value, 3);

        (Vector3 o2, Vector3 d2) = RayAt(handles[0].Point + new Vector3(1f, 0f, 0f), eye);
        float size = EntityHandles.Follow(grab, o2, d2)!.Value;
        EntityHandles.Apply(entity.Node, grab.Handle, size);

        Assert.Equal(new Vector3(6f, 6f, 2f), FcbEntityFields.ReadVector3(trigger, VectorSize));
    }

    [Fact]
    public void A_light_radius_ring_follows_the_cursor_across_its_plane()
    {
        (WorldEntity entity, FcbObject light) = With(Light, c => c.Values[Radius] = BitConverter.GetBytes(5f));
        IReadOnlyList<EntityHandle> handles = EntityHandles.For(entity, entity.Node);
        Vector3 eye = At + new Vector3(0f, -20f, 30f);

        (Vector3 o, Vector3 d) = RayAt(At + new Vector3(5f, 0f, 0f), eye);
        HandleGrab grab = EntityHandles.Grab(handles, o, d, OneMetre)!.Value;
        Assert.Equal(HandleKind.Radius, grab.Handle.Kind);

        (Vector3 o2, Vector3 d2) = RayAt(At + new Vector3(8f, 0f, 0f), eye);
        EntityHandles.Apply(entity.Node, grab.Handle, EntityHandles.Follow(grab, o2, d2)!.Value);

        Assert.Equal(8f, FcbEntityFields.ReadFloat(light, Radius)!.Value, 3);
    }

    /// <summary>Every retail ceiling spot is turned -90 about X, so that turn must point it down.</summary>
    [Fact]
    public void A_spot_turned_minus_90_about_x_shines_down()
    {
        Vector3 down = EntityHandles.SpotDirection(new Vector3(-90f, 0f, 0f));

        Assert.Equal(-1f, down.Z, 4);
    }

    [Fact]
    public void Widening_the_inner_cone_past_the_outer_stops_at_the_outer()
    {
        (WorldEntity entity, FcbObject light) = Spot(radius: 4f, outer: 60f, inner: 20f);
        EntityHandle inner = EntityHandles.For(entity, entity.Node).Single(h => h.Kind == HandleKind.InnerCone);

        EntityHandles.Apply(entity.Node, inner, 90f);

        Assert.Equal(60f, FcbEntityFields.ReadFloat(light, Inner));
    }

    [Fact]
    public void A_cone_rim_sits_where_its_angle_puts_it()
    {
        (WorldEntity entity, _) = Spot(radius: 4f, outer: 90f, inner: 20f);
        EntityHandle outer = EntityHandles.For(entity, entity.Node).Single(h => h.Kind == HandleKind.OuterCone);

        Assert.Equal(At.Z - 4f, outer.Centre.Z, 3);
        Assert.Equal(4f, outer.Extent, 3);

        Vector3 eye = At + new Vector3(0f, -10f, 20f);
        (Vector3 o, Vector3 d) = RayAt(outer.Centre + new Vector3(4f, 0f, 0f), eye);
        HandleGrab grab = EntityHandles.Grab([outer], o, d, OneMetre)!.Value;
        Assert.Equal(90f, EntityHandles.Follow(grab, o, d)!.Value, 2);
    }
}
