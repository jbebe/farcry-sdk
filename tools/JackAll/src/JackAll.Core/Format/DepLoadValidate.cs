
namespace JackAll.Core.Format;

/// <summary>
/// Checks the invariants a `depload.dat` has to hold that decoding it does not already prove.
/// </summary>
/// <remarks>
/// Decoding rejects a slice that runs off the end and a type index past the table, and encoding
/// rejects the u16/u8 ceilings, so the gap this fills is the sort order - the engine binary-searches
/// the parents array, and a file with it wrong loads and then misbehaves rather than failing. That is
/// the documented way hand-merging this format goes wrong, and it is invisible until playtesting.
/// </remarks>
public static class DepLoadValidate
{
    public static IReadOnlyList<string> Problems(DepLoadFile file)
    {
        var problems = new List<string>();

        for (int i = 1; i < file.Parents.Count; i++)
        {
            uint previous = file.Parents[i - 1].Hash;
            uint current = file.Parents[i].Hash;
            if (current <= previous)
            {
                problems.Add(current == previous
                    ? $"Parent {current} is listed twice (entries {i - 1} and {i})."
                    : $"Parents are out of order at entry {i}: {current} follows {previous}. The engine "
                      + "binary-searches this array, so it must ascend as unsigned 32-bit.");
            }
        }

        return problems;
    }
}
