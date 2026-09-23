using JackAll.Tools.Mgb;

namespace JackAll.Tests;

/// <summary>
/// Round-trips the <c>.mgb</c> codec over shipped packages.
/// </summary>
/// <remarks>
/// <c>Write(Read(x)) == x</c> over real files proves the reader and the writer simultaneously: any
/// field whose width, order or conditionality is wrong either fails to read or fails to reproduce.
/// It matters far more than a "parses without throwing" check, because this format has no lengths,
/// no alignment and no sentinels - a wrong field silently reinterprets everything after it, and a
/// broken decoder can still land on a plausible-looking offset by coincidence.
/// </remarks>
public sealed class MgbRoundTripTests
{
    /// <summary>A full menu page, a small one, and a fonts-only package whose one page is empty.</summary>
    public static TheoryData<string> Packages => new() { "Mgb/options.mgb", "Mgb/controller.mgb", "Mgb/fonts.mgb" };

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
        => Fixture.AssertPresent("Mgb/options.mgb", "Mgb/controller.mgb", "Mgb/fonts.mgb");

    [Theory]
    [MemberData(nameof(Packages))]
    public void Reserialises_a_package_byte_for_byte(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        byte[] rewritten = MgbPackage.Read(original).Write();

        Fixture.AssertSameBytes(path, original, rewritten);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void Reads_every_area_and_element_of_a_package(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        MgbPackage package = MgbPackage.Read(original);

        // Every area and element resolved to a real class - the reader throws otherwise, but
        // asserting it here documents that "read succeeded" means the tree is genuinely typed and
        // not a bag of opaque blobs.
        foreach (MgbArea area in package.Areas)
        {
            Assert.Contains(area.TypeName, MgbSchema.AreaTypes);
            foreach (MgbElement element in area.Elements)
            {
                Assert.True(MgbSchema.IsWidgetType(element.WidgetTypeName));
                Assert.Equal(element.WidgetTypeName, element.Widget.TypeName);
                foreach (MgbKeyframe keyframe in element.Keyframes)
                {
                    Assert.Equal(element.StateTypeName, keyframe.State.TypeName);
                }
            }
        }
    }

    /// <summary>A changed value must survive a write/read cycle, and must not disturb anything
    /// else: the only bytes that differ are the ones the edit owns.</summary>
    [Fact]
    public void An_edited_value_survives_a_round_trip_without_moving_anything_else()
    {
        if (Fixture.Read("Mgb/controller.mgb") is not { } original) return;

        MgbPackage package = MgbPackage.Read(original);

        MgbArea area = package.Areas[0];
        uint before = area.FrameRate;
        area.FrameRate = before + 7;

        byte[] edited = package.Write();
        // A u32 in place changes no sizes.
        Assert.Equal(original.Length, edited.Length);

        MgbPackage reread = MgbPackage.Read(edited);
        Assert.Equal(before + 7, reread.Areas[0].FrameRate);

        int differing = 0;
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] != edited[i])
            {
                differing++;
            }
        }
        // The one u32 field, nothing more.
        Assert.InRange(differing, 1, 4);
    }

    /// <summary>Declaring a class the file doesn't already list must append a type-table entry and
    /// shift the body - something the old byte-splicing editor could not do at all.</summary>
    [Fact]
    public void Declaring_a_new_class_grows_the_type_table_and_still_round_trips()
    {
        if (Fixture.Read("Mgb/controller.mgb") is not { } original) return;

        MgbPackage package = MgbPackage.Read(original);
        int before = package.Types.RawIds.Count;

        // Shipped files carry a build-wide superset of the type table, so most classes are already
        // declared. Asking for one of those must reuse its slot rather than add a duplicate.
        string declared = "Button";
        byte existingSlot = package.Types.SlotForName(declared);
        Assert.Equal(before, package.Types.RawIds.Count);
        Assert.Equal(declared, package.Types.NameForSlot(existingSlot));

        // Find a class this file genuinely doesn't declare, to exercise the append path.
        string? absent = MgbTypeTable.KnownClassNames
            .FirstOrDefault(n => !package.Types.RawIds.Contains(MgbTypeTable.Hash(n)));
        Assert.NotNull(absent);

        byte newSlot = package.Types.SlotForName(absent);
        Assert.Equal(before + 1, package.Types.RawIds.Count);
        Assert.Equal(before + 1, newSlot);
        Assert.Equal(absent, package.Types.NameForSlot(newSlot));

        // Growing the table shifts every body offset after it, which is exactly what the old
        // byte-splicing editor could not survive - full reserialisation makes it routine.
        byte[] grown = package.Write();
        MgbPackage reread = MgbPackage.Read(grown);
        Assert.Equal(absent, reread.Types.NameForSlot(newSlot));
        Assert.Equal(package.Areas.Count, reread.Areas.Count);
        // One extra u32 in the table.
        Assert.Equal(4, grown.Length - original.Length);
    }
}
