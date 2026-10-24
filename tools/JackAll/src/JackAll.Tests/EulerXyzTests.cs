using System.Numerics;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// <c>hidAngles</c> to a rotation and back. Only the rotation is asserted, never the angles: at the
/// gimbal several triples mean the same orientation, and any of them is a correct answer.
/// </summary>
public class EulerXyzTests
{
    internal static void AssertSameRotation(Matrix4x4 expected, Matrix4x4 actual)
    {
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                Assert.Equal(expected[row, column], actual[row, column], 0.001f);
            }
        }
    }

    [Fact]
    public void Arbitrary_angles_survive_the_round_trip()
    {
        var random = new Random(2026);
        for (int i = 0; i < 500; i++)
        {
            var angles = new Vector3(
                random.NextSingle() * 360f - 180f, random.NextSingle() * 360f - 180f, random.NextSingle() * 360f - 180f);
            Matrix4x4 rotation = EulerXyz.ToMatrix(angles);

            AssertSameRotation(rotation, EulerXyz.ToMatrix(EulerXyz.FromMatrix(rotation)));
        }
    }

    /// <summary>The rows are the model's axes as <c>CEntity::SetAngles</c> writes them.</summary>
    [Fact]
    public void The_axes_are_the_engines()
    {
        const float x = -83.2271f, y = -80.6305f, z = 3.52274f;
        float sx = MathF.Sin(x * MathF.PI / 180), cx = MathF.Cos(x * MathF.PI / 180);
        float sy = MathF.Sin(y * MathF.PI / 180), cy = MathF.Cos(y * MathF.PI / 180);
        float sz = MathF.Sin(z * MathF.PI / 180), cz = MathF.Cos(z * MathF.PI / 180);
        var engine = new Matrix4x4(
            cy * cz, cy * sz, -sy, 0,
            sx * sy * cz - cx * sz, sx * sy * sz + cx * cz, sx * cy, 0,
            cx * sy * cz + sx * sz, cx * sy * sz - sx * cz, cx * cy, 0,
            0, 0, 0, 1);

        AssertSameRotation(engine, EulerXyz.ToMatrix(new Vector3(x, y, z)));
    }

    [Theory]
    [InlineData(30f, 90f, 10f)]
    [InlineData(-45f, -90f, 120f)]
    public void The_gimbal_still_round_trips(float x, float y, float z)
    {
        Matrix4x4 rotation = EulerXyz.ToMatrix(new Vector3(x, y, z));

        AssertSameRotation(rotation, EulerXyz.ToMatrix(EulerXyz.FromMatrix(rotation)));
    }

    /// <summary>Within the principal ranges the angles come back as given, which is what keeps an
    /// untouched entity's saved <c>hidAngles</c> from drifting.</summary>
    [Fact]
    public void Principal_angles_come_back_unchanged()
    {
        Vector3 angles = EulerXyz.FromMatrix(EulerXyz.ToMatrix(new Vector3(10f, 20f, 30f)));

        Assert.Equal(10f, angles.X, 3);
        Assert.Equal(20f, angles.Y, 3);
        Assert.Equal(30f, angles.Z, 3);
    }

    /// <summary>Every orientation a retail sector ships survives, including its large and odd ones.</summary>
    [Fact]
    public void Every_orientation_in_a_retail_sector_round_trips()
    {
        if (Fixture.Read(WorldSectorFragmentTests.Sector56) is not { } bytes) return;

        FcbObject root = FcbDocument.Deserialize(bytes);
        int checkedCount = 0;
        foreach (FcbObject entity in root.Children.SelectMany(layer => layer.Children))
        {
            if (FcbEntityFields.ReadVector3(entity, WorldHashes.HidAngles) is { } angles)
            {
                Matrix4x4 rotation = EulerXyz.ToMatrix(angles);
                AssertSameRotation(rotation, EulerXyz.ToMatrix(EulerXyz.FromMatrix(rotation)));
                checkedCount++;
            }
        }
        Assert.True(checkedCount > 0);
    }
}
