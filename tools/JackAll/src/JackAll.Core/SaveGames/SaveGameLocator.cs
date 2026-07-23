namespace JackAll.Core.SaveGames;

/// <summary>
/// Resolves the folder Far Cry 2 writes savegames to and enumerates the .sav files in it.
/// </summary>
/// <remarks>
/// The game itself builds this path by calling <c>SHGetFolderPathA</c>/<c>W</c> (almost certainly
/// with <c>CSIDL_PERSONAL</c>/<c>CSIDL_MYDOCUMENTS</c>) and appending the literal string
/// <c>"My Games\Far Cry 2\"</c> — not reading an environment variable — per
/// reverse/dunia/save_data_path.md. <see cref="Environment.GetFolderPath"/> with
/// <see cref="Environment.SpecialFolder.MyDocuments"/> is the .NET equivalent of that same Shell API
/// call, so it tracks a redirected Documents folder the same way the game does.
/// </remarks>
public static class SaveGameLocator
{
    public static string SavedGamesFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "My Games", "Far Cry 2", "Saved Games");

    /// <summary>
    /// Every .sav file in <see cref="SavedGamesFolder"/> — empty (not an error) if that folder
    /// doesn't exist yet, which just means the game has never been run or nothing has been saved.
    /// </summary>
    public static IEnumerable<string> EnumerateSaveFiles()
    {
        string folder = SavedGamesFolder;
        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.sav") : [];
    }
}
