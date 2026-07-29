using System.Text.RegularExpressions;
using Domino.Core.Lua;

namespace Domino.Core.Nodes;

/// <summary>
/// Extracts a `system\` node's pin signature from its `-- DOMINO REFLECTION BOX START ... END` comment
/// block — a literal, hand-written XML-in-comments header BlackBox reads to populate the visual editor's
/// palette. Confirmed present, exactly once, in all 233 real `system\*.lua` files; whitespace inside a
/// tag (spaces vs. tabs, alignment padding) varies file to file but the tag/attribute vocabulary itself
/// is closed (six tags, seven attribute names total) — a handful of regexes are simpler and just as
/// robust as a general XML parser here.
/// </summary>
public static partial class ReflectionBoxParser
{
    private const string StartMarker = "DOMINO REFLECTION BOX START";
    private const string EndMarker = "DOMINO REFLECTION BOX END";

    [GeneratedRegex("""^<Display\s+Category="([^"]*)"\s+Text="([^"]*)"\s*/>$""")]
    private static partial Regex DisplayRegex();

    [GeneratedRegex("""^<ControlIn\s+Name="([^"]*)"(?:\s+Dynamic="([^"]*)")?\s*/>$""")]
    private static partial Regex ControlInRegex();

    [GeneratedRegex("""^<ControlOut\s+Name="([^"]*)"(?:\s+Delayed="([^"]*)")?(?:\s+Dynamic="([^"]*)")?\s*/>$""")]
    private static partial Regex ControlOutRegex();

    [GeneratedRegex("""^<DataIn\s+Name="([^"]*)"\s+Type="([^"]*)"\s*/>$""")]
    private static partial Regex DataInRegex();

    [GeneratedRegex("""^<DataOut\s+Name="([^"]*)"\s+Type="([^"]*)"\s*/>$""")]
    private static partial Regex DataOutRegex();

    [GeneratedRegex("""^<Stateless\s*/>$""")]
    private static partial Regex StatelessRegex();

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns null if <paramref name="chunk"/> has no reflection box (only expected for
    /// `user\` graph files, never for a real `system\` node).</summary>
    public static NodeReflection? Parse(LuaChunk chunk)
    {
        NodeDisplay? display = null;
        var controlIns = new List<ControlInPin>();
        var controlOuts = new List<ControlOutPin>();
        var dataIns = new List<DataInPin>();
        var dataOuts = new List<DataOutPin>();
        bool stateless = false;
        bool inBox = false;
        bool found = false;

        foreach (var stmt in chunk.Statements)
        {
            if (stmt is not CommentStmt comment)
            {
                continue;
            }

            string text = comment.Text.Trim();
            if (text == StartMarker)
            {
                inBox = true;
                found = true;
                continue;
            }
            if (text == EndMarker)
            {
                inBox = false;
                continue;
            }
            if (!inBox || text.Length == 0)
            {
                continue;
            }

            if (DisplayRegex().Match(text) is { Success: true } m)
            {
                display = new NodeDisplay(m.Groups[1].Value, m.Groups[2].Value);
            }
            else if (ControlInRegex().Match(text) is { Success: true } ci)
            {
                controlIns.Add(new ControlInPin(ci.Groups[1].Value, IsTrue(ci.Groups[2].Value)));
            }
            else if (ControlOutRegex().Match(text) is { Success: true } co)
            {
                controlOuts.Add(new ControlOutPin(co.Groups[1].Value, IsTrue(co.Groups[2].Value), IsTrue(co.Groups[3].Value)));
            }
            else if (DataInRegex().Match(text) is { Success: true } di)
            {
                dataIns.Add(new DataInPin(di.Groups[1].Value, di.Groups[2].Value));
            }
            else if (DataOutRegex().Match(text) is { Success: true } dout)
            {
                dataOuts.Add(new DataOutPin(dout.Groups[1].Value, dout.Groups[2].Value));
            }
            else if (StatelessRegex().IsMatch(text))
            {
                stateless = true;
            }
            else
            {
                throw new FormatException($"Unrecognized reflection box line: '{text}'");
            }
        }

        return found ? new NodeReflection(display, controlIns, controlOuts, dataIns, dataOuts, stateless) : null;
    }
}
