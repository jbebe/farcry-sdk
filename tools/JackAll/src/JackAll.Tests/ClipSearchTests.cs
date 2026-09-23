using JackAll.Tools.Fc2Model;

namespace JackAll.Tests;

/// <summary>
/// Finding a model's animation banks by asking the banks.
/// </summary>
/// <remarks>
/// Mirroring the model's folder into <c>animations/weapons/</c> looks right on the ak47 but is not a
/// rule - <c>deserteagle</c>'s banks are filed under <c>desert_eagle_50</c>, and the rifle is carried
/// through cutscene banks filed nowhere near it. Reading the tag records instead is exact.
/// </remarks>
public sealed class ClipSearchTests
{
    private const string Reload = @"graphics\characters\_common\animations\weapons\primary\ak47\3rdge_uppb_reload_nodir_prak4_i1.mab";

    // A cutscene bank that carries the rifle, filed away from every weapon folder.
    private const string Cutscene = @"graphics\characters\_common\animations\choreographed_scene\story_mission\sm10\se02\sm10_se02_guard02_wait01.mab";

    // Filed under desert_eagle_50, which no model is named.
    private const string DesertEagle = @"graphics\characters\_common\animations\weapons\secondary\desert_eagle_50\1stge_uppb_shootingcycle_+000fw_sedea_i1.mab";

    // Each bank's game path, and the fixture holding it.
    private static readonly Dictionary<string, string> Banks = new()
    {
        [Reload] = MabFixtures.Reload,
        [Cutscene] = MabFixtures.Cutscene,
        [DesertEagle] = MabFixtures.Tie,
    };

    [Fact]
    public void The_rifle_finds_banks_filed_away_from_it()
    {
        if (!Fixture.Present([.. Banks.Values]))
        {
            return;
        }

        Assert.Equal([Reload, Cutscene], Search(@"graphics\weapons\primary\ak47\ak47.xbg"));
    }

    [Fact]
    public void A_weapon_finds_its_banks_under_a_folder_named_otherwise()
    {
        if (!Fixture.Present([.. Banks.Values]))
        {
            return;
        }

        Assert.Equal([DesertEagle], Search(@"graphics\weapons\secondary\deserteagle\deserteagle.xbg"));
    }

    // A pickup is a weapon-folder model that nothing animates, so no bank may claim to.
    [Fact]
    public void A_pickup_finds_none()
    {
        if (!Fixture.Present([.. Banks.Values]))
        {
            return;
        }

        Assert.Empty(Search(@"graphics\weapons\ammunitions\ammobox_pistol.xbg"));
    }

    private static List<string> Search(string model) => ClipSearch.For(model, Banks.Keys, path => Fixture.Read(Banks[path]));
}
