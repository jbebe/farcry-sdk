namespace JackAll.Tools.Format.Mgb;

/// <summary>
/// A packed <c>.mgb</c> colour word - <c>0xAARRGGBB</c>, alpha in the high byte.
/// </summary>
/// <remarks>
/// Two independent findings pin this down.
///
/// The packing comes from <c>magma::LoadVisitor::ReadState</c> (<c>0x0a066400</c>), which parses
/// <c>STATECOLOR</c>'s <c>%d %d %d %d</c> into four locals and stores
/// <c>c1 &lt;&lt; 24 | c2 &lt;&lt; 16 | c3 &lt;&lt; 8 | c4</c> - first component highest.
///
/// *Which* component is alpha does not follow from that, and comes from the shipped data instead.
/// Across the 500 vanilla <c>.mgb</c> packages the two most common state colours by a wide margin
/// are <c>0xFFFFFFFF</c> (80,240 uses) and <c>0x00FFFFFF</c> (7,010), and the pattern repeats for
/// every other colour in use - <c>0xFFA5BDC5</c>/<c>0x00A5BDC5</c>, <c>0xFFC0C0C0</c>/<c>0x00C0C0C0</c>,
/// <c>0xFF9CB1B8</c>/<c>0x009CB1B8</c> - matched pairs of the same three low bytes at high byte
/// <c>FF</c> and <c>00</c>. That is what the two ends of a fade keyframe look like, and it only
/// works if the top byte is alpha. Reading the word as RGBA instead would make those pairs "opaque
/// white" and "opaque cyan", which nothing in a menu fade would produce 7,010 times.
///
/// So the word is ARGB and the authored order is <c>A R G B</c>. Earlier format notes called it
/// "packed RGBA"; see docs/docs/file-formats/mgb-field-names.md.
/// </remarks>
public static class MgbColor
{
    public static byte Alpha(uint value) => (byte)(value >> 24);
    public static byte Red(uint value) => (byte)(value >> 16);
    public static byte Green(uint value) => (byte)(value >> 8);
    public static byte Blue(uint value) => (byte)value;

    public static uint Pack(byte alpha, byte red, byte green, byte blue)
        => ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | blue;

    /// <summary>The authored <c>%d %d %d %d</c> form, for anyone comparing against a <c>.mgm</c> source.</summary>
    public static string Describe(uint value)
        => $"{Alpha(value)} {Red(value)} {Green(value)} {Blue(value)}";
}
