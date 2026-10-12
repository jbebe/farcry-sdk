using System.Numerics;
using JackAll.Tools.Hkx;

namespace JackAll.Tests;

/// <summary>An <c>.hkx</c> edit undoes and redoes as a whole, tracks unsaved changes, and saves with its caches in line.</summary>
public sealed class HkxEditSessionTests
{
    // One body: a MOPP over a list of six boxes; the seat, child 4, is the topmost.
    private const string Chair = "Hkx/chairbar01.hkx";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixture_was_actually_found() => Fixture.AssertPresent(Chair);

    [Fact]
    public void An_undone_edit_leaves_the_file_as_opened_and_a_redone_one_as_edited()
    {
        if (Fixture.Read(Chair) is not { } bytes)
        {
            return;
        }

        var session = new HkxEditSession(bytes);
        var reparsed = new List<bool>();
        session.Changed += reparsed.Add;

        session.Edit("Raise the seat", () => Raise(session.File, 2));
        byte[] edited = session.File.Write();
        Assert.True(session.IsDirty);

        session.Undo();
        Assert.False(session.IsDirty);
        Assert.Equal(bytes, session.File.Write());

        session.Redo();
        Assert.True(session.IsDirty);
        Assert.Equal(edited, session.File.Write());
        Assert.Equal([false, true, true], reparsed);
    }

    [Fact]
    public void Edits_under_one_key_in_quick_succession_undo_as_one()
    {
        if (Fixture.Read(Chair) is not { } bytes)
        {
            return;
        }

        var session = new HkxEditSession(bytes);
        session.Edit("Raise the seat", () => Raise(session.File, 1), mergeKey: "seat");
        session.Edit("Raise the seat", () => Raise(session.File, 1), mergeKey: "seat");

        session.Undo();

        Assert.False(session.History.CanUndo);
        Assert.Equal(bytes, session.File.Write());
    }

    [Fact]
    public void A_save_rebuilds_the_mopp_and_saving_the_seat_back_in_place_restores_every_byte()
    {
        if (Fixture.Read(Chair) is not { } bytes)
        {
            return;
        }

        var session = new HkxEditSession(bytes);
        float[] start = Seat(session.File).GetReals("translation");
        session.Edit("Raise the seat", () => Raise(session.File, 2));
        byte[] saved = [];
        Assert.True(session.Save(b => (saved = b) is not null));
        Assert.False(session.IsDirty);

        HkxObject mopp = HkxFile.Parse(saved).Objects().First(o => o.Class.Name == "hkpMoppBvTreeShape");
        (Vector4 info, byte[] code) = HkxMopp.Code(mopp);
        float[] at = mopp.GetPointer("child.childShape")!.GetPointer("childInfo[4].shape")!.GetReals("translation");
        Vector3 seat = new(at[0], at[1], at[2]);
        Assert.Equal([4u], HkxMopp.Query(info, code, seat - new Vector3(0.01f), seat + new Vector3(0.01f)));

        session.Edit("Put the seat back", () => Seat(session.File).SetReals("translation", start));
        session.Save(b => (saved = b) is not null);
        Assert.Equal(bytes, saved);
    }

    [Fact]
    public void A_save_the_store_turns_down_leaves_the_edits_unsaved()
    {
        if (Fixture.Read(Chair) is not { } bytes)
        {
            return;
        }

        var session = new HkxEditSession(bytes);
        session.Edit("Raise the seat", () => Raise(session.File, 2));

        Assert.False(session.Save(_ => false));
        Assert.True(session.IsDirty);
    }

    private static void Raise(HkxFile file, float by)
    {
        float[] at = Seat(file).GetReals("translation");
        Seat(file).SetReals("translation", at[0], at[1], at[2] + by, at[3]);
    }

    private static HkxObject Seat(HkxFile file) => file.Objects().First(o => o.Class.Name == "hkpListShape").GetPointer("childInfo[4].shape")!;
}
