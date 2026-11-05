using System.Text;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Tools.SoundLabels;
using JackAll.Tools.Spk;

namespace JackAll.Tests;

public class SoundLabelTests
{
    private const string EntityLibrary = @"Fcb\world1_entitylibrary.fcb";
    private const string ReloadClip = @"Mab\3rdge_uppb_reload_nodir_prak4_i1.mab";
    private const string Barks = @"Bark\scripts\game\barkdata\1431575.bank";
    private const string ShotList = @"Spk\004bf5ea_5c852949.spk";
    private const string ShotLayer = @"Spk\004bf5e9.spk";
    private const string BarkSounds = @"Spk\bark_1820776.spk";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
        => Fixture.AssertPresent(EntityLibrary, ReloadClip, Barks, ShotList, ShotLayer, BarkSounds);

    [Theory]
    [InlineData("0x004B27D4", 0x004B27D4u)]
    [InlineData("0x004b27d4", 0x004B27D4u)]
    [InlineData("0xFFFFFFFF", null)]
    [InlineData("0x00000000", null)]
    [InlineData("Victory!", null)]
    public void A_sound_id_is_ox_and_eight_hex_digits(string text, uint? id) => Assert.Equal(id, SoundUses.ParseId(text));

    [Theory]
    [InlineData("sndSingleBulletShot", "single bullet shot")]
    [InlineData("sndPickupGrabSound_1st", "pickup grab, 1st person")]
    [InlineData("sndMalariaStartSoundID", "malaria start")]
    [InlineData("dissndStart", "start")]
    [InlineData("sndChangePitch", "change pitch")]
    [InlineData("sndchMultitrackChannel", "multitrack channel")]
    [InlineData("sndId", "")]
    public void A_field_name_reads_as_words(string field, string words) => Assert.Equal(words, SoundCategories.Field(field));

    [Theory]
    [InlineData(@"graphics\characters\_common\animations\weapons\primary\m-16\1stge_uppb_reload_+000fw_prm16_i1.mab", "m-16", "reload, 1st person")]
    [InlineData(@"graphics\characters\_common\animations\weapons\dlc\crossbow\dlc1_3rdge_fulb_reload_+000fw_spcrb_i1.mab", "crossbow", "reload, 3rd person")]
    [InlineData(@"graphics\characters\_common\animations\choreographed_scene\story_mission\sm04\se01\sm04_se01_player_standup.mab", "se01", "sm04_se01_player_standup")]
    public void An_animation_path_names_its_subject_and_action(string path, string subject, string action)
        => Assert.Equal((subject, action), SoundCategories.Animation(path));

    [Theory]
    [InlineData("weaponproperties.primary.ak47 | sounds | sndemptyweapon", "weapon")]
    [InlineData("vehicle.land.buggy | sound | sndsmallcollision", "vehicle")]
    [InlineData(@"engine\gamemodes\gamemodesconfig.xml | gamemodes/partnertapes/tape | startsoundid", "dialogue")]
    [InlineData(@"databases\materialimpacts\materialimpacts.xml | materialimpactlibrary/materialimpact | sndswitchsound", "impact")]
    [InlineData("player.maincharacter.pawnplayer | biofeedback/malaria | sndmalariastartsoundid", "effect")]
    [InlineData("domino\\user\\a1sm01_townescape.a1sm01_mission.lua | playsound | soundid", "other")]
    public void A_use_gets_the_first_matching_category(string context, string category)
        => Assert.Equal(category, SoundCategories.Of(context));

    [Fact]
    public void An_entity_library_names_a_weapon_sound_by_its_archetype_and_field()
    {
        if (Fixture.Read(EntityLibrary) is not { } bytes) return;

        SoundUse[] uses = [.. SoundUses.InFcb(bytes, "entitylibrary.fcb", BundledAssets.LoadFcbClasses())];
        Assert.Contains(uses, u => u is { Id: 0x004493A3, Category: "weapon", Subject: "AK47", Action: "empty weapon" });
    }

    [Fact]
    public void A_reload_clip_names_the_sounds_its_events_play()
    {
        if (Fixture.Read(ReloadClip) is not { } bytes) return;

        SoundUse[] uses = [.. SoundUses.InAnimation(bytes,
            @"graphics\characters\_common\animations\weapons\primary\ak47\3rdge_uppb_reload_nodir_prak4_i1.mab")];
        Assert.Contains(uses, u => u.Id == 0x004B28DE);
        Assert.All(uses, u => Assert.Equal(("weapon", "ak47", "reload, 3rd person"), (u.Category, u.Subject, u.Action)));
    }

