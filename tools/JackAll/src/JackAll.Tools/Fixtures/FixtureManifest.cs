using System.IO.Compression;
using System.Security.Cryptography;
using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Naming;

namespace JackAll.Tools.Fixtures;

/// <summary>
/// One test fixture and where its bytes come from: an entry of an archive (<see cref="Source"/> is the
/// .fat under Data_Win32), a whole file under Data_Win32 (no entry), a Nexus download (<see cref="Source"/>
/// is the mod page, <see cref="Entry"/> the file name, then <c>/</c> and a path inside it when it is a
/// zip), or a file committed to git.
/// </summary>
public sealed record FixtureLine(string Fixture, string Sha256, string Source, string Entry)
{
    public bool FromNexus => Source.StartsWith("https://", StringComparison.Ordinal);
    public bool FromGit => Source == "git";
    public bool FromArchive => Entry.Length > 0 && Source.EndsWith(".fat", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a restore or record wrote, what it left as it was, and every fixture it could not
/// account for.</summary>
public sealed record FixtureReport(int Written, int Unchanged, IReadOnlyList<string> Problems);

/// <summary>
/// The test fixtures' <c>manifest.tsv</c>: every file under the fixtures folder by SHA-256, and where a
/// Steam install of Far Cry 2 1.03 or a Nexus download holds the same bytes.
/// </summary>
public static class FixtureManifest
{
    private const string FileName = "manifest.tsv";
    private const string Header = "# fixture\tsha256\tsource\tentry";

    public static List<FixtureLine> Read(string folder)
        => [.. File.ReadLines(Path.Combine(folder, FileName))
            .Where(line => line.Length > 0 && line[0] != '#')
            .Select(line => line.Split('\t'))
            .Select(cells => new FixtureLine(cells[0], cells[1], cells[2], cells.Length > 3 ? cells[3] : ""))];

    public static void Write(string folder, IEnumerable<FixtureLine> lines)
        => File.WriteAllLines(
            Path.Combine(folder, FileName),
            [Header, .. lines
                .OrderBy(line => line.Fixture, StringComparer.Ordinal)
                .Select(line => $"{line.Fixture}\t{line.Sha256}\t{line.Source}\t{line.Entry}".TrimEnd('\t'))]);

    /// <summary>The fixture files no line of the manifest names with their present bytes.</summary>
    public static List<string> Unrecorded(string folder, IEnumerable<FixtureLine> lines)
    {
        Dictionary<string, string> recorded = lines.ToDictionary(line => line.Fixture, line => line.Sha256);
        return [.. Files(folder).Where(fixture => recorded.GetValueOrDefault(fixture) != Sha256Of(Path.Combine(folder, fixture)))];
    }

    /// <summary>Writes every fixture that is missing or differs from its line, read from the install's
    /// archives and files or from the Nexus downloads in <paramref name="mods"/>.</summary>
    public static FixtureReport Restore(string folder, GameInstall install, string mods)
    {
        List<FixtureLine> lines = Read(folder);
        using var sources = new InstallSources(install);
        sources.RequireRecordedPatch(lines);

        int written = 0, unchanged = 0;
        var problems = new List<string>();
        foreach (FixtureLine line in lines)
        {
            string path = Path.Combine(folder, line.Fixture);
            if (File.Exists(path) && Sha256Of(path) == line.Sha256)
            {
                unchanged++;
                continue;
            }
            if (line.FromGit)
            {
                problems.Add($"{line.Fixture} is committed to git: check it out again.");
                continue;
            }

            try
            {
                byte[] bytes = line.FromNexus ? ReadDownload(line, mods) : sources.Read(line);
                if (Sha256Of(bytes) != line.Sha256)
                {
                    problems.Add($"{line.Fixture}: {line.Source} {line.Entry} holds other bytes than the manifest's.");
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
                written++;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
            {
                problems.Add($"{line.Fixture}: {ex.Message}");
            }
        }
        return new FixtureReport(written, unchanged, problems);
    }

    /// <summary>
    /// Writes a line for each fixture file the manifest lacks or lists with other bytes, found in the
    /// install by its bytes. Lines are only added or replaced: one whose file is gone stays until it is
    /// removed by hand.
    /// </summary>
    public static FixtureReport Record(string folder, GameInstall install)
    {
        Dictionary<string, FixtureLine> lines = (File.Exists(Path.Combine(folder, FileName)) ? Read(folder) : [])
            .ToDictionary(line => line.Fixture);
        using var sources = new InstallSources(install);
        sources.RequireRecordedPatch(lines.Values);

        List<(string Fixture, string Sha256, long Size)> wanted = [.. Unrecorded(folder, lines.Values).Select(fixture =>
        {
            string path = Path.Combine(folder, fixture);
            return (fixture, Sha256Of(path), new FileInfo(path).Length);
        })];
        List<FixtureLine> found = sources.Find(wanted);
        foreach (FixtureLine line in found)
        {
            lines[line.Fixture] = line;
        }
        Write(folder, lines.Values);

        HashSet<string> recorded = [.. found.Select(line => line.Fixture)];
        return new FixtureReport(found.Count, lines.Count - found.Count, [.. wanted
            .Where(fixture => !recorded.Contains(fixture.Fixture))
            .Select(fixture => $"{fixture.Fixture} is not in this install: a Nexus download or a committed "
                + "file needs its line written by hand.")]);
    }

    private static IEnumerable<string> Files(string folder)
        => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
            .Where(path => path is not FileName and not ".gitignore");

    private static byte[] ReadDownload(FixtureLine line, string mods)
    {
        int slash = line.Entry.IndexOf('/');
        string name = slash < 0 ? line.Entry : line.Entry[..slash];
        string path = Path.Combine(mods, name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"download {name} from {line.Source} into {mods}.");
        }
        if (slash < 0)
        {
            return File.ReadAllBytes(path);
        }

        using ZipArchive zip = ZipFile.OpenRead(path);
        string inner = line.Entry[(slash + 1)..];
        ZipArchiveEntry entry = zip.GetEntry(inner) ?? throw new FileNotFoundException($"{name} holds no {inner}.");
        var bytes = new byte[entry.Length];
        using Stream stream = entry.Open();
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static string Sha256Of(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>An install's archives and files as the base game holds them, each archive opened once.</summary>
    private sealed class InstallSources(GameInstall install) : IDisposable
    {
        private readonly Dictionary<string, DuniaArchive> _archives = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Refuses an install whose patch index is not the one the manifest records.</summary>
        public void RequireRecordedPatch(IEnumerable<FixtureLine> lines)
        {
            FixtureLine? patch = lines.FirstOrDefault(line => !line.FromArchive
                && line.Source.Equals("patch.fat", StringComparison.OrdinalIgnoreCase));
            if (patch is not null && Sha256Of(DataPath(patch.Source)) != patch.Sha256)
            {
                throw new InvalidOperationException(
                    $"The fixtures come from Steam's Far Cry 2 1.03, and {DataPath(patch.Source)} is another patch: "
                    + "GOG ships its own, and a modded patch.dat with no .vanilla backup beside it is not the "
                    + "original. Point --game at a Steam install.");
            }
        }

        public byte[] Read(FixtureLine line)
        {
            if (!line.FromArchive)
            {
                return File.ReadAllBytes(DataPath(line.Source));
            }
            uint hash = line.Entry.StartsWith('#') ? Convert.ToUInt32(line.Entry[1..], 16) : NameHash.Compute(line.Entry);
            return Archive(line.Source).Read(hash);
        }

        /// <summary>Lines for the wanted files that a file under Data_Win32 or an archive entry matches
        /// byte for byte.</summary>
        public List<FixtureLine> Find(IReadOnlyList<(string Fixture, string Sha256, long Size)> wanted)
        {
            Dictionary<long, List<(string Fixture, string Sha256)>> bySize = wanted
                .GroupBy(fixture => fixture.Size)
                .ToDictionary(group => group.Key, group => group.Select(fixture => (fixture.Fixture, fixture.Sha256)).ToList());
            var found = new List<FixtureLine>();

            void Match(byte[] bytes, string source, string entry)
            {
                List<(string Fixture, string Sha256)> group = bySize[bytes.LongLength];
                string sha = Sha256Of(bytes);
                found.AddRange(group.Where(fixture => fixture.Sha256 == sha)
                    .Select(fixture => new FixtureLine(fixture.Fixture, sha, source, entry)));
                group.RemoveAll(fixture => fixture.Sha256 == sha);
                if (group.Count == 0)
                {
                    bySize.Remove(bytes.LongLength);
                }
            }

            foreach (string path in Directory.EnumerateFiles(install.DataDir, "*", SearchOption.AllDirectories))
            {
                string source = Path.GetRelativePath(install.DataDir, path);
                if (source.EndsWith(GameInstall.VanillaSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    source = source[..^GameInstall.VanillaSuffix.Length];
                }
                // Skips the live patch when its backup stands in for it.
                if (DataPath(source) == path && bySize.ContainsKey(new FileInfo(path).Length))
                {
                    Match(File.ReadAllBytes(path), source, "");
                }
            }

            NameDatabase? names = null;
            foreach (string fat in install.EnumerateArchiveFats().TakeWhile(_ => bySize.Count > 0))
            {
                string source = Path.GetRelativePath(install.DataDir, fat);
                DuniaArchive archive = Archive(source);
                foreach (FatEntry entry in archive.Entries.Where(entry => bySize.ContainsKey(entry.RealSize)))
                {
                    names ??= BundledAssets.LoadNames();
                    Match(archive.Read(entry), source, names.TryResolve(entry.Hash, out string name) ? name : $"#{entry.Hash:x8}");
                }
            }
            return found;
        }

        public void Dispose()
        {
            foreach (DuniaArchive archive in _archives.Values)
            {
                archive.Dispose();
            }
        }

        private DuniaArchive Archive(string fat)
        {
            if (!_archives.TryGetValue(fat, out DuniaArchive? archive))
            {
                archive = DuniaArchive.Open(DataPath(fat), DataPath(Path.ChangeExtension(fat, ".dat")), Path.GetFileNameWithoutExtension(fat));
                _archives[fat] = archive;
            }
            return archive;
        }

        private string DataPath(string relative) => install.BasePath(Path.Combine(install.DataDir, relative));
    }
}
