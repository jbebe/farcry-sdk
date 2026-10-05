using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;

namespace JackAll.Core.Legacy;

/// <summary>What one file in a legacy mod's archive is.</summary>
/// <param name="Role">
/// <c>archive</c> a patch.fat/patch.dat pair; <c>data</c> a loose file at an archive path;
/// <c>binary</c> a patched game binary; <c>loose</c> anything else installed beside the game;
/// <c>doc</c> a readme or image for people; <c>unsupported</c> another archive pair, which no build
/// can carry.
/// </param>
public sealed record LegacyFile(string Path, string Role, long Size, string? Note = null);

/// <summary>The outcome of one analysis, written beside its changes.</summary>
public sealed record LegacyAnalysis(
    string Source,
    string? Sha256,
    string Game,
    IReadOnlyList<LegacyFile> Files,
    LegacyImportResult? Import,
    int Changes,
    int Units,
    int WholeUnits);

/// <summary>
/// Turns a legacy mod - a zip, 7z or rar, or a folder - into the complete list of its changes
/// against the base game, in a work folder: the extracted source, the imported layer, and
/// <c>changes.jsonl</c>.
/// </summary>
public static class LegacyAnalyzer
{
    public const string ChangesFile = "changes.jsonl";
    public const string AnalysisFile = "analysis.json";
    public const string LayerFolder = "layer";

