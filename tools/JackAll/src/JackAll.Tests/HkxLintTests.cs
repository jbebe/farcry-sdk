using JackAll.Tools.Hkx;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>The lint is silent on shipped files and speaks up on the edits that would break one in game.</summary>
public sealed class HkxLintTests
{
    // One body: a MOPP over a list of six boxes; the seat, child 4, is the topmost.
    private const string Chair = "Hkx/chairbar01.hkx";

    // Bodies, then HIGH_ ones, and hinges between them.
    private const string Truck = "Hkx/bigtruck.hkx";

    // A MOPP over an extended mesh with a subpart of boxes.
    private const string Garage = "Hkx/colonialgarage02_ruin.hkx";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Chair, Truck, Garage);

    [Theory]
    [InlineData(Chair)]
    [InlineData(Truck)]
    [InlineData(Garage)]
    public void A_shipped_file_has_no_problems(string fixture)
    {
        if (Read(fixture) is not { } file)
        {
            return;
        }

        Assert.Empty(HkxLint.Check(file));
    }

    [Fact]
    public void A_seat_moved_without_a_refresh_is_lost_to_the_mopp_and_outside_the_list_bounds_until_refreshed()
    {
        if (Fixture.Read(Chair) is not { } bytes)
        {
            return;
        }

        HkxFile file = HkxFile.Parse(bytes);
        HkxObject seat = file.Objects().First(o => o.Class.Name == "hkpListShape").GetPointer("childInfo[4].shape")!;
        float[] at = seat.GetReals("translation");
        seat.SetReals("translation", at[0], at[1], at[2] + 2, at[3]);

        IReadOnlyList<HkxProblem> stale = HkxLint.Check(file);
        HkxCaches.Refresh(file, HkxFile.Parse(bytes));

        Assert.Contains(stale, p => p.Severity == LintSeverity.Error && p.Message.Contains("MOPP"));
        Assert.Contains(stale, p => p.Severity == LintSeverity.Error && p.Message.Contains("cached bounds"));
        Assert.Empty(HkxLint.Check(file));
    }

    [Fact]
    public void A_body_after_the_first_HIGH_body_is_an_error()
    {
        if (Read(Truck) is not { } file)
        {
            return;
        }

        HkxObject system = file.Objects().First(o => o.Class.Name == "hkpPhysicsSystem");
        HkxObject chassis = system.GetPointers("rigidBodies")[0]!;
        system.Append("rigidBodies", chassis);

        Assert.Contains(HkxLint.Check(file), p => p.Severity == LintSeverity.Error && p.Message.Contains("comes after"));
    }

    [Fact]
    public void A_hand_set_radius_stays_while_the_shapes_do_and_a_new_body_gets_havoks_own()
    {
        if (Fixture.Read(Chair) is not { } bytes)
        {
            return;
        }

        HkxFile file = HkxFile.Parse(bytes);
        HkxObject body = file.Objects().Single(o => o.Class.Name == "hkpRigidBody");
        body.SetReals("motion.motionState.objectRadius", 3);
        HkxCaches.Refresh(file, HkxFile.Parse(bytes));
        Assert.Equal(3, body.GetReals("motion.motionState.objectRadius")[0]);

        body.SetString("name", "Renamed");
        HkxCaches.Refresh(file, HkxFile.Parse(bytes));
        Assert.Equal(0.8263601f, body.GetReals("motion.motionState.objectRadius")[0], 3);
    }

    private static HkxFile? Read(string fixture) => Fixture.Read(fixture) is { } bytes ? HkxFile.Parse(bytes) : null;
}
