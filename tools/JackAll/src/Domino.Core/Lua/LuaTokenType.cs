namespace Domino.Core.Lua;

public enum LuaTokenType
{
    Eof,
    Identifier,
    Number,
    String,
    Comment,

    // Keywords
    KwAnd, KwBreak, KwDo, KwElse, KwElseif, KwEnd, KwFalse, KwFor, KwFunction,
    KwIf, KwIn, KwLocal, KwNil, KwNot, KwOr, KwRepeat, KwReturn, KwThen,
    KwTrue, KwUntil, KwWhile,

    // Symbols
    Plus, Minus, Star, Slash, Percent, Caret, Hash,
    Eq, NotEq, LessEq, GreaterEq, Less, Greater, Assign,
    LParen, RParen, LBrace, RBrace, LBracket, RBracket,
    Semicolon, Colon, Comma, Dot, DotDot, DotDotDot,
}
