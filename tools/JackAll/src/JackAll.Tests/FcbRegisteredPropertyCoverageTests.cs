using System.Text.Json;
using JackAll.Core;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tests;

/// <summary>
/// What the field names recovered from <c>RegisterProperties</c>
/// (tools/fc2re/out/register_properties.jsonl) can and cannot add to <c>binary_classes.xml</c> for
/// real entity libraries, measured rather than assumed.
/// </summary>
/// <remarks>
/// The vocabulary genuinely matches - most member hashes in real entity libraries are names the
/// registry knows - but the XML already names almost all of those, and the <c>text_</c> twins it
/// derives cover the rest; the registry never held those, since the engine does not read them.
/// The registry reaches JackAll through <see cref="ComponentSchema"/> for its types, enum labels and
/// unset properties instead. These tests pin the measurement so any improvement upstream shows up here.
/// </remarks>
public class FcbRegisteredPropertyCoverageTests
{
    private static readonly string[] Libraries =
        [FcbDocumentTests.Worlds, FcbDocumentTests.PatchOverride, FcbDocumentTests.Dlc1];

    private static string RegisteredPropertiesPath
        => Path.Combine(TestSupport.RepositoryRoot, "tools", "fc2re", "out", "register_properties.jsonl");

    private static bool InputsPresent => File.Exists(RegisteredPropertiesPath) && Fixture.Present(Libraries);

    // The registry dump is gitignored like the fixtures, so its absence would no-op these tests too.
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_registry_dump_was_actually_found()
        => Assert.True(File.Exists(RegisteredPropertiesPath), $"{RegisteredPropertiesPath} is missing.");

    /// <summary>The registry's names are the same vocabulary real .fcb member hashes are CRC32 of.</summary>
    [Fact]
    public void The_registrys_member_names_are_the_same_vocabulary_as_fcb_member_names()
    {
        if (!InputsPresent) return;

        HashSet<uint> present = DistinctMemberHashesInFixtures();
        (_, Dictionary<uint, string> flat) = LoadRegistry();

        Assert.Equal(1650, present.Count);
        Assert.Equal(1449, flat.Keys.Count(present.Contains));
    }

    /// <summary>
    /// The few values left unnamed are all names the registry knows, but never under the node's own
    /// class: the registry's declaring class is not the .fcb node's type, so only its member names
    /// transfer, not its class scoping.
    /// </summary>
    [Fact]
    public void The_few_values_left_unnamed_are_registry_names_from_another_class()
    {
        if (!InputsPresent) return;

        // The XML alone, without the schema BundledAssets merges in: that is what is being measured.
        FcbClassDefinitions defs = FcbClassDefinitions.Load(
            BundledAssets.FindAsset(".fcbclasses", Path.Combine("assets", "binary_classes.xml"))!);
        (Dictionary<(uint Class, uint Member), string> scoped, Dictionary<uint, string> flat) = LoadRegistry();

        int values = 0, unnamed = 0, scopedHits = 0, flatHits = 0;

        foreach (string file in Libraries)
        {
            FcbObject root = FcbDocument.Deserialize(Fixture.Read(file)!);
            Walk(root, defs.GetClass(root.TypeHash));

            void Walk(FcbObject node, FcbClass cls)
            {
                foreach (uint member in node.Values.Keys)
                {
                    values++;
                    if (cls.FindMember(member)?.Name is not null)
                    {
                        continue;
                    }

                    unnamed++;
                    if (scoped.ContainsKey((node.TypeHash, member))) scopedHits++;
                    else if (flat.ContainsKey(member)) flatHits++;
                }

                foreach (FcbObject child in node.Children)
                {
                    Walk(child, cls.Resolve(child));
                }
            }
        }

        Assert.Equal(483004, values);
        Assert.Equal(7, unnamed);
        Assert.Equal(0, scopedHits);
        Assert.Equal(7, flatHits);
    }

    private static HashSet<uint> DistinctMemberHashesInFixtures()
    {
        HashSet<uint> present = [];
        foreach (string file in Libraries)
        {
            Collect(FcbDocument.Deserialize(Fixture.Read(file)!));
        }
        return present;

        void Collect(FcbObject node)
        {
            foreach (uint member in node.Values.Keys)
            {
                present.Add(member);
            }
            foreach (FcbObject child in node.Children)
            {
                Collect(child);
            }
        }
    }

    /// <summary>The registry keyed both ways: by declaring class and member, and by member hash alone.</summary>
    private static (Dictionary<(uint, uint), string> Scoped, Dictionary<uint, string> Flat) LoadRegistry()
    {
        var scoped = new Dictionary<(uint, uint), string>();
        var flat = new Dictionary<uint, string>();

        foreach (string line in File.ReadLines(RegisteredPropertiesPath))
        {
            if (line.Length == 0) continue;

            using JsonDocument row = JsonDocument.Parse(line);
            if (!row.RootElement.TryGetProperty("name", out JsonElement name)
                || name.ValueKind != JsonValueKind.String
                || !row.RootElement.TryGetProperty("owner", out JsonElement owner)
                || owner.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string memberName = name.GetString()!;
            uint memberHash = FcbClassDefinitions.Crc32Ascii(memberName);
            scoped[(FcbClassDefinitions.Crc32Ascii(owner.GetString()!), memberHash)] = memberName;
            flat[memberHash] = memberName;
        }
        return (scoped, flat);
    }
}
