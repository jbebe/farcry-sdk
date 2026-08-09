using JackAll.Tools.Mgb;

namespace JackAll.Tests;

/// <summary>
/// The correctness gate for the whole <c>.mgb</c> codec.
/// </summary>
/// <remarks>
/// <c>Write(Read(x)) == x</c> over real files proves the reader and the writer simultaneously: any
/// field whose width, order or conditionality is wrong either fails to read or fails to reproduce.
/// It matters far more than a "parses without throwing" check, because this format has no lengths,
/// no alignment and no sentinels - a wrong field silently reinterprets everything after it, and a
/// broken decoder can still land on a plausible-looking offset by coincidence.
///
/// The corpus lives in <c>tmp/menu/</c>, which is gitignored, so these tests skip rather than fail
/// when it is absent - a fresh checkout should not report failures for data it was never given.
/// </remarks>
public sealed class MgbRoundTripTests
{
    private static readonly string CorpusDirectory =
        Path.Combine(TestSupport.RepositoryRoot, "tmp", "menu");

    public static TheoryData<string> CorpusFiles()
    {
        var data = new TheoryData<string>();
        if (Directory.Exists(CorpusDirectory))
        {
            foreach (string path in Directory.EnumerateFiles(CorpusDirectory, "*.mgb").Order())
            {
                data.Add(Path.GetFileName(path));
            }
        }
        if (data.Count == 0)
        {
            data.Add(string.Empty); // keeps xUnit from erroring on an empty theory
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Reserialises_every_corpus_file_byte_for_byte(string fileName)
    {
        if (fileName.Length == 0)
        {
            return; // corpus not present in this checkout
        }

        byte[] original = File.ReadAllBytes(Path.Combine(CorpusDirectory, fileName));
        MgbPackage package = MgbPackage.Read(original);
        byte[] rewritten = package.Write();

        Assert.Equal(original.Length, rewritten.Length);
        int firstDifference = FirstDifference(original, rewritten);
        if (firstDifference >= 0)
        {
            Assert.Fail(
                $"{fileName}: first byte difference at offset 0x{firstDifference:X} " +
                $"(original 0x{original[firstDifference]:X2}, rewritten 0x{rewritten[firstDifference]:X2})");
        }
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Reads_every_area_and_element_of_every_corpus_file(string fileName)
    {
        if (fileName.Length == 0)
        {
            return;
        }

        byte[] original = File.ReadAllBytes(Path.Combine(CorpusDirectory, fileName));
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
        string path = Path.Combine(CorpusDirectory, "controller.mgb");
        if (!File.Exists(path))
        {
            return;
        }

        byte[] original = File.ReadAllBytes(path);
        MgbPackage package = MgbPackage.Read(original);

        MgbArea area = package.Areas[0];
        uint before = area.FrameRate;
        area.FrameRate = before + 7;

        byte[] edited = package.Write();
        Assert.Equal(original.Length, edited.Length); // a u32 in place changes no sizes

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
        Assert.InRange(differing, 1, 4); // the one u32 field, nothing more
    }

    /// <summary>Declaring a class the file doesn't already list must append a type-table entry and
    /// shift the body - something the old byte-splicing editor could not do at all.</summary>
    [Fact]
    public void Declaring_a_new_class_grows_the_type_table_and_still_round_trips()
    {
        string path = Path.Combine(CorpusDirectory, "controller.mgb");
        if (!File.Exists(path))
        {
            return;
        }

        MgbPackage package = MgbPackage.Read(File.ReadAllBytes(path));
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
        Assert.Equal(4, grown.Length - new FileInfo(path).Length); // one extra u32 in the table
    }

    private static int FirstDifference(byte[] a, byte[] b)
    {
        int shared = Math.Min(a.Length, b.Length);
        for (int i = 0; i < shared; i++)
        {
            if (a[i] != b[i])
            {
                return i;
            }
        }
        return a.Length == b.Length ? -1 : shared;
    }
}
