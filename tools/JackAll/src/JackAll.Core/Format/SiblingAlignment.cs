using System.Xml.Linq;

namespace JackAll.Core.Format;

/// <summary>
/// Pairs two lists of sibling elements: equal ones first, by longest common subsequence, then whatever
/// sits between two such anchors by position. Unpaired entries are removals or additions.
/// </summary>
public static class SiblingAlignment
{
    /// <summary>Beyond this many sibling pairs, siblings are paired by position.</summary>
    private const long Budget = 4_000_000;

    /// <param name="identity">What two elements must share to count as equal.</param>
    public static IEnumerable<(int? Before, int? After)> Align(
        List<XElement> before, List<XElement> after, Func<XElement, string> identity)
    {
        // A list of plain values edited in place keeps its length; matching equal values out of
        // position there would read every edit as a removal and an addition.
        bool valuesInPlace = before.Count == after.Count && before.Concat(after).All(e => !e.HasElements);
        List<(int, int)> anchors = !valuesInPlace && (long)before.Count * after.Count <= Budget
            ? CommonSubsequence([.. before.Select(identity)], [.. after.Select(identity)])
            : [];
        anchors.Add((before.Count, after.Count));

        int b = 0, a = 0;
        foreach ((int nextB, int nextA) in anchors)
        {
            while (b < nextB && a < nextA)
            {
                yield return (b++, a++);
            }

            while (b < nextB)
            {
                yield return (b++, null);
            }

            while (a < nextA)
            {
                yield return (null, a++);
            }

            if (nextB < before.Count)
            {
                yield return (b++, a++);
            }
        }
    }

    private static List<(int, int)> CommonSubsequence(string[] before, string[] after)
    {
        int[,] length = new int[before.Length + 1, after.Length + 1];
        for (int i = before.Length - 1; i >= 0; i--)
        {
            for (int j = after.Length - 1; j >= 0; j--)
            {
                length[i, j] = before[i] == after[j]
                    ? length[i + 1, j + 1] + 1
                    : Math.Max(length[i + 1, j], length[i, j + 1]);
            }
        }

        List<(int, int)> pairs = [];
        for (int i = 0, j = 0; i < before.Length && j < after.Length;)
        {
            if (before[i] == after[j])
            {
                pairs.Add((i++, j++));
            }
            else if (length[i + 1, j] >= length[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return pairs;
    }
}
