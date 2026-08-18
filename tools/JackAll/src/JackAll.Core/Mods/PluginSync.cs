using System.Text.Json;
using System.Text.Json.Serialization;

namespace JackAll.Core.Mods;

/// <summary>What one <see cref="PluginSync.Apply"/> or <see cref="PluginSync.RemoveAll"/> did.
/// <see cref="SkippedForeign"/> lists desired paths whose target file exists but isn't
/// manifest-tracked — left untouched, for the caller to warn about.</summary>
public sealed record PluginSyncResult(int Deployed, int Removed, IReadOnlyList<string> SkippedForeign);

/// <summary>
/// Mirrors the enabled layers' <c>plugins\</c> payloads (see <see cref="IModLayer.PluginPaths"/>)
/// into <c>bin\plugins</c>, where FCSE loads them from. The manifest records which files are
/// JackAll's; anything not listed — a hand-installed DLL, another manager's deployment — is never
/// overwritten or deleted.
/// </summary>
public static class PluginSync
{
    /// <summary>No .dll/.lua extension, so FCSE never tries to load it.</summary>
    public const string ManifestFileName = ".jackall-plugins.json";

    public static string PluginsDir(GameInstall install)
        => Path.Combine(install.RootPath, "bin", ModPathHashing.PluginsFolder);

    /// <summary>Makes <c>bin\plugins</c> match the enabled layers' plugin payloads, later layer
    /// winning on a shared path — the same rule whole-file overrides follow.</summary>
    public static PluginSyncResult Apply(
        GameInstall install, IReadOnlyList<IModLayer> layers, IProgress<string>? progress = null)
    {
        string pluginsDir = PluginsDir(install);
        HashSet<string> tracked = LoadTrackedPaths(pluginsDir, progress);

        var desired = new Dictionary<string, IModLayer>(StringComparer.OrdinalIgnoreCase);
        foreach (IModLayer layer in layers.Where(l => l.Enabled))
        {
            foreach (string path in layer.PluginPaths)
            {
                desired[path] = layer;
            }
        }

        // Nothing wanted and nothing owned: never touch (or create) bin\plugins at all.
        if (desired.Count == 0 && tracked.Count == 0)
        {
            return new PluginSyncResult(0, 0, []);
        }

        int deployed = 0;
        var skippedForeign = new List<string>();
        var manifest = new List<PluginManifestEntry>();
        foreach ((string path, IModLayer layer) in desired)
        {
            string target = Path.Combine(pluginsDir, path);
            byte[] content = layer.ReadPlugin(path);
            // An untracked file with the same bytes is the same plugin, whoever copied it there -
            // adopt it instead of warning. Different bytes stay foreign and untouched.
            if (!tracked.Contains(path) && File.Exists(target)
                && !content.AsSpan().SequenceEqual(File.ReadAllBytes(target)))
            {
                skippedForeign.Add(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, content);
            deployed++;
            manifest.Add(new PluginManifestEntry(path, layer.Name));
        }

        int removed = Remove(pluginsDir, tracked.Where(path => !desired.ContainsKey(path)));
        WriteManifest(pluginsDir, manifest);
        return new PluginSyncResult(deployed, removed, skippedForeign);
    }

    /// <summary>Removes every manifest-tracked file plus the manifest itself. No manifest → no-op.</summary>
    public static PluginSyncResult RemoveAll(GameInstall install)
    {
        string pluginsDir = PluginsDir(install);
        int removed = Remove(pluginsDir, LoadTrackedPaths(pluginsDir, progress: null));
        WriteManifest(pluginsDir, []);
        return new PluginSyncResult(0, removed, []);
    }

    private static HashSet<string> LoadTrackedPaths(string pluginsDir, IProgress<string>? progress)
    {
        var tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string manifestPath = Path.Combine(pluginsDir, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return tracked;
        }

        try
        {
            PluginManifest? manifest = JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath), PluginManifestJson.Default.PluginManifest);
            foreach (PluginManifestEntry entry in manifest?.Files ?? [])
            {
                tracked.Add(entry.Path);
            }
        }
        catch (JsonException ex)
        {
            // Ownership of past deployments is lost; deploying fresh is still correct, the stale
            // files just surface as foreign until removed by hand.
            progress?.Report($"Ignoring unreadable {ManifestFileName}: {ex.Message}");
        }
        return tracked;
    }

    private static int Remove(string pluginsDir, IEnumerable<string> paths)
    {
        int removed = 0;
        foreach (string path in paths)
        {
            string target = Path.Combine(pluginsDir, path);
            if (File.Exists(target))
            {
                File.Delete(target);
                removed++;
            }
            PruneEmptyDirectories(Path.GetDirectoryName(target), pluginsDir);
        }
        return removed;
    }

    /// <summary>Deletes now-empty folders up to (never including) <paramref name="stopAt"/> — FCSE
    /// expects <c>bin\plugins</c> itself to stay.</summary>
    private static void PruneEmptyDirectories(string? dir, string stopAt)
    {
        while (dir is not null
               && dir.StartsWith(stopAt, StringComparison.OrdinalIgnoreCase)
               && !dir.Equals(stopAt, StringComparison.OrdinalIgnoreCase)
               && Directory.Exists(dir)
               && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    private static void WriteManifest(string pluginsDir, List<PluginManifestEntry> files)
    {
        string manifestPath = Path.Combine(pluginsDir, ManifestFileName);
        if (files.Count == 0)
        {
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }
            return;
        }

        File.WriteAllText(manifestPath, JsonSerializer.Serialize(
            new PluginManifest(1, files), PluginManifestJson.Default.PluginManifest));
    }
}

/// <summary><see cref="PluginSync"/>'s bookkeeping file. <c>Layer</c> is informational only —
/// ownership is decided by <c>Path</c> membership alone.</summary>
internal sealed record PluginManifest(int Version, List<PluginManifestEntry> Files);

internal sealed record PluginManifestEntry(string Path, string Layer);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(PluginManifest))]
internal partial class PluginManifestJson : JsonSerializerContext;
