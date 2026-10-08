using System.Collections.Concurrent;
using System.Xml.Linq;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// Two layers' edits to one `.fcb` fragment merged as a tree: a value paired by its name, an object by
/// its type, and a list of same-typed siblings record by record, each matched to its ancestor by content.
/// </summary>
public class FcbMergeTests : IDisposable
{
    private const string ContainerPath = @"worlds\world1\generated\entitylibrary.fcb";

    private static readonly FcbContainerSplitter Splitter = new(FcbClassDefinitions.Empty);

    /// <summary>An archetype with one component, whose event links are a list.</summary>
    private static readonly string Ancestor = XElement.Parse("""
        <object type="EntityPrototype">
          <value name="Name" type="String">Test.Gun</value>
          <object type="Entity">
            <value name="hidName" type="String">Test.Gun</value>
            <object type="Components">
              <object type="CEventComponent">
                <value name="hidHasAliasName" type="Bool">False</value>
                <object type="hidLinks">
                  <object type="Link">
                    <value name="InputEvent" type="String">OnFire</value>
                    <value name="TargetEntityId" type="Int64">1</value>
                  </object>
                  <object type="Link">
                    <value name="InputEvent" type="String">OnReload</value>
                    <value name="TargetEntityId" type="Int64">2</value>
                  </object>
                </object>
              </object>
            </object>
          </object>
        </object>
        """).ToString();

    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Two_layers_adding_different_keys_share_one_data_component()
    {
        (string merged, IReadOnlyList<string> conflicts) = FcbMerge.Merge(
            Ancestor, Adding(DataComponent(Key("ModA.Key", 1))), Adding(DataComponent(Key("ModB.Key", 2))));

        Assert.Empty(conflicts);
        XElement component = Assert.Single(Components(merged), c => TypeOf(c) == "CFCSEDataComponent");
        Assert.Equal(["ModA.Key", "ModB.Key"], component.Elements("object").Select(TypeOf));
    }

    [Fact]
    public void Both_layers_adding_the_same_key_and_value_keep_it_once()
    {
        string both = Adding(DataComponent(Key("Shared.Key", 3)));

        (string merged, IReadOnlyList<string> conflicts) = FcbMerge.Merge(Ancestor, both, both);

        Assert.Empty(conflicts);
        XElement component = Assert.Single(Components(merged), c => TypeOf(c) == "CFCSEDataComponent");
        Assert.Equal("Shared.Key", TypeOf(Assert.Single(component.Elements("object"))));
    }

    /// <summary>The one real collision: theirs wins it, and the report says where it was.</summary>
    [Fact]
    public void The_same_key_with_different_values_conflicts_at_its_path()
    {
        (string merged, IReadOnlyList<string> conflicts) = FcbMerge.Merge(
            Ancestor, Adding(DataComponent(Key("Shared.Key", 1))), Adding(DataComponent(Key("Shared.Key", 2))));

        Assert.Equal(["Entity/Components/CFCSEDataComponent/Shared.Key"], conflicts);
        XElement key = Components(merged).Single(c => TypeOf(c) == "CFCSEDataComponent").Element("object")!;
        Assert.Equal("2", key.Element("value")!.Value);
    }

    /// <summary>A key is one value: given two types, it is not merged into a key holding both.</summary>
    [Fact]
    public void The_same_key_with_different_types_conflicts_as_one_value()
    {
        (string merged, IReadOnlyList<string> conflicts) = FcbMerge.Merge(
            Ancestor, Adding(DataComponent(Key("Shared.Key", 1))), Adding(DataComponent(Key("Shared.Key", 1f))));

        Assert.Equal(["Entity/Components/CFCSEDataComponent/Shared.Key"], conflicts);
        XElement key = Components(merged).Single(c => TypeOf(c) == "CFCSEDataComponent").Element("object")!;
        Assert.Equal("Float", (string?)Assert.Single(key.Elements("value")).Attribute("name"));
    }