    private static readonly HashSet<string> DocExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".pdf", ".rtf", ".htm", ".html", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".url", ".nfo", ".docx",
    };

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static LegacyAnalysis Analyze(
        string from, string workDir, GameInstall install, GameVfs vfs, NameDatabase names,
        FcbClassDefinitions definitions, IProgress<string>? progress = null)
    {
        Directory.CreateDirectory(workDir);
        string source = Directory.Exists(from) ? from : Extract(from, Path.Combine(workDir, "source"), progress);
        string? sha256 = File.Exists(from) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(from))) : null;

        string layerRoot = Path.Combine(workDir, LayerFolder);
        string dataRoot = Path.Combine(workDir, "data");
        Reset(layerRoot);
        Reset(dataRoot);

        progress?.Report("Reading the game's archive indexes…");
        HashSet<uint> baseHashes = install.ReadBaseGameHashes(progress);
        (List<LegacyFile> files, List<LegacyChange> outside, (string Fat, string Dat)? pair) =
            Inventory(source, install, baseHashes, dataRoot);

        var workspace = new FolderModLayer(layerRoot, "legacy");
        LegacyImportResult? import = null;
        if (pair is { } patch)
        {
            progress?.Report("Diffing the patch archive against the base game…");
            import = LegacyPatchImporter.Import(
                patch.Fat, patch.Dat, workspace, names, definitions, vfs.ReadOriginal, vfs.ReadOriginalHash, progress);
        }

        if (Directory.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories).Any())
        {
            progress?.Report("Diffing loose archive files against the base game…");
            LegacyImportResult loose = LegacyPatchImporter.ImportTree(
                dataRoot, workspace, names, definitions, vfs.ReadOriginal, vfs.ReadOriginalHash, progress);
            import = import is null ? loose : Combine(import, loose);
        }

        var units = new LegacyUnits(layerRoot, vfs, names, definitions);
        List<LegacyChange> changes = [];
        int unitCount = 0, wholeUnits = 0;
        foreach (string unit in units.All())
        {
            if (++unitCount % 1_000 == 0)
            {
                progress?.Report($"Comparing units field by field… ({unitCount:N0})");
            }

            List<LegacyChange> found = units.Compare(unit);
            wholeUnits += found.Count > 0 && found[0].Whole ? 1 : 0;
            changes.AddRange(found);
        }

        changes.AddRange(outside);
        LegacyChange.WriteAll(Path.Combine(workDir, ChangesFile), changes);

        var analysis = new LegacyAnalysis(
            Path.GetFullPath(from), sha256, install.RootPath, files, import, changes.Count, unitCount, wholeUnits);
        File.WriteAllText(Path.Combine(workDir, AnalysisFile), JsonSerializer.Serialize(analysis, Json));
        return analysis;
    }

    /// <summary>Marks the changes in units whose archetype a later library declares again, keyed by
    /// unit address, with that library. Returns how many changes it marked.</summary>
    public static int MarkShadowed(string workDir, IReadOnlyDictionary<string, string> winnerByUnit)
    {
        string path = Path.Combine(workDir, ChangesFile);
        int marked = 0;
        List<LegacyChange> changes = [.. LegacyChange.ReadAll(path).Select(change =>
        {
            if (!winnerByUnit.TryGetValue(change.Unit, out string? winner))
            {
                return change;
            }

            marked++;
            return change with { ShadowedBy = winner };
        })];
        LegacyChange.WriteAll(path, changes);
        return marked;
    }

    public static LegacyAnalysis ReadAnalysis(string workDir)
        => JsonSerializer.Deserialize<LegacyAnalysis>(File.ReadAllText(Path.Combine(workDir, AnalysisFile)), Json)
           ?? throw new InvalidDataException($"{AnalysisFile} in '{workDir}' is empty.");

    /// <summary>
    /// Sorts every file in the mod by what it is. Loose archive files are copied under
    /// <paramref name="dataRoot"/> at their archive paths; files installed beside the game become
    /// changes straight away, since no archive diff will see them.
    /// </summary>
    private static (List<LegacyFile>, List<LegacyChange>, (string Fat, string Dat)?) Inventory(
        string source, GameInstall install, HashSet<uint> baseHashes, string dataRoot)
    {
        List<string> all = [.. Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        List<string> fats = [.. all.Where(f => Path.GetExtension(f).Equals(".fat", StringComparison.OrdinalIgnoreCase))];
        List<string> patchFats = [.. fats.Where(f => Path.GetFileName(f).Equals("patch.fat", StringComparison.OrdinalIgnoreCase)
                                                && File.Exists(Path.ChangeExtension(f, ".dat")))];
        if (patchFats.Count > 1)
        {
            throw new InvalidDataException(
                "This mod ships more than one patch.fat - variants or optional parts. Analyze each one as its "
                + "own folder: " + string.Join(", ", patchFats.Select(f => Path.GetRelativePath(source, f))));
        }

        (string Fat, string Dat)? pair = patchFats.Count == 1 ? (patchFats[0], Path.ChangeExtension(patchFats[0], ".dat")) : null;
        var pairFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string fat in fats.Where(f => File.Exists(Path.ChangeExtension(f, ".dat"))))
        {
            pairFiles.Add(fat);
            pairFiles.Add(Path.ChangeExtension(fat, ".dat"));
        }

        string? gameRoot = GameRootOf(source);
        List<LegacyFile> files = [];
        List<LegacyChange> outside = [];
        foreach (string file in all)
        {
            string relative = Path.GetRelativePath(source, file);
            long size = new FileInfo(file).Length;
            string installPath = (gameRoot is null ? relative : Path.GetRelativePath(gameRoot, file)).Replace('\\', '/');

            if (pairFiles.Contains(file))
            {
                bool isPatch = pair is { } p && (file == p.Fat || file == p.Dat);
                files.Add(new LegacyFile(relative, isPatch ? "archive" : "unsupported", size,
                    isPatch ? null : "only patch.dat can be rebuilt; the rest of the archives are read-only"));
                continue;
            }

            if (ArchivePathOf(relative, installPath, baseHashes) is { } archivePath)
            {
                string staged = Path.Combine(dataRoot, archivePath);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                File.Copy(file, staged, overwrite: true);
                files.Add(new LegacyFile(relative, "data", size, archivePath.Replace('\\', '/')));
                continue;
            }

            if (DocExtensions.Contains(Path.GetExtension(file)))
            {
                files.Add(new LegacyFile(relative, "doc", size));
                continue;
            }

            string unit = "install/" + installPath.ToLowerInvariant();
            string vanillaPath = Path.Combine(install.RootPath, installPath);
            bool isBinary = Path.GetExtension(file).ToLowerInvariant() is ".dll" or ".exe";
            if (isBinary && File.Exists(vanillaPath))
            {
                files.Add(new LegacyFile(relative, "binary", size, installPath));
                outside.AddRange(BinaryDiff.Diff(unit, File.ReadAllBytes(vanillaPath), File.ReadAllBytes(file)));
                continue;
            }

            byte[] bytes = File.ReadAllBytes(file);
            files.Add(new LegacyFile(relative, "loose", size, installPath));
            outside.Add(new LegacyChange(ChangeKind.Loose, unit, unit,
                File.Exists(vanillaPath) ? $"{new FileInfo(vanillaPath).Length} bytes" : null,
                $"{size} bytes", true, LooseHint(bytes)));
        }

        return (files, outside, pair);
    }

    /// <summary>The folder inside the mod that stands for the game's install folder: the one holding
    /// <c>Data_Win32</c> or <c>bin</c>. Null when the mod has neither.</summary>
    private static string? GameRootOf(string source)
    {
        var pending = new Queue<string>([source]);
        while (pending.TryDequeue(out string? directory))
        {
            string[] children = Directory.GetDirectories(directory);
            if (children.Any(child => Path.GetFileName(child) is var name
                    && (name.Equals("Data_Win32", StringComparison.OrdinalIgnoreCase) || name.Equals("bin", StringComparison.OrdinalIgnoreCase))))
            {
                return directory;
            }

            foreach (string child in children)
            {
                pending.Enqueue(child);
            }
        }

        return null;
    }

    /// <summary>
    /// The archive path a loose file stands for: everything under <c>Data_Win32</c>, or failing
    /// that the shortest tail of its path that names a file the base game has.
    /// </summary>
    private static string? ArchivePathOf(string relative, string installPath, HashSet<uint> baseHashes)
    {
        string[] segments = installPath.Split('/');
        int data = Array.FindIndex(segments, s => s.Equals("Data_Win32", StringComparison.OrdinalIgnoreCase));
        if (data >= 0 && data < segments.Length - 1)
        {
            return string.Join('\\', segments[(data + 1)..]);
        }

        string[] relativeSegments = relative.Split(Path.DirectorySeparatorChar);
        for (int i = 0; i < relativeSegments.Length - 1; i++)
        {
            string tail = string.Join('\\', relativeSegments[i..]);
            if (baseHashes.Contains(NameHash.Compute(tail)))
            {
                return tail;
            }
        }

        return null;
    }

    private static string? LooseHint(byte[] bytes)
    {
        if (bytes.AsSpan().Contains((byte)0))
        {
            return null;
        }

        string first = System.Text.Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 400)).TrimStart().Split('\n', 2)[0].Trim();
        return first.Length > 0 ? first[..Math.Min(first.Length, 160)] : null;
    }

    private static LegacyImportResult Combine(LegacyImportResult a, LegacyImportResult b)
        => new(a.TotalEntries + b.TotalEntries, a.Imported + b.Imported, a.FragmentsImported + b.FragmentsImported,
            a.Skipped + b.Skipped, [.. a.Refused, .. b.Refused], [.. a.WholeFile, .. b.WholeFile],
            [.. a.Unreachable, .. b.Unreachable]);

    private static void Reset(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
    }

    /// <summary>Unpacks a zip natively, anything else through 7-Zip.</summary>
    private static string Extract(string archive, string destination, IProgress<string>? progress)
    {
        Reset(destination);
        progress?.Report($"Extracting {Path.GetFileName(archive)}…");
        if (Path.GetExtension(archive).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archive, destination);
            return destination;
        }

        string sevenZip = SevenZip()
            ?? throw new FileNotFoundException(
                $"'{Path.GetFileName(archive)}' is not a zip, and 7-Zip (7z.exe) is neither on PATH nor in Program Files.");
        using Process process = Process.Start(new ProcessStartInfo(sevenZip, ["x", "-y", $"-o{destination}", archive])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0
            ? destination
            : throw new InvalidDataException($"7-Zip could not extract '{archive}' (exit code {process.ExitCode}).");
    }

    private static string? SevenZip()
        => (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip"))
            .Select(dir => Path.Combine(dir, "7z.exe"))
            .FirstOrDefault(File.Exists);
}
