namespace JackAll.Tests;

/// <summary>The retail materials under <c>Fixtures\Xbm*\</c> the material tests read.</summary>
internal static class XbmFixtures
{
    // One of the AK-47's materials, opaque and over wood.
    public const string Wood = "Xbm/jpcormier-m-2007100950589039.xbm";

    // Another of the AK-47's materials, over metal.
    public const string Metal = "Xbm/sdore2-m-2007073066669286.xbm";

    // An alpha-blended material.
    public const string Blended = "XbmAlpha/blended.xbm";

    // An alpha-tested material on ordinary, non-billboard geometry.
    public const string Masked = "XbmAlpha/masked.xbm";

    // The one shipped material that repeats a key inside a section.
    public const string RepeatedKey = "XbmRepeat/repeated_key.xbm";
}
