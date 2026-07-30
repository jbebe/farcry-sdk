using Domino.Core.Nodes;

namespace Domino.Core.Tests;

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

    [Fact]
    public void Every_real_system_node_has_a_reflection_box_that_parses()
    {
        string dir = Path.Combine("Fixtures", "Domino", "system");
        if (!Directory.Exists(dir)) return;

        var files = Directory.EnumerateFiles(dir, "*.lua", SearchOption.AllDirectories).ToList();
        Assert.True(files.Count > 0, "Fixture corpus is present but empty.");

        var failures = new List<string>();
        foreach (var file in files)
        {
            try
            {
                var reflection = ReflectionBoxParser.Parse(DominoLuaSource.Parse(File.ReadAllText(file)));
                if (reflection is null)
                {
                    failures.Add($"{file}: no reflection box found");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{file}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count}/{files.Count} files failed:\n" + string.Join('\n', failures.Take(10)));
    }
}