    [Fact]
    public void Two_layers_adding_different_components_keep_both_in_fold_order()
    {
        (string merged, IReadOnlyList<string> conflicts) = FcbMerge.Merge(
            Ancestor, Adding(DataComponent(Key("ModA.Key", 1))), Adding(new XElement("object", new XAttribute("type", "CSoundComponent"))));

        Assert.Empty(conflicts);
        Assert.Equal(["CEventComponent", "CFCSEDataComponent", "CSoundComponent"], Components(merged).Select(TypeOf));
    }

    [Fact]
    public void Edits_to_different_records_of_a_list_both_land()
    {
        string ours = Edit(Ancestor, root => Event(Links(root).First()).Value = "OnFireStart");
        string theirs = Edit(Ancestor, root => Event(Links(root).Last()).Value = "OnReloadEnd");

        (string merged, IReadOnlyList<string> conflicts) = FcbMerge.Merge(Ancestor, ours, theirs);

        Assert.Empty(conflicts);
        Assert.Equal(["OnFireStart", "OnReloadEnd"], Events(merged));
    }

    [Fact]
    public void Two_layers_appending_to_a_list_keep_both_records_in_fold_order()
    {
        string ours = Edit(Ancestor, root => Links(root).Last().AddAfterSelf(Link("OnJam", 3)));
        string theirs = Edit(Ancestor, root => Links(root).Last().AddAfterSelf(Link("OnEmpty", 4)));

        (string merged, IReadOnlyList<string> conflicts) = FcbMerge.Merge(Ancestor, ours, theirs);

        Assert.Empty(conflicts);
        Assert.Equal(["OnFire", "OnReload", "OnJam", "OnEmpty"], Events(merged));
    }

    [Fact]
    public void Both_layers_appending_the_same_record_keep_it_once()
    {
        string ours = Edit(Ancestor, root => Links(root).Last().AddAfterSelf(Link("OnJam", 3)));
        string theirs = Edit(Ancestor, root =>
        {
            Links(root).Last().AddAfterSelf(Link("OnJam", 3));
            Target(Links(root).First()).Value = "7";
        });

        (string merged, IReadOnlyList<string> conflicts) = FcbMerge.Merge(Ancestor, ours, theirs);

        Assert.Empty(conflicts);
        Assert.Equal(["OnFire", "OnReload", "OnJam"], Events(merged));
        Assert.Equal("7", Target(Links(XElement.Parse(merged)).First()).Value);
    }

    /// <summary>A record whose fields one layer lists in another order is still the same record, so
    /// an insertion beside it does not throw off which ancestor record the other layer edited.</summary>
    [Fact]
    public void A_record_matches_its_ancestor_whatever_order_its_fields_are_in()
    {
        string ours = Edit(Ancestor, root =>
        {
            foreach (XElement link in Links(root))
            {
                XElement first = link.Elements("value").First();
                first.Remove();
                link.Add(first);
            }
            Links(root).First().AddBeforeSelf(Link("OnDraw", 5));
        });
        string theirs = Edit(Ancestor, root => Target(Links(root).Last()).Value = "9");

        (string merged, IReadOnlyList<string> conflicts) = FcbMerge.Merge(Ancestor, ours, theirs);

        Assert.Empty(conflicts);
        Assert.Equal(["OnDraw", "OnFire", "OnReload"], Events(merged));
        Assert.Equal("9", Target(Links(XElement.Parse(merged)).Last()).Value);
    }

    [Fact]
    public void Both_layers_editing_one_record_differently_conflict_at_its_index()
    {
        string ours = Edit(Ancestor, root => Event(Links(root).Last()).Value = "OnReloadStart");
        string theirs = Edit(Ancestor, root => Event(Links(root).Last()).Value = "OnReloadEnd");

        (string merged, IReadOnlyList<string> conflicts) = FcbMerge.Merge(Ancestor, ours, theirs);

        Assert.Equal(["Entity/Components/CEventComponent/hidLinks/Link[1]/InputEvent"], conflicts);
        Assert.Equal(["OnFire", "OnReloadEnd"], Events(merged));
    }

