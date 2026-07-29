using Domino.Core.Lua;

namespace Domino.Core.Tests;

public class LuaWriterTests
{
    [Fact]
    public void Writing_and_reparsing_a_hand_written_snippet_is_idempotent()
    {
        const string source = """
            function ForEach:In()
                if (self.Array ~= nil) then
                    for i,v in self.Array do
                        self.String = v; self:Out();
                    end
                end
                return self:Finished();
            end
            """;

        var chunk1 = LuaParser.Parse(source);
        string written1 = LuaWriter.Write(chunk1);

        var chunk2 = LuaParser.Parse(written1);
        string written2 = LuaWriter.Write(chunk2);

        Assert.Equal(written1, written2);
    }

    [Fact]
    public void String_escapes_round_trip()
    {
        var chunk = LuaParser.Parse("x = \"a\\nb\\tc\\\"d\";");
        string written = LuaWriter.Write(chunk);

        var reparsed = LuaParser.Parse(written);
        var assign = Assert.IsType<AssignStmt>(reparsed.Statements.Single());
        Assert.Equal("a\nb\tc\"d", Assert.IsType<StringExpr>(assign.Values[0]).Value);
    }

    [Fact]
    public void Operator_precedence_is_preserved_through_a_round_trip()
    {
        var chunk = LuaParser.Parse("x = -2^2;");
        string written = LuaWriter.Write(chunk);
        var reparsed = LuaParser.Parse(written);

        var assign = Assert.IsType<AssignStmt>(reparsed.Statements.Single());
        var unary = Assert.IsType<UnaryExpr>(assign.Values[0]);
        Assert.Equal("-", unary.Op);
        Assert.IsType<BinaryExpr>(unary.Operand);
    }

    [Fact]
    public void Comments_are_written_back_out()
    {
        var chunk = LuaParser.Parse("-- hello\nx = 1;");
        string written = LuaWriter.Write(chunk);
        Assert.Contains("-- hello", written);
    }

    [Fact]
    public void Every_real_extracted_domino_file_writes_and_reparses_stably()
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
                var chunk1 = LuaParser.Parse(File.ReadAllText(file));
                string written1 = LuaWriter.Write(chunk1);
                var chunk2 = LuaParser.Parse(written1);
                string written2 = LuaWriter.Write(chunk2);

                if (written1 != written2)
                {
                    failures.Add($"{file}: unstable round trip");
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
