using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Nodes;

namespace JackAll.Tests;

public class ReflectionBoxParserTests
{
    [Fact]
    public void Parses_a_simple_node_pin_signature()
    {
        const string source = """
            -- DOMINO REFLECTION BOX START
            -- <Display Category="Change Variables" Text="ForEach"/>
            -- <ControlIn  Name="In"/>
            -- <DataIn     Name="Array" Type="Core|string"/>
            -- <ControlOut Name="Out"/>
            -- <ControlOut Name="Finished"/>
            -- <DataOut    Name="String" Type="Core|string"/>
            -- DOMINO REFLECTION BOX END

            function ForEach:In()
            end
            """;

        var reflection = ReflectionBoxParser.Parse(DominoLuaSource.Parse(source));

        Assert.NotNull(reflection);
        Assert.Equal(new NodeDisplay("Change Variables", "ForEach"), reflection.Display);
        Assert.Equal([new ControlInPin("In", Dynamic: false)], reflection.ControlIns);
        Assert.Equal([new DataInPin("Array", "Core|string")], reflection.DataIns);
        Assert.Equal(
            [new ControlOutPin("Out", Delayed: false, Dynamic: false), new ControlOutPin("Finished", Delayed: false, Dynamic: false)],
            reflection.ControlOuts);
        Assert.Equal([new DataOutPin("String", "Core|string")], reflection.DataOuts);
        Assert.False(reflection.Stateless);
    }

    [Fact]
    public void Parses_dynamic_and_stateless_attributes()
    {
        const string source = """
            -- DOMINO REFLECTION BOX START
            -- <Display Category="Script Flow" Text="Output Order"/>
            -- <ControlIn  Name="In"/>
            -- <ControlOut Name="Out"    Dynamic="True"/>
            -- <Stateless/>
            -- DOMINO REFLECTION BOX END
            """;

        var reflection = ReflectionBoxParser.Parse(DominoLuaSource.Parse(source));

        Assert.NotNull(reflection);
        Assert.True(reflection.Stateless);
        Assert.True(reflection.ControlOuts.Single().Dynamic);
    }

    [Fact]
    public void Returns_null_when_no_reflection_box_is_present()
    {
        var reflection = ReflectionBoxParser.Parse(DominoLuaSource.Parse("x = 1"));
        Assert.Null(reflection);
    }

    [Theory]
    [InlineData(DominoFixtures.ProximityTrigger)]
    [InlineData(DominoFixtures.BypassMissionStatus)]
    public void A_real_system_nodes_reflection_box_parses(string script)
    {
        if (Fixture.ReadText(script) is not { } source) return;

        Assert.NotNull(ReflectionBoxParser.Parse(DominoLuaSource.Parse(source)));
    }
}
