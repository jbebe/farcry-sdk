namespace JackAll.Tools;

/// <summary>The four difficulty levels, 0 to 3, by the names the game's menu shows.</summary>
public static class Difficulty
{
    public static IReadOnlyList<string> Names { get; } = ["Easy", "Normal", "Hardcore", "Infamous"];

    public static string NameOf(uint level) => level < Names.Count ? Names[(int)level] : $"difficulty {level}";
}
