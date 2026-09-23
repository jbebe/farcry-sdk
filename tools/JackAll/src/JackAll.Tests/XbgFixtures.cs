namespace JackAll.Tests;

/// <summary>The retail meshes under <c>Fixtures\Xbg\</c> the mesh tests read.</summary>
internal static class XbgFixtures
{
    // Skinned vertices, with its material embedded rather than named.
    public const string Bat = "Xbg/bat.xbg";

    // Static vertices: a destructible fence section with damage states and the rare ADKI chunk.
    public const string Fence = "Xbg/fencedusttowndest_c_temp_part02.xbg";

    // The grass vertex format: a position, one UV set and a colour, with no normal.
    public const string Grass = "Xbg/artemesiagrassa.xbg";

    // A rifle: five LODs, eleven parts, and parts missing from some LODs.
    public const string Ak47 = "Xbg/ak47.xbg";

    // A skinned character.
    public const string Character = "Xbg/andrehyppolite.xbg";

    // A static prop with asymmetric V bounds.
    public const string Prop = "Xbg/chairbar01.xbg";

    // A vehicle whose four wheels are each modelled around their own pivot.
    public const string Buggy = "Xbg/buggy.xbg";

    // Body and roof each ship as STATE01 and STATE02.
    public const string SwampBoat = "Xbg/swampboat.xbg";

    // A wardrobe whose clothes carry two LOD suffixes, P_WC_LB_JEANS03_LOD00_LOD0.
    public const string FemaleCivilianKit = "Xbg/female_civilian_kit.xbg";

    /// <summary>One mesh for each of the three vertex formats that ship: skinned, static and grass.</summary>
    public static TheoryData<string> Formats => new() { Bat, Fence, Grass };
}
