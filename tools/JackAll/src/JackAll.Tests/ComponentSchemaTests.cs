using JackAll.Core.Format.Fcb;

namespace JackAll.Tests;

/// <summary>
/// The engine-registered component schema as it merges into <see cref="FcbClassDefinitions"/>: it adds
/// what binary_classes.xml lacks and never changes what the XML already says.
/// </summary>
public class ComponentSchemaTests
{
    private const string ClassesPath = "Fixtures/Fcb/binary_classes.xml";

    private static readonly string SchemaPath =
        Path.Combine(TestSupport.RepositoryRoot, "tools", "JackAll", "assets", "component_schema.json");

    private static readonly Lazy<ComponentSchema> Schema = new(() => ComponentSchema.Load(SchemaPath));

    private static uint H(string name) => FcbClassDefinitions.Crc32Ascii(name);

    private static FcbClassDefinitions Merged() => FcbClassDefinitions.Load(ClassesPath, Schema.Value);

    [Fact]
    public void Every_declared_wire_type_is_one_JackAll_decodes()
    {
        static IEnumerable<SchemaMember> Walk(IEnumerable<SchemaMember> members)
            => members.SelectMany(m => Walk(m.Members ?? []).Prepend(m));

        IEnumerable<string> types = Schema.Value.Classes.SelectMany(c => Walk(c.Members))
            .Select(m => m.Type).OfType<string>().Distinct();

        Assert.All(types, t => Assert.True(Enum.TryParse(t, out FcbMemberType _), t));
    }

    [Fact]
    public void The_xml_wins_every_member_both_define()
    {
        FcbClassDefinitions xml = FcbClassDefinitions.Load(ClassesPath);
        FcbClassDefinitions merged = Merged();

        foreach (SchemaClass schemaClass in Schema.Value.Classes)
        {
            FcbClass before = xml.GetClass(H(schemaClass.Name));
            FcbClass after = merged.GetClass(H(schemaClass.Name));
            foreach ((uint hash, FcbMember member) in before.AllMembers())
            {
                FcbMember? kept = after.FindMember(hash);
                Assert.Equal((member.Name, member.Type), (kept?.Name, kept?.Type));
            }
        }
    }

    [Fact]
    public void Every_schema_class_resolves_by_name_and_inherits_its_parents_members()
    {
        FcbClassDefinitions merged = Merged();

        Assert.All(Schema.Value.Classes, c => Assert.Equal(c.Name, merged.GetClass(H(c.Name)).Name));
        Assert.Equal("hidHasAliasName", merged.GetClass(H("CRigidPhysComponent")).FindMember(H("hidHasAliasName"))?.Name);
    }

    [Fact]
    public void An_enum_index_carries_its_labels()
    {
        FcbMember? layer = Merged().GetClass(H("CRigidPhysComponent")).FindMember(H("selCollisionLayer"));

        Assert.NotNull(layer?.Labels);
        Assert.NotEmpty(layer!.Labels!);
    }

    [Fact]
    public void An_embedded_class_puts_its_members_in_the_same_node()
    {
        FcbClass light = Merged().GetClass(H("CDynamicLightComponent"));

        Assert.Equal(FcbMemberType.Float, light.FindMember(H("fIntensity"))?.Type);
        Assert.Equal(FcbMemberType.Vector3, light.FindMember(H("clrColor"))?.Type);
    }

    [Fact]
    public void A_group_is_a_child_node_and_a_wrapped_container_names_its_elements()
    {
        FcbClassDefinitions merged = Merged();
        FcbClass boids = merged.GetClass(H("CBoidsComponent"));
        FcbClass events = merged.GetClass(H("CEventComponent"));

        Assert.Equal(FcbMemberType.Float, boids.Resolve(H("Banking")).FindMember(H("fBankingAmount"))?.Type);
        Assert.Equal("Link", events.Resolve(H("hidLinks")).Resolve(H("Link")).Name);
    }

    [Fact]
    public void A_component_lists_what_it_does_not_set_at_the_types_zero()
    {
        var light = new FcbObject { TypeHash = H("CDynamicLightComponent") };
        light.Values[H("fIntensity")] = BitConverter.GetBytes(2f);

        List<Tools.World.MergedField> unset = [.. Tools.World.MergedNode.Of(light, null)
            .UnsetFields(Merged().GetClass(light.TypeHash))];

        Assert.DoesNotContain(unset, f => f.Hash == H("fIntensity"));
        Tools.World.MergedField color = Assert.Single(unset, f => f.Hash == H("clrColor"));
        Assert.Equal(new byte[12], color.Value);
        Assert.Equal(Tools.World.FieldOrigin.Unset, color.Origin);
    }

    [Fact]
    public void Only_registered_components_are_offered_as_creatable()
    {
        List<SchemaClass> creatable = [.. Schema.Value.Classes.Where(c => c.IsComponent && c.Creatable)];

        Assert.Contains(creatable, c => c.Name == "CDynamicLightComponent");
        Assert.DoesNotContain(creatable, c => c.Name == "CEntityComponent");
    }
}