    /// <summary>An archetype can hold several occlusion volumes; they are a list, and the components
    /// beside them still pair by type.</summary>
    [Fact]
    public void Repeated_components_leave_entity_data_to_merge_by_key()
    {
        string withVolumes = Edit(Ancestor, root => At(root, "Entity/Components").Add(Volume(1), Volume(2)));

        (string merged, IReadOnlyList<string> conflicts) = FcbMerge.Merge(withVolumes,
            Adding(DataComponent(Key("ModA.Key", 1)), withVolumes), Adding(DataComponent(Key("ModB.Key", 2)), withVolumes));

        Assert.Empty(conflicts);
        XElement component = Assert.Single(Components(merged), c => TypeOf(c) == "CFCSEDataComponent");
        Assert.Equal(["ModA.Key", "ModB.Key"], component.Elements("object").Select(TypeOf));
        Assert.Equal(2, Components(merged).Count(c => TypeOf(c) == "CVisibilityOcclusionVolumeComponent"));
    }

    /// <summary>The case that motivated the tree merge, through the build's own fold and splice.</summary>
    [Fact]
    public void Two_mods_adding_entity_data_to_one_archetype_build_both_keys()
    {
        byte[] library = Library();
        string id = FragmentIdOf(library);

        Dictionary<string, string> resolved = Resolve(library, null,
            Stage("mod_a", id, Adding(DataComponent(Key("ModA.Key", 1)))),
            Stage("mod_b", id, Adding(DataComponent(Key("ModB.Key", 2)))));

        FcbObject archetype = FcbFragments.Find(FcbDocument.Deserialize(Splitter.Apply(library, resolved)), id)!;
        FcbObject data = FcbEntityFields.FindComponent(
            archetype.Children.Single(c => c.TypeHash == WorldHashes.Entity), WorldHashes.CFCSEDataComponent)!;
        Assert.Equal([Hash("ModA.Key"), Hash("ModB.Key")], data.Children.Select(c => c.TypeHash));
    }

    [Fact]
    public void A_build_reports_the_path_of_a_collision()
    {
        byte[] library = Library();
        string id = FragmentIdOf(library);
        FolderModLayer modA = Stage("mod_a", id, Adding(DataComponent(Key("Shared.Key", 1))));
        FolderModLayer modB = Stage("mod_b", id, Adding(DataComponent(Key("Shared.Key", 2))));
        // Names the class definitions do not know are rendered as their hashes.
        string path = string.Join('/', new[] { "Entity", "Components", "CFCSEDataComponent", "Shared.Key" }
            .Select(name => Hash(name).ToString("X8")));

        var conflicts = new ConcurrentQueue<ModConflict>();
        Resolve(library, conflicts, modA, modB);

        ModConflict reported = Assert.Single(conflicts);
        Assert.Equal([path], reported.Paths);
        Assert.Contains(path, reported.Describe(), StringComparison.Ordinal);
        Assert.Contains(path, Assert.Throws<InvalidDataException>(() => Resolve(library, null, modA, modB)).Message,
            StringComparison.Ordinal);
    }

