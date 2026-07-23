using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Rml;

namespace JackAll.Core.Tests;

/// <summary>
/// Runs against real shipped .rml files — the two DLC `toc.rml` manifests as checked-in fixtures
/// (small and deterministic), plus, when the real game is installed, `oasisstrings.rml` pulled
/// straight out of `patch.fat` (much larger: thousands of nodes, heavy string-table reuse), same
/// rationale as <see cref="FatArchiveTests"/> — the only authority on the format is what Ubisoft
/// actually shipped.
/// </summary>
public class RmlDocumentTests
{
    private const string FixturesDir = "Fixtures/Rml";

    [Fact]
    public void The_fixture_files_were_actually_found()
        => Assert.True(
            Directory.Exists(FixturesDir) && Directory.EnumerateFiles(FixturesDir, "*.rml").Any(),
            $"{FixturesDir} had no .rml samples, so every fixture-backed test in this class silently no-opped.");

    public static TheoryData<string> SampleFiles()
    {
        var data = new TheoryData<string>();
        if (!Directory.Exists(FixturesDir))
        {
            data.Add(string.Empty);
            return data;
        }
        foreach (string file in Directory.EnumerateFiles(FixturesDir, "*.rml"))
        {
            data.Add(file);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void Reserializing_a_shipped_rml_reproduces_it_byte_for_byte(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        byte[] original = File.ReadAllBytes(path);
        XElement root = RmlDocument.Deserialize(original);
        byte[] rewritten = RmlDocument.Serialize(root);

        Assert.Equal(original, rewritten);
    }

    [Fact]
    public void Decoding_the_jungle_dlc_manifest_produces_the_expected_content()
    {
        byte[] original = File.ReadAllBytes(Path.Combine(FixturesDir, "dlc_jungle_toc.rml"));
        XElement root = RmlDocument.Deserialize(original);

        Assert.Equal("DLC", root.Name.LocalName);
        Assert.Equal("Jungle", (string?)root.Attribute("name"));
        XElement? map = root.Element("MapService")?.Element("Maps")?.Element("Map");
        Assert.Equal("Jungle Seizure", (string?)map?.Attribute("displayName"));
        Assert.Equal("2001", (string?)map?.Attribute("id"));
    }

    /// <summary>
    /// Skips (rather than fails) when the game isn't installed, so the suite stays green on a machine
    /// without a copy of FC2 — matching <see cref="FatArchiveTests"/>'s convention exactly.
    /// </summary>
    private static string? FindDataDir()
    {
        string[] candidates =
        [
            @"C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2",
            @"D:\Steam\steamapps\common\Far Cry 2",
        ];
        return candidates
            .Select(root => Path.Combine(root, "Data_Win32"))
            .FirstOrDefault(Directory.Exists);
    }

    [Fact]
    public void Reserializing_the_real_oasisstrings_rml_reproduces_it_byte_for_byte()
    {
        string? dataDir = FindDataDir();
        if (dataDir is null) return;

        using var archive = DuniaArchive.Open(Path.Combine(dataDir, "patch.fat"));
        uint hash = NameHash.Compute(@"languages\english\oasisstrings.rml");
        Assert.True(archive.Contains(hash), "patch.fat has no languages\\english\\oasisstrings.rml entry.");

        byte[] original = archive.Read(hash);
        XElement root = RmlDocument.Deserialize(original);
        byte[] rewritten = RmlDocument.Serialize(root);

        Assert.Equal(original, rewritten);
        Assert.True(root.Descendants().Count() > 100, "Expected a large, real localization tree.");
    }
}
