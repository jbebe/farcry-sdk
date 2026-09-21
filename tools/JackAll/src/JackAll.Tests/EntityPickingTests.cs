using System.Numerics;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>Viewport picking: the ray against each entity's own rotated box, nearest hit first.</summary>
public class EntityPickingTests
{
    private static readonly (Vector3, Vector3) UnitBox = (new Vector3(-0.5f), new Vector3(0.5f));

    private static WorldEntity At(Vector3 position, Vector3 angles = default) => new()
    {
        Node = new FcbObject(),
        HomeSector = new WorldSectorDocument { SourcePath = "", SectorId = 0, PristineRoot = new FcbObject() },
        LayerPathId = MissionLayers.MainName,
        Position = position,
        Angles = angles,
    };

    [Fact]
    public void The_nearest_box_along_the_ray_wins()
    {
        WorldEntity far = At(new Vector3(10f, 0f, 0f));
        WorldEntity near = At(new Vector3(5f, 0f, 0f));

        Assert.Same(near, EntityPicking.Pick(Vector3.Zero, Vector3.UnitX, [far, near], _ => UnitBox));
    }

    [Fact]
    public void A_ray_wide_of_every_box_picks_nothing()
        => Assert.Null(EntityPicking.Pick(Vector3.Zero, Vector3.UnitY, [At(new Vector3(5f, 0f, 0f))], _ => UnitBox));

    /// <summary>A long thin box turned 90° is hit where it now lies, not where it would lie unturned.</summary>
    [Fact]
    public void A_turned_box_is_hit_along_its_turned_extent()
    {
        (Vector3, Vector3) plank = (new Vector3(-4f, -0.1f, -0.1f), new Vector3(4f, 0.1f, 0.1f));
        WorldEntity turned = At(Vector3.Zero, new Vector3(0f, 0f, 90f));
        Vector3 from = new(0f, 3f, -10f);

        Assert.Same(turned, EntityPicking.Pick(from, Vector3.UnitZ, [turned], _ => plank));
        Assert.Null(EntityPicking.Pick(new Vector3(3f, 0f, -10f), Vector3.UnitZ, [turned], _ => plank));
    }
}