    /// <summary>FCSE would silently keep the last copy, so even a layer nobody merges with is refused.</summary>
    [Fact]
    public void A_key_listed_twice_is_an_error_naming_the_mod_and_the_key()
    {
        byte[] library = Library();
        string id = FragmentIdOf(library);
        FolderModLayer mod = Stage("mod_a", id, Adding(DataComponent(Key("Twice.Key", 1), Key("Twice.Key", 2))));

        string message = Assert.Throws<InvalidDataException>(() => Resolve(library, null, mod)).Message;

        Assert.Contains("mod_a", message, StringComparison.Ordinal);
        Assert.Contains("'Twice.Key' 2 times", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_layer_passes_through_byte_identical()
    {
        byte[] library = Library();
        string id = FragmentIdOf(library);
        string edited = Adding(DataComponent(Key("ModA.Key", 1)));

        Dictionary<string, string> resolved = Resolve(library, null, Stage("mod_a", id, edited));

        Assert.Equal(Splitter.Canonicalize(id, edited), Assert.Single(resolved).Value);
    }

    private static uint Hash(string name) => FcbClassDefinitions.Crc32Ascii(name);

    private static string? TypeOf(XElement element) => (string?)element.Attribute("type");

    private static XElement At(XElement root, string path)
        => path.Split('/').Aggregate(root, (parent, type) => parent.Elements("object").Single(c => TypeOf(c) == type));

    private static IEnumerable<XElement> Components(string xml) => At(XElement.Parse(xml), "Entity/Components").Elements("object");

    private static IEnumerable<XElement> Links(XElement root)
        => At(root, "Entity/Components/CEventComponent/hidLinks").Elements("object");

    private static IEnumerable<string> Events(string xml) => Links(XElement.Parse(xml)).Select(link => Event(link).Value);

    private static XElement Event(XElement link) => ValueOf(link, "InputEvent");

    private static XElement Target(XElement link) => ValueOf(link, "TargetEntityId");

    private static XElement ValueOf(XElement parent, string name)
        => parent.Elements("value").Single(v => (string?)v.Attribute("name") == name);

    private static string Edit(string xml, Action<XElement> edit)
    {
        XElement root = XElement.Parse(xml);
        edit(root);
        return root.ToString();
    }

    private static string Adding(XElement component, string? onto = null)
        => Edit(onto ?? Ancestor, root => At(root, "Entity/Components").Add(component));

    private static XElement DataComponent(params XElement[] keys)
        => new("object", new XAttribute("type", "CFCSEDataComponent"), keys);

    private static XElement Key(string name, int value)
        => new("object", new XAttribute("type", name),
            new XElement("value", new XAttribute("name", "Int"), new XAttribute("type", "Int32"), value));

    private static XElement Key(string name, float value)
        => new("object", new XAttribute("type", name),
            new XElement("value", new XAttribute("name", "Float"), new XAttribute("type", "Float"), value));

    private static XElement Link(string inputEvent, long target)
        => new("object", new XAttribute("type", "Link"),
            new XElement("value", new XAttribute("name", "InputEvent"), new XAttribute("type", "String"), inputEvent),
            new XElement("value", new XAttribute("name", "TargetEntityId"), new XAttribute("type", "Int64"), target));

    private static XElement Volume(float radius)
        => new("object", new XAttribute("type", "CVisibilityOcclusionVolumeComponent"),
            new XElement("value", new XAttribute("name", "fRadius"), new XAttribute("type", "Float"), radius));

    /// <summary>An entity library holding just <see cref="Ancestor"/>.</summary>
    private static byte[] Library()
    {
        var group = new XElement("object", new XAttribute("type", "EntityLibrary"),
            new XElement("value", new XAttribute("name", "Name"), new XAttribute("type", "String"), "Test"),
            XElement.Parse(Ancestor));
        return FcbDocument.Serialize(FcbXml.FromXml(new XElement("object", new XAttribute("type", "EntityLibraries"), group).ToString()));
    }

    private static string FragmentIdOf(byte[] library) => Assert.Single(Splitter.Open(library).List()).Id;

    private static Dictionary<string, string> Resolve(
        byte[] library, ConcurrentQueue<ModConflict>? conflicts, params FolderModLayer[] layers)
        => TestSupport.ResolveFragments(Splitter, library, ContainerPath, conflicts, layers);

    private FolderModLayer Stage(string name, string id, string xml)
        => TestSupport.StageFragment(_sandbox, name, ContainerPath, id, xml);
}