    [Fact]
    public void A_bark_bank_names_each_line_with_its_speaker()
    {
        if (Fixture.Read(Barks) is not { } bytes) return;

        SoundUse[] uses = [.. SoundUses.InBarks(bytes, "1431575.bank")];
        Assert.NotEmpty(uses);
        Assert.All(uses, u => Assert.Equal(SoundCategories.Dialogue, u.Category));
        Assert.Contains(uses, u => u is { Action: "GREET", Speaker: not null });
    }

    [Fact]
    public void A_script_names_its_box_and_a_buddy_field_its_speaker()
    {
        byte[] lua = Encoding.UTF8.GetBytes("""
            self[3] = cbox:CreateBox("Domino/System/PhoneCall.lua");
            self[3].MartyStart = "0x004b5c73";
            Boxes[PathID("Domino/System/PlayMusic.lua")].SoundId = "0x004b642e";
            """);

        SoundUse[] uses = [.. SoundUses.InScript(lua, @"domino\user\a1lm04_directspear.a1lm04_mission.lua")];
        Assert.Equal(
            [
                new SoundUse(0x004B5C73, SoundCategories.Dialogue, "a1lm04 directspear, a1lm04 mission", "PhoneCall marty start",
                    @"domino\user\a1lm04_directspear.a1lm04_mission.lua", "Marty"),
                new SoundUse(0x004B642E, "music", "a1lm04 directspear, a1lm04 mission", "PlayMusic",
                    @"domino\user\a1lm04_directspear.a1lm04_mission.lua"),
            ],
            uses);
    }

    [Fact]
    public void A_bank_nothing_names_takes_the_uses_of_the_event_that_plays_it()
    {
        if (Fixture.Read(ShotList) is not { } list || Fixture.Read(ShotLayer) is not { } layer) return;

        var banks = new Dictionary<string, SpkPackage>
        {
            [@"soundbinary\004bf5ea.spk"] = SpkPackage.Parse(list),
            [@"soundbinary\004bf5e9.spk"] = SpkPackage.Parse(layer),
        };
        var shot = new SoundUse(0x004BF5EA, "weapon", "AK47", "single bullet shot", "entitylibrary.fcb");

        Dictionary<string, SoundLabel> labels = SoundLabeler.Label(banks, [shot], _ => null).ToDictionary(l => l.Path);
        Assert.Equal(("weapon", "single bullet shot (AK47)", SoundLabeler.FromUsage), Parts(labels[@"soundbinary\004bf5ea.spk"]));
        Assert.Equal(("weapon", "single bullet shot (AK47)", SoundLabeler.FromLinks), Parts(labels[@"soundbinary\004bf5e9.spk"]));
    }

    [Fact]
    public void A_localized_bank_is_dialogue_with_its_speaker_and_transcript_when_known()
    {
        if (Fixture.Read(BarkSounds) is not { } bytes) return;

        var banks = new Dictionary<string, SpkPackage> { [@"scripts\game\barkdata\loc\1820776.spk"] = SpkPackage.Parse(bytes) };
        var line = new SoundUse(0x004DF52C, SoundCategories.Dialogue, "", "GREET", "1820776.bank", "Reuben");

        SoundLabel bare = Assert.Single(SoundLabeler.Label(banks, [], _ => null));
        Assert.Equal((SoundCategories.Dialogue, "", SoundLabeler.FromFolder), Parts(bare));

        SoundLabel spoken = Assert.Single(SoundLabeler.Label(banks, [line], id => id == 0x004DF52C ? "Hello." : null));
        Assert.Equal("Reuben: \"Hello.\"", spoken.Detail);
    }

    [Fact]
    public void The_label_list_reads_back_what_it_writes()
    {
        var label = new SoundLabel(@"soundbinary\004bf5ea.spk", "weapon", "single bullet shot (AK47)", "usage", "entitylibrary.fcb");

        SoundLabelTable read = SoundLabelTable.Parse(new SoundLabelTable([label]).Write());
        Assert.Equal(label, read.Find(NameHash.Compute(label.Path)));
    }

    private static (string, string, string) Parts(SoundLabel label) => (label.Category, label.Detail, label.Source);
}
