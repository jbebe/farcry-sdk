using System.Text.Json;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tests;

/// <summary>
/// Why the 5,680 field names recovered from <c>RegisterProperties</c>
/// (tools/fc2re/out/register_properties.jsonl) are not wired into <see cref="FcbClassDefinitions"/>,
/// measured rather than assumed.
/// </summary>
/// <remarks>
/// The vocabulary genuinely matches - most member hashes in real entity libraries are names the
/// registry knows - but the bundled <c>binary_classes.xml</c> already names almost all of those, and
/// the values it cannot name are ones the registry does not know either. So the registry would add
/// names to roughly one value in six thousand, at the cost of a second name source to keep in sync.
/// These tests pin that measurement so revisiting it is cheap and any improvement upstream shows up
/// as a failure here.
/// </remarks>
public class FcbRegisteredPropertyCoverageTests
{
    private const string FixturesDir = "Fixtures/Fcb";
    private const string ClassesFixture = "Fixtures/Fcb/binary_classes.xml";

    private static string RegisteredPropertiesPath
        => Path.Combine(TestSupport.RepositoryRoot, "tools", "fc2re", "out", "register_properties.jsonl");

    private static bool InputsPresent
        => File.Exists(RegisteredPropertiesPath)
           && File.Exists(ClassesFixture)
           && Directory.EnumerateFiles(FixturesDir, "*.fcb").Any();

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_measurement_inputs_were_actually_found()
        => Assert.True(
            InputsPresent,
            $"Needs {RegisteredPropertiesPath}, {ClassesFixture} and .fcb samples; the coverage "
            + "measurement silently no-opped.");

    /// <summary>
    /// The registry's names are the same vocabulary real .fcb member hashes are CRC32 of - the reason
    /// this source looked promising, and worth keeping recorded even though it is not wired in.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_registrys_member_names_are_the_same_vocabulary_as_fcb_member_names()
    {
        if (!InputsPresent) return;

        HashSet<uint> present = DistinctMemberHashesInFixtures();
        (_, Dictionary<uint, string> flat) = LoadRegistry();

        Assert.Equal(1650, present.Count);
        Assert.Equal(1436, flat.Keys.Count(present.Contains));
    }

    /// <summary>
    /// The reason it is not wired in: of the values <c>binary_classes.xml</c> leaves unnamed, the
    /// registry names a negligible share. The scoped count is zero because the registry's declaring
    /// class is not the .fcb node's own type - only its member names transfer, not its class scoping.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_registry_names_almost_none_of_what_binary_classes_leaves_unnamed()
    {
        if (!InputsPresent) return;

        FcbClassDefinitions defs = FcbClassDefinitions.Load(ClassesFixture);
        (Dictionary<(uint Class, uint Member), string> scoped, Dictionary<uint, string> flat) = LoadRegistry();

        int values = 0, unnamed = 0, scopedHits = 0, flatHits = 0;

        foreach (string file in Directory.EnumerateFiles(FixturesDir, "*.fcb"))
        {
            FcbObject root = FcbDocument.Deserialize(File.ReadAllBytes(file));
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
                    Walk(child, cls.Resolve(child.TypeHash));
                }
            }
        }

        Assert.Equal(596574, values);
        Assert.Equal(61355, unnamed);
        Assert.Equal(0, scopedHits);
        Assert.Equal(10, flatHits);
    }

    private static HashSet<uint> DistinctMemberHashesInFixtures()
    {
        HashSet<uint> present = [];
        foreach (string file in Directory.EnumerateFiles(FixturesDir, "*.fcb"))
        {
            Collect(FcbDocument.Deserialize(File.ReadAllBytes(file)));
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
