using JackAll.Tools.Format.Mgb;

namespace JackAll.Core.Tests;

/// <summary>
/// How the property grid reads the two value shapes the wire format doesn't describe: a packed
/// colour word, and an integer the XML loader authors by name.
/// </summary>
public sealed class MgbColorTests
{
    [Fact]
    public void Splits_a_word_into_alpha_red_green_blue_from_the_top_down()
    {
        const uint value = 0x12345678;
        Assert.Equal(0x12, MgbColor.Alpha(value));
        Assert.Equal(0x34, MgbColor.Red(value));
        Assert.Equal(0x56, MgbColor.Green(value));
        Assert.Equal(0x78, MgbColor.Blue(value));
        Assert.Equal(value, MgbColor.Pack(0x12, 0x34, 0x56, 0x78));
        Assert.Equal("18 52 86 120", MgbColor.Describe(value));
    }

    /// <summary>The values that dominate the shipped corpus, read the way the format notes now say
    /// they should be: opaque white and fully transparent white - the two ends of a fade. Reading the
    /// same words as RGBA would make the second one opaque cyan.</summary>
    [Theory]
    [InlineData(0xFFFFFFFFu, 255, 255, 255, 255)]
    [InlineData(0x00FFFFFFu, 0, 255, 255, 255)]
    [InlineData(0xFF000000u, 255, 0, 0, 0)]
    [InlineData(0xFFA5BDC5u, 255, 0xA5, 0xBD, 0xC5)]
    public void Reads_the_corpus_favourites_as_argb(uint value, byte a, byte r, byte g, byte b)
    {
        Assert.Equal(a, MgbColor.Alpha(value));
        Assert.Equal(r, MgbColor.Red(value));
        Assert.Equal(g, MgbColor.Green(value));
        Assert.Equal(b, MgbColor.Blue(value));
        Assert.Equal(value, MgbColor.Pack(a, r, g, b));
    }
}

/// <summary>
/// <see cref="MgbEnums"/> against the tag table it was read out of.
/// </summary>
/// <remarks>
/// The names are transcribed from <c>ms_tagTable</c> in the shipped binary, so what's worth guarding
/// is the value↔name pairing itself: slipping one entry would silently retitle everything after it,
/// and nothing else in the build would notice.
/// </remarks>
public sealed class MgbEnumsTests
{
    public static TheoryData<MgbEnum, uint, string> KnownPairs() => new()
    {
        { MgbEnums.BlendingMode, 0, "Normal" },
        { MgbEnums.BlendingMode, 15, "Lighten 2X" },
        { MgbEnums.BlendingMode, 17, "Add" },
        { MgbEnums.BlendingMode, 21, "Modulate" },
        { MgbEnums.BlendingMode, 26, "Custom4" },
        { MgbEnums.Interpolation, 0, "None" },
        { MgbEnums.Interpolation, 6, "CircleDecel" },
        { MgbEnums.AlignmentX, 0, "LEFT" },
        { MgbEnums.AlignmentX, 3, "JUSTIFY" },
        { MgbEnums.AlignmentY, 2, "BOTTOM" },
        { MgbEnums.HeaderFooterPos, 1, "Left and Right" },
        { MgbEnums.Orientation, 1, "Vertical" },
        { MgbEnums.AddressingMode, 3, "Border" },
        { MgbEnums.MaskMode, 3, "USEMASK_INVERTED" },
    };

    [Theory]
    [MemberData(nameof(KnownPairs))]
    public void Maps_each_value_to_the_name_the_tag_table_gives_it(MgbEnum set, uint value, string name)
    {
        Assert.Equal(name, set.NameFor(value));
        Assert.True(set.TryValueFor(name, out uint roundTripped));
        Assert.Equal(value, roundTripped);
    }

    public static TheoryData<MgbEnum, int> Sizes() => new()
    {
        { MgbEnums.Interpolation, 7 }, { MgbEnums.AlignmentX, 4 }, { MgbEnums.AlignmentY, 3 },
        { MgbEnums.HeaderFooterPos, 2 }, { MgbEnums.Orientation, 2 }, { MgbEnums.BlendingMode, 27 },
        { MgbEnums.AddressingMode, 4 }, { MgbEnums.MaskMode, 4 },
    };

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Covers_its_whole_tag_group_with_distinct_names(MgbEnum set, int count)
    {
        Assert.Equal(count, set.Names.Count);
        Assert.All(set.Names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(count, set.Names.Distinct().Count());
    }

    /// <summary>A file may hold a value the table doesn't name - the engine's own lookup returns a
    /// not-found marker for it - so the editor has to keep showing the raw number rather than
    /// blanking the field.</summary>
    [Fact]
    public void Reports_no_name_for_a_value_outside_the_table()
    {
        Assert.Null(MgbEnums.BlendingMode.NameFor(99));
        Assert.Null(MgbEnums.AlignmentX.NameFor(4));
        Assert.False(MgbEnums.MaskMode.TryValueFor("NOSUCHMODE", out _));
    }

    /// <summary><c>Util::GetType</c> compares with <c>strcasecmp</c>.</summary>
    [Fact]
    public void Matches_a_name_regardless_of_case()
    {
        Assert.True(MgbEnums.BlendingMode.TryValueFor("modulate", out uint value));
        Assert.Equal(21u, value);
    }
}
