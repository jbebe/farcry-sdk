namespace Domino.Core.Lua;

/// <summary>One lexed token, with enough position info for error messages and comment reattachment.</summary>
public readonly record struct LuaToken(LuaTokenType Type, string Text, int Line, int Column)
{
    public override string ToString() => $"{Type} '{Text}' @{Line}:{Column}";
}
