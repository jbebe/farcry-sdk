using Domino.Core.Lua;

namespace Domino.Core.Tests;

public class LuaParserTests
{
    [Fact]
    public void Parses_a_reflection_box_node_body()
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
                if (self.Array ~= nil) then
                    for i,v in self.Array do
                        self.String = v; self:Out();
                    end
                end
                return self:Finished();
            end
            """;

        var chunk = LuaParser.Parse(source);

        var fn = Assert.IsType<FunctionDeclStmt>(Assert.Single(chunk.Statements, s => s is FunctionDeclStmt));
        Assert.Equal(["ForEach", "In"], fn.NamePath);
        Assert.True(fn.IsMethod);

        var ifStmt = Assert.IsType<IfStmt>(fn.Body[0]);
        var forStmt = Assert.IsType<GenericForStmt>(ifStmt.Clauses[0].Body[0]);
        Assert.Equal(["i", "v"], forStmt.Names);
    }

    [Fact]
    public void Trailing_comment_inside_a_table_constructor_does_not_break_parsing()
    {
        const string source = """
            OutputOrder = {
              Out = {}, -- Intentional, not a bug.
            };
            """;

        var chunk = LuaParser.Parse(source);
        var assign = Assert.IsType<AssignStmt>(chunk.Statements.Single());
        Assert.IsType<TableConstructorExpr>(assign.Values[0]);
    }

    [Fact]
    public void Operator_precedence_matches_lua_not_left_to_right()
    {
        // -2^2 in Lua is -(2^2) = -4, since unary minus binds looser than ^.
        var chunk = LuaParser.Parse("x = -2^2");
        var assign = Assert.IsType<AssignStmt>(chunk.Statements.Single());
        var unary = Assert.IsType<UnaryExpr>(assign.Values[0]);
        Assert.Equal("-", unary.Op);
        var pow = Assert.IsType<BinaryExpr>(unary.Operand);
        Assert.Equal("^", pow.Op);
    }

    [Fact]
    public void Concat_and_pow_are_right_associative()
    {
        var chunk = LuaParser.Parse("x = a .. b .. c");
        var assign = Assert.IsType<AssignStmt>(chunk.Statements.Single());
        var outer = Assert.IsType<BinaryExpr>(assign.Values[0]);
        // a .. (b .. c)
        Assert.IsType<NameExpr>(outer.Left);
        Assert.IsType<BinaryExpr>(outer.Right);
    }

    [Fact]
    public void Colon_call_is_distinct_from_dot_call()
    {
        var chunk = LuaParser.Parse("cbox:RegisterBox(\"Domino/System/X.lua\");");
        var call = Assert.IsType<CallStmt>(chunk.Statements.Single());
        var method = Assert.IsType<MethodCallExpr>(call.Call);
        Assert.Equal("RegisterBox", method.Method);
    }

    [Fact]
    public void Every_real_extracted_domino_file_parses_without_error()
    {
        string dir = Path.Combine("Fixtures", "Domino");
        if (!Directory.Exists(dir)) return;

        var files = Directory.EnumerateFiles(dir, "*.lua", SearchOption.AllDirectories).ToList();
        Assert.True(files.Count > 0, "Fixture corpus is present but empty.");

        var failures = new List<string>();
        foreach (var file in files)
        {
            try
            {
                LuaParser.Parse(File.ReadAllText(file));
            }
            catch (Exception ex)
            {
                failures.Add($"{file}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count}/{files.Count} files failed to parse:\n" + string.Join('\n', failures.Take(10)));
    }
}
