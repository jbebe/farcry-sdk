using Domino.Core.Lua;

namespace Domino.Core.Tests;

public class LuaLexerTests
{
    private static List<LuaToken> Tokens(string source) => LuaLexer.Tokenize(source);

    [Fact]
    public void Keywords_are_recognized_not_identifiers()
    {
        var tokens = Tokens("if then end");
        Assert.Equal([LuaTokenType.KwIf, LuaTokenType.KwThen, LuaTokenType.KwEnd, LuaTokenType.Eof],
            tokens.Select(t => t.Type));
    }

    [Fact]
    public void Multi_char_operators_are_not_split_into_singles()
    {
        var tokens = Tokens("a == b ~= c <= d >= e .. f ...");
        Assert.Equal(
            [LuaTokenType.Identifier, LuaTokenType.Eq, LuaTokenType.Identifier, LuaTokenType.NotEq,
             LuaTokenType.Identifier, LuaTokenType.LessEq, LuaTokenType.Identifier, LuaTokenType.GreaterEq,
             LuaTokenType.Identifier, LuaTokenType.DotDot, LuaTokenType.Identifier, LuaTokenType.DotDotDot,
             LuaTokenType.Eof],
            tokens.Select(t => t.Type));
    }

    [Fact]
    public void Line_comment_stops_at_newline()
    {
        var tokens = Tokens("-- hello\nx = 1");
        Assert.Equal(LuaTokenType.Comment, tokens[0].Type);
        Assert.Equal(" hello", tokens[0].Text);
        Assert.Equal(LuaTokenType.Identifier, tokens[1].Type);
    }

    [Fact]
    public void Long_bracket_comment_spans_multiple_lines()
    {
        var tokens = Tokens("--[[ line one\nline two ]] x = 1");
        Assert.Equal(LuaTokenType.Comment, tokens[0].Type);
        Assert.Contains("line one", tokens[0].Text);
        Assert.Contains("line two", tokens[0].Text);
        Assert.Equal(LuaTokenType.Identifier, tokens[1].Type);
    }

    [Fact]
    public void Long_bracket_string_with_equals_level_is_not_closed_by_a_shorter_bracket()
    {
        var tokens = Tokens("""x = [==[ a ]] still inside ]==]""");
        Assert.Equal(LuaTokenType.String, tokens[2].Type);
        Assert.Contains("still inside", tokens[2].Text);
    }

    [Fact]
    public void String_escapes_are_decoded()
    {
        var tokens = Tokens("x = \"a\\nb\\tc\\\"d\"");
        Assert.Equal("a\nb\tc\"d", tokens[2].Text);
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("3.14", "3.14")]
    [InlineData("0x1F", "0x1F")]
    [InlineData("1e10", "1e10")]
    [InlineData("1.5e-3", "1.5e-3")]
    public void Number_literals_are_captured_verbatim(string raw, string expected)
    {
        var tokens = Tokens(raw);
        Assert.Equal(LuaTokenType.Number, tokens[0].Type);
        Assert.Equal(expected, tokens[0].Text);
    }

    [Fact]
    public void Every_token_stream_ends_with_eof()
    {
        Assert.Equal(LuaTokenType.Eof, Tokens("").Single().Type);
        Assert.Equal(LuaTokenType.Eof, Tokens("x").Last().Type);
    }
}
