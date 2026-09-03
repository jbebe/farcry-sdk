using System.Xml.Linq;
using System.Text;
using JackAll.Core.Format;
using JackAll.Core.Format.Rml;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// The end-to-end check on a real mod: the VSS Vintorez's ten renamed strings, stated in one 1.3 KB
/// patch document, rebuild the table it used to ship as a 946 KB whole-file override.
/// </summary>
/// <remarks>
/// A real mod against a known-good target is worth more than any synthetic case, and it needs no
/// game launch: if the strings match, the game cannot tell the difference.
/// </remarks>
[Trait("Category", "RequiresFixture")]
public sealed class StringTableVssMigrationTests
{
    private static string Vanilla =>
        Path.Combine(Fc2Corpus.Root, "patch", "languages", "english", "oasisstrings.rml");

    private static string ModLayer =>
        Path.Combine(TestSupport.RepositoryRoot, "mods", "vss-vintorez", "layer");

    private static string PatchDocument =>
        Path.Combine(ModLayer, "mods", "languages", "english", OasisStringsPatch.FileName);

    /// <summary>
    /// Every string the mod renames, and nothing else. The crate name appears twice - once in the
    /// bazaar and once more under <c>Tutorial</c> - which is why ten strings live in five sections.
    /// </summary>
    private static readonly (string Section, string Key, string Value)[] Renamed =
    [
        ("Challenges", "dartrifle", "VSS Vintorez"),
        ("Items", "dart_rifle", "VSS Vintorez"),
        ("StatisticService", "Kills_Dart_Rifle", "VSS Vintorez Kills"),
        ("StatisticService", "Executions_Dart_Rifle", "VSS Vintorez Executions"),
        ("StatisticService", "Headshots_Dart_Rifle", "VSS Vintorez Headshots"),
        ("Tutorial", "WEAPONBAZAAR_DART_RIFLECRATE_NAME", "VSS Vintorez"),
        ("WeaponBazaar", "WEAPONBAZAAR_DART_RIFLECRATE_NAME", "VSS Vintorez"),
        ("WeaponBazaar", "WEAPONBAZAAR_DART_RIFLE_OPERATION_MANUAL_NAME", "VSS Vintorez"),
        ("WeaponBazaar", "WEAPONBAZAAR_DART_RIFLE_REPAIR_MANUAL_NAME", "VSS Vintorez"),
    ];

    /// <summary>
    /// The mod's one patch document, taken through the real layer scan rather than parsed here: this
    /// is the path a build takes, so it gates the expansion into per-string fragments too.
    /// </summary>
    [Fact]
    public void The_vss_fragments_rename_only_the_strings_they_mean_to()
    {
        if (!File.Exists(Vanilla) || !File.Exists(PatchDocument))
        {
            return;
        }

        byte[] vanilla = File.ReadAllBytes(Vanilla);
        var layer = new FolderModLayer(ModLayer, "vss-vintorez");

        IReadOnlyList<FragmentOverride> staged =
            layer.FragmentOverrides[NameHash.Compute(@"languages\english\oasisstrings.rml")];
        Assert.Equal(Renamed.Length + 1, staged.Count);

        byte[] built = StringTableContainerSplitter.Instance.Apply(
            vanilla,
            staged.ToDictionary(f => f.FragmentId, f => Encoding.UTF8.GetString(layer.Read(f.EntryHash))));

        Dictionary<(string, string), string> before = Strings(vanilla);
        Dictionary<(string, string), string> after = Strings(built);

        // Same shape: nothing added, removed or reordered - a rename rewrites values in place.
        Assert.Equal(before.Count, after.Count);

        (string Section, string Key)[] changed =
            [.. after.Where(e => before[e.Key] != e.Value).Select(e => e.Key).Order()];

        // COMPUTER_ADVERT_01 names the weapon in running prose, so it is a rename too - but its text
        // is a paragraph rather than a name, and the exact wording is the mod's to choose.
        Assert.Equal(
            [.. Renamed.Select(r => (r.Section, r.Key)).Append(("Tutorial", "COMPUTER_ADVERT_01")).Order()],
            changed);

        foreach ((string section, string key, string value) in Renamed)
        {
            Assert.Equal(value, after[(section, key)]);
        }

        Assert.DoesNotContain("Dart Rifle", after[("Tutorial", "COMPUTER_ADVERT_01")]);
    }

    /// <summary>Every string in a table, keyed by the section that holds it.</summary>
    private static Dictionary<(string Section, string Key), string> Strings(byte[] table)
    {
        var strings = new Dictionary<(string, string), string>();
        foreach (XElement section in RmlDocument.Deserialize(table).Elements("section"))
        {
            string name = (string)section.Attribute("name")!;
            foreach (XElement entry in section.Elements("string"))
            {
                strings[(name, (string)entry.Attribute("enum")!)] = (string?)entry.Attribute("value") ?? string.Empty;
            }
        }
        return strings;
    }
}
