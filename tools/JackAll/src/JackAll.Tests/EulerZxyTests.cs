using System.Numerics;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// <c>hidAngles</c> to a rotation and back. Only the rotation is asserted, never the angles: at the
/// gimbal several triples mean the same orientation, and any of them is a correct answer.
/// </summary>
public class EulerZxyTests
{
    internal static void AssertSameRotation(Matrix4x4 expected, Matrix4x4 actual)
    {
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                Assert.Equal(expected[row, column], actual[row, column], 3);
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
            Matrix4x4 rotation = EulerZxy.ToMatrix(angles);

            AssertSameRotation(rotation, EulerZxy.ToMatrix(EulerZxy.FromMatrix(rotation)));
        }
    }

    [Theory]
    [InlineData(90f, 30f, 10f)]
    [InlineData(-90f, -45f, 120f)]
    public void The_gimbal_still_round_trips(float x, float y, float z)
    {
        Matrix4x4 rotation = EulerZxy.ToMatrix(new Vector3(x, y, z));

        AssertSameRotation(rotation, EulerZxy.ToMatrix(EulerZxy.FromMatrix(rotation)));
    }

    /// <summary>Within the principal ranges the angles come back as given, which is what keeps an
    /// untouched entity's saved <c>hidAngles</c> from drifting.</summary>
    [Fact]
    public void Principal_angles_come_back_unchanged()
    {
        Vector3 angles = EulerZxy.FromMatrix(EulerZxy.ToMatrix(new Vector3(10f, 20f, 30f)));

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
                Matrix4x4 rotation = EulerZxy.ToMatrix(angles);
                AssertSameRotation(rotation, EulerZxy.ToMatrix(EulerZxy.FromMatrix(rotation)));
                checkedCount++;
            }
        }
        Assert.True(checkedCount > 0);
    }
}
