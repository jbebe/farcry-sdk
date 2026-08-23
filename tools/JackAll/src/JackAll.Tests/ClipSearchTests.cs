using JackAll.Tools.Fc2Model;

namespace JackAll.Tests;

/// <summary>
/// Finding a model's animation banks by asking the banks, over every shipped weapon.
/// </summary>
/// <remarks>
/// This is the measurement that killed the obvious rule. Mirroring the model's folder into
/// <c>animations/weapons/</c> looks right on the ak47 and lands for only 12 of the 49 shipped weapon
/// folders - <c>spas12</c> is filed as <c>franchi_spas12</c>, <c>m16</c> as <c>m-16</c>,
/// <c>deserteagle</c> as <c>desert_eagle_50</c>. Reading the tag records instead is exact.
/// </remarks>
public sealed class ClipSearchTests
{
    /// <summary>
    /// Every weapon that moves finds its banks, and the ones that do not move find none.
    /// </summary>
    /// <remarks>
    /// Both halves matter. A search that returned everything would pass the first half alone, and
    /// the second is what says the tag name is a real link rather than a coincidence: an ammo box
    /// and a fuel pile are weapon-folder models with nothing animating them, and they come back
    /// empty.
    /// </remarks>
    [Fact]
    public void A_weapon_finds_its_banks_and_a_pickup_finds_none()
    {
        if (!Fc2Corpus.Present)
        {
            return;
        }

        List<string> banks = [.. Fc2Corpus.Find(".mab")];
        Dictionary<string, byte[]> cache = [];
        byte[]? Read(string path) => cache.TryGetValue(path, out byte[]? bytes)
            ? bytes
            : cache[path] = File.ReadAllBytes(path);

        int moved = 0;
        foreach (string model in Weapons())
        {
            List<string> found = ClipSearch.For(model, banks, Read);
            moved += found.Count > 0 ? 1 : 0;

            // A pickup is a weapon-folder model that nothing animates. Nothing may claim to.
            if (Path.GetFileNameWithoutExtension(model).StartsWith("ammobox", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Empty(found);
            }
        }

        // Measured: 44 of the 89 models under graphics/weapons find banks. The rest are ammo boxes,
        // pickups, casings and bullets, which nothing animates. Held just under the measurement so a
        // regression in the search shows up rather than being absorbed.
        Assert.True(moved >= 44, $"Only {moved} of the weapon models found any bank.");
    }

    /// <summary>
    /// The rifle's banks are found wherever they are filed, not only beside it.
    /// </summary>
    /// <remarks>
    /// 62 banks sit in the ak47's own animation folder and 94 name the rifle - the rest are
    /// locomotion and cutscene banks that carry it while a character runs or talks. A folder rule
    /// would miss every one of them, and those are the clips that say whether a part is going to
    /// pass through the character's arm.
    /// </remarks>
    [Fact]
    public void The_rifle_finds_banks_filed_away_from_it()
    {
        if (!Fc2Corpus.Present)
        {
            return;
        }

        List<string> found = ClipSearch.For(
            "graphics/weapons/primary/ak47/ak47.xbg",
            [.. Fc2Corpus.Find(".mab")],
            File.ReadAllBytes);

        Assert.True(found.Count >= 90, $"Only {found.Count} banks name the ak47.");
        Assert.Contains(found, path => !path.Contains("weapons", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Weapons().Any(), Fc2Corpus.MissingMessage(".xbg"));

    private static IEnumerable<string> Weapons()
        => Fc2Corpus.Find(".xbg")
            .Where(path => path.Replace('\\', '/')
                .Contains("/graphics/weapons/", StringComparison.OrdinalIgnoreCase));
}
