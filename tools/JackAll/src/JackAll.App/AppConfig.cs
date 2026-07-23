using System.IO;
using IniParser;
using IniParser.Model;

namespace JackAll.App;

/// <summary>
/// config.ini, sitting next to the exe.
/// </summary>
/// <remarks>
/// INI rather than JSON because this file is meant to be hand-edited and pasted into forum posts by
/// people who don't program. A stray trailing comma kills a JSON file; an INI shrugs.
///
/// A disabled mod is marked by a '!' in front of its path rather than by commenting the line out.
/// Commenting-out reads better but means round-tripping comments through the parser to remember
/// which mods exist-but-are-off, and that machinery is worse than the problem it solves. '!' is one
/// character, obvious on sight, and survives hand-editing.
/// </remarks>
public sealed class AppConfig
{
    private const string GameSection = "game";
    private const string ModsSection = "mods";
    private const char DisabledMarker = '!';

    private static string ExeDir => AppContext.BaseDirectory;

    public static string ConfigPath => Path.Combine(ExeDir, "config.ini");

    /// <summary>Fixed by design — the staging area is not something a user should have to locate.</summary>
    public static string WorkspaceDir => Path.Combine(ExeDir, "workspace");

    /// <summary>
    /// Everything the tool ships or writes that isn't <see cref="ConfigPath"/> or
    /// <see cref="WorkspaceDir"/> — kept out of the exe's own folder so that folder stays just
    /// "JackAll.exe, config.ini, workspace\" to look at.
    /// </summary>
    private static string DataDir => Path.Combine(ExeDir, "data");

    /// <summary>
    /// The hash -> filename dictionary. A fixed part of the product, not something a user picks or
    /// edits.
    /// </summary>
    public static string NamesFile => Path.Combine(DataDir, ".itemhashes");

    /// <summary>
    /// Sniffed file types for the game's read-only archives, plus decoded `.fcb` fragment structure
    /// (see <c>FcbXml.ListFragmentIds</c>) — one file for both, since they share the same lifecycle:
    /// safe to delete at any time, rebuilt on the next launch. Deleting it is the supported way to
    /// recover if the game itself is reinstalled or patched underneath us.
    /// </summary>
    public static string CacheFile => Path.Combine(DataDir, ".appcache");

    /// <summary>
    /// Far Cry 2's .fcb class/member name-and-type config (wobatt's improved binary_classes.xml) — a
    /// fixed part of the product, same reasoning as <see cref="NamesFile"/>. Missing is not fatal: the
    /// .fcb handler falls back to hash-only/BinHex for everything (see <c>FcbClassDefinitions.Empty</c>).
    /// </summary>
    public static string BinaryClassesFile => Path.Combine(DataDir, ".fcbclasses");

    /// <summary>
    /// Reference archive hashes for a clean 1.03 install (see
    /// <see cref="JackAll.Core.VanillaHashes"/>) — same shipped-asset treatment as
    /// <see cref="NamesFile"/>/<see cref="BinaryClassesFile"/>. Missing is not fatal: validation
    /// against an empty hash set simply finds nothing to flag.
    /// </summary>
    public static string VanillaHashesFile => Path.Combine(DataDir, ".archivehashes");

    /// <summary>
    /// A savegame-specific hash -> name table (see <see cref="JackAll.App.SaveGames.SaveGameCompiledFieldNames"/>)
    /// recovered by CRC32 dictionary-matching every string constant in the game's Linux dedicated-server
    /// binary against the exact hashes a real save's <c>PersistenceDB</c> tree uses - a fixed part of the
    /// product, same reasoning as <see cref="BinaryClassesFile"/>. Missing is not fatal: those hashes
    /// just stay unresolved in the Saves tab's tree view.
    /// </summary>
    public static string SaveGameFieldNamesFile => Path.Combine(DataDir, ".savegamefieldnames");

    public string GamePath { get; set; } = string.Empty;

    /// <summary>Mod zip paths in apply order — later ones win.</summary>
    public List<ModEntry> Mods { get; init; } = [];

    /// <summary>
    /// The workspace layer's own on/off state — stored separately from <see cref="Mods"/> because the
    /// workspace isn't a zip path, it's the always-last staging folder (see
    /// <see cref="MainViewModel.LoadModsFromConfig"/>). Persisted under the reserved "0" key in
    /// <see cref="ModsSection"/>, ahead of the numbered ("1", "2", …) mod entries.
    /// </summary>
    public bool WorkspaceEnabled { get; set; } = true;

    public sealed record ModEntry(string Path, bool Enabled);

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new AppConfig();
        }

        var parser = new FileIniDataParser();
        parser.Parser.Configuration.SkipInvalidLines = true;
        IniData data = parser.ReadFile(ConfigPath);

        var config = new AppConfig
        {
            GamePath = data[GameSection]["path"]?.Trim() ?? string.Empty,
        };

        string workspaceValue = data[ModsSection]["0"]?.Trim() ?? string.Empty;
        if (workspaceValue.Length > 0)
        {
            config.WorkspaceEnabled = workspaceValue[0] != DisabledMarker;
        }

        // Keys are ordinals ("1", "2", …) purely so the file reads as an ordered list; the numbers
        // themselves carry no meaning beyond sort order. "0" is reserved for WorkspaceEnabled above,
        // not a mod entry.
        foreach (KeyData key in data[ModsSection]
                     .Where(k => k.KeyName != "0")
                     .OrderBy(k => int.TryParse(k.KeyName, out int n) ? n : int.MaxValue))
        {
            string value = key.Value.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            bool enabled = value[0] != DisabledMarker;
            config.Mods.Add(new ModEntry(value.TrimStart(DisabledMarker).Trim(), enabled));
        }

        return config;
    }

    public void Save()
    {
        var data = new IniData();

        data.Sections.AddSection(GameSection);
        data.Sections.GetSectionData(GameSection).Comments.AddRange(
        [
            " Far Cry 2 install folder - must contain bin\\FarCry2.exe and Data_Win32\\patch.fat",
        ]);
        data[GameSection]["path"] = GamePath;

        data.Sections.AddSection(ModsSection);
        data.Sections.GetSectionData(ModsSection).Comments.AddRange(
        [
            " Applied top to bottom - if two mods change the same file, the lower one wins.",
            $" Put a {DisabledMarker} in front of a path to turn that mod off without losing it.",
            " \"0\" is the workspace (staging area) toggle, always applied last - not a mod path.",
        ]);

        data[ModsSection]["0"] = WorkspaceEnabled ? "workspace" : DisabledMarker + "workspace";

        int index = 1;
        foreach (ModEntry mod in Mods)
        {
            data[ModsSection][index++.ToString()] = mod.Enabled ? mod.Path : DisabledMarker + mod.Path;
        }

        new FileIniDataParser().WriteFile(ConfigPath, data);
    }
}
