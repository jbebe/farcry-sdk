using JackAll.Tools.Hkx;

namespace JackAll.Tests;

/// <summary>
/// A shipped `.hkx` re-serialises to its own bytes, and the class layouts behind it are Dunia's own.
/// </summary>
public sealed class HkxFileTests
{
    // A vehicle: hinged doors and hood, MOPP meshes, and Dunia's own nomadPartModifiersGroup.
    private const string Vehicle = "Hkx/bigtruck.hkx";

    // A ragdoll, the only kind of file that stores class reflection in __types__.
    private const string Ragdoll = "Hkx/generic_ragdoll.hkx";

    // A building whose shape subparts leave its local fixups out of source order.
    private const string Building = "Hkx/colonialgarage02_ruin.hkx";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Vehicle, Ragdoll, Building);

    /// <summary>The writer computes every listed signature from the registry, so this also proves them.</summary>
    [Theory]
    [InlineData(Vehicle)]
    [InlineData(Ragdoll)]
    [InlineData(Building)]
    public void Reserialises_a_shipped_file_byte_for_byte(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original)
        {
            return;
        }

        Fixture.AssertSameBytes(fixture, original, HkxFile.Parse(original).Write());
    }

    [Fact]
    public void The_truck_hinges_its_doors_and_hood_with_motors()
    {
        if (Fixture.Read(Vehicle) is not { } bytes)
        {
            return;
        }

        var counts = HkxFile.Parse(bytes).Objects().CountBy(o => o.Class.Name).ToDictionary();

        Assert.Equal(3, counts["hkpConstraintInstance"]);
        Assert.Equal(3, counts["hkpLimitedHingeConstraintData"]);
        Assert.Equal(3, counts["hkpPositionConstraintMotor"]);
        Assert.Equal(1, counts["nomadPartModifiersGroup"]);
    }

    [Fact]
    public void A_class_the_file_never_had_is_listed_when_an_object_needs_it()
    {
        if (Fixture.Read(Vehicle) is not { } bytes)
        {
            return;
        }

        HkxFile file = HkxFile.Parse(bytes);
        HkxPointer motor = file.Objects().First(o => o.Class.Name == "hkpLimitedHingeConstraintData")
            .Refs.Values.OfType<HkxPointer>().First(p => p.Target.Class.Name == "hkpPositionConstraintMotor");
        HkClass velocity = HavokClasses.Default["hkpVelocityConstraintMotor"];
        motor.Target = new HkxObject(velocity, new byte[velocity.Size]);

        HkxFile reread = HkxFile.Parse(file.Write());

        Assert.Contains(velocity.Name, reread.ClassNames);
        Assert.Single(reread.Objects(), o => o.Class == velocity);
        Assert.Equal(2, reread.Objects().Count(o => o.Class.Name == "hkpPositionConstraintMotor"));
    }

    [Fact]
    public void A_cloned_body_edited_by_member_name_survives_a_write_and_leaves_its_original_alone()
    {
        if (Fixture.Read(Vehicle) is not { } bytes)
        {
            return;
        }

        HkxFile file = HkxFile.Parse(bytes);
        HkxObject system = file.Objects().Single(o => o.Class.Name == "hkpPhysicsSystem");
        HkxObject door = Body(file, "DoorLeftState02");
        HkxObject fan = door.Clone();
        fan.SetString("name", "Fan");
        fan.SetReals("motion.motionState.sweptTransform.centerOfMassLocal", 1, 2, 3, 0);
        system.Insert("rigidBodies", 1, fan);

        HkxFile reread = HkxFile.Parse(file.Write());
        HkxObject written = Body(reread, "Fan");
        List<string?> names = [.. reread.Objects().Single(o => o.Class.Name == "hkpPhysicsSystem")
            .GetPointers("rigidBodies").Select(b => b?.GetString("name"))];

        Assert.Equal([1f, 2f, 3f, 0f], written.GetReals("motion.motionState.sweptTransform.centerOfMassLocal"));
        Assert.Equal("hkpConvexVerticesShape", written.GetPointer("collidable.shape")!.Class.Name);
        Assert.Equal(["Chassis", "Fan", "DoorLeftState02"], names.Take(3));
        Assert.Equal(32, names.Count);
        Assert.NotEqual(1f, Body(reread, "DoorLeftState02").GetReals("motion.motionState.sweptTransform.centerOfMassLocal")[0]);
    }

    [Fact]
    public void The_dump_names_members_and_numbers_pointers()
    {
        if (Fixture.Read(Vehicle) is not { } bytes)
        {
            return;
        }

        string text = HkxDump.Write(HkxFile.Parse(bytes));

        Assert.Contains("  name: \"DoorLeftState02\"", text);
        Assert.Contains("  motion.motionState.maxAngularVelocity: 86", text);
        Assert.Contains("entities[1]: -> #3 hkpRigidBody", text);
    }

    private static HkxObject Body(HkxFile file, string name)
        => file.Objects().Single(o => o.Class.Name == "hkpRigidBody" && o.GetString("name") == name);
}
