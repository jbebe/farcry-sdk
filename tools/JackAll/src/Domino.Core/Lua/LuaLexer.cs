using System.Text;

namespace Domino.Core.Lua;

/// <summary>
/// Tokenizes the Lua dialect Far Cry 2's Domino scripts are written in (a real, historical "Lua 4.1
/// alpha" branch per <c>docs/docs/engine-internals/architecture.md</c> — but every generated file in
/// the real corpus turned out to be plain source text, not compiled bytecode, so this is an ordinary
/// lexer, not a bytecode reader). Comments are emitted as real tokens rather than skipped, because a
/// <c>system\</c> node's pin metadata (<c>-- DOMINO REFLECTION BOX ...</c>) lives inside them — see
/// <see cref="ReflectionBoxParser"/>.
/// </summary>
public sealed class LuaLexer
{
    private static readonly Dictionary<string, LuaTokenType> Keywords = new()
    {
        ["and"] = LuaTokenType.KwAnd, ["break"] = LuaTokenType.KwBreak, ["do"] = LuaTokenType.KwDo,
        ["else"] = LuaTokenType.KwElse, ["elseif"] = LuaTokenType.KwElseif, ["end"] = LuaTokenType.KwEnd,
        ["false"] = LuaTokenType.KwFalse, ["for"] = LuaTokenType.KwFor, ["function"] = LuaTokenType.KwFunction,
        ["if"] = LuaTokenType.KwIf, ["in"] = LuaTokenType.KwIn, ["local"] = LuaTokenType.KwLocal,
        ["nil"] = LuaTokenType.KwNil, ["not"] = LuaTokenType.KwNot, ["or"] = LuaTokenType.KwOr,
        ["repeat"] = LuaTokenType.KwRepeat, ["return"] = LuaTokenType.KwReturn, ["then"] = LuaTokenType.KwThen,
        ["true"] = LuaTokenType.KwTrue, ["until"] = LuaTokenType.KwUntil, ["while"] = LuaTokenType.KwWhile,
    };

    private readonly string _src;
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    public LuaLexer(string source) => _src = source;

    public static List<LuaToken> Tokenize(string source)
    {
        var lexer = new LuaLexer(source);
        var tokens = new List<LuaToken>();
        LuaToken t;
        do
        {
            t = lexer.Next();
            tokens.Add(t);
        } while (t.Type != LuaTokenType.Eof);
        return tokens;
    }

    private char Cur => _pos < _src.Length ? _src[_pos] : '\0';
    private char Peek(int ahead = 1) => _pos + ahead < _src.Length ? _src[_pos + ahead] : '\0';

    private void Advance()
    {
        if (Cur == '\n')
        {
            _line++;
            _col = 1;
        }
        else
        {
            _col++;
        }
        _pos++;
    }

    private LuaToken Next()
    {
        SkipWhitespace();
        int line = _line, col = _col;

        if (_pos >= _src.Length)
        {
            return new LuaToken(LuaTokenType.Eof, "", line, col);
        }

        char c = Cur;

        if (c == '-' && Peek() == '-')
        {
            return ReadComment(line, col);
        }

        if (char.IsLetter(c) || c == '_')
        {
            return ReadIdentifierOrKeyword(line, col);
        }

        if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek())))
        {
            return ReadNumber(line, col);
        }

        if (c is '"' or '\'')
        {
            return ReadQuotedString(line, col);
        }

        if (c == '[' && (Peek() == '[' || Peek() == '='))
        {
            var longStr = TryReadLongBracketString(line, col);
            if (longStr is { } ls)
            {
                return ls;
            }
        }

        return ReadSymbol(line, col);
    }

    private void SkipWhitespace()
    {
        while (_pos < _src.Length && (Cur is ' ' or '\t' or '\r' or '\n'))
        {
            Advance();
        }
    }

    private LuaToken ReadComment(int line, int col)
    {
        // Already positioned at the first '-' of "--".
        Advance();
        Advance();

        // Long comment: --[[ ... ]] or --[=[ ... ]=] etc.
        if (Cur == '[')
        {
            int save = _pos, saveLine = _line, saveCol = _col;
            int level = 0;
            int probe = _pos + 1;
            while (probe < _src.Length && _src[probe] == '=')
            {
                level++;
                probe++;
            }
            if (probe < _src.Length && _src[probe] == '[')
            {
                // Consume the opening [==[
                while (_pos <= probe)
                {
                    Advance();
                }
                string closer = "]" + new string('=', level) + "]";
                var sb = new StringBuilder();
                int closeIdx = _src.IndexOf(closer, _pos, StringComparison.Ordinal);
                if (closeIdx < 0)
                {
                    // Unterminated - consume to EOF, still emit as a comment rather than throwing.
                    while (_pos < _src.Length)
                    {
                        sb.Append(Cur);
                        Advance();
                    }
                    return new LuaToken(LuaTokenType.Comment, sb.ToString(), line, col);
                }
                while (_pos < closeIdx)
                {
                    sb.Append(Cur);
                    Advance();
                }
                for (int i = 0; i < closer.Length; i++)
                {
                    Advance();
                }
                return new LuaToken(LuaTokenType.Comment, sb.ToString(), line, col);
            }
            // Not actually a long-bracket opener - fall through to line comment, restore position.
            _pos = save;
            _line = saveLine;
            _col = saveCol;
        }

        var lineSb = new StringBuilder();
        while (_pos < _src.Length && Cur != '\n')
        {
            lineSb.Append(Cur);
            Advance();
        }
        return new LuaToken(LuaTokenType.Comment, lineSb.ToString(), line, col);
    }

    private LuaToken ReadIdentifierOrKeyword(int line, int col)
    {
        int start = _pos;
        while (_pos < _src.Length && (char.IsLetterOrDigit(Cur) || Cur == '_'))
        {
            Advance();
        }
        string text = _src[start.._pos];
        return Keywords.TryGetValue(text, out var kw)
            ? new LuaToken(kw, text, line, col)
            : new LuaToken(LuaTokenType.Identifier, text, line, col);
    }

    private LuaToken ReadNumber(int line, int col)
    {
        int start = _pos;
        if (Cur == '0' && (Peek() is 'x' or 'X'))
        {
            Advance();
            Advance();
            while (_pos < _src.Length && Uri.IsHexDigit(Cur))
            {
                Advance();
            }
            return new LuaToken(LuaTokenType.Number, _src[start.._pos], line, col);
        }

        while (_pos < _src.Length && char.IsDigit(Cur))
        {
            Advance();
        }
        if (Cur == '.')
        {
            Advance();
            while (_pos < _src.Length && char.IsDigit(Cur))
            {
                Advance();
            }
        }
        if (Cur is 'e' or 'E')
        {
            Advance();
            if (Cur is '+' or '-')
            {
                Advance();
            }
            while (_pos < _src.Length && char.IsDigit(Cur))
            {
                Advance();
            }
        }
        return new LuaToken(LuaTokenType.Number, _src[start.._pos], line, col);
    }

    private LuaToken ReadQuotedString(int line, int col)
    {
        char quote = Cur;
        Advance();
        var sb = new StringBuilder();
        while (_pos < _src.Length && Cur != quote)
        {
            if (Cur == '\\')
            {
                Advance();
                sb.Append(Cur switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    'a' => '\a',
                    'b' => '\b',
                    'f' => '\f',
                    'v' => '\v',
                    '\\' => '\\',
                    '"' => '"',
                    '\'' => '\'',
                    '\n' => '\n',
                    _ => Cur,
                });
                Advance();
            }
            else
            {
                sb.Append(Cur);
                Advance();
            }
        }
        if (_pos < _src.Length)
        {
            Advance(); // closing quote
        }
        return new LuaToken(LuaTokenType.String, sb.ToString(), line, col);
    }

    private LuaToken? TryReadLongBracketString(int line, int col)
    {
        int save = _pos, saveLine = _line, saveCol = _col;
        int probe = _pos + 1;
        int level = 0;
        while (probe < _src.Length && _src[probe] == '=')
        {
            level++;
            probe++;
        }
        if (probe >= _src.Length || _src[probe] != '[')
        {
            return null;
        }

        while (_pos <= probe)
        {
            Advance();
        }
        string closer = "]" + new string('=', level) + "]";
        var sb = new StringBuilder();
        int closeIdx = _src.IndexOf(closer, _pos, StringComparison.Ordinal);
        if (closeIdx < 0)
        {
            _pos = save;
            _line = saveLine;
            _col = saveCol;
            return null;
        }
        while (_pos < closeIdx)
        {
            sb.Append(Cur);
            Advance();
        }
        for (int i = 0; i < closer.Length; i++)
        {
            Advance();
        }
        return new LuaToken(LuaTokenType.String, sb.ToString(), line, col);
    }

    private LuaToken ReadSymbol(int line, int col)
    {
        char c = Cur;
        char n = Peek();

        (LuaTokenType type, int len) = (c, n) switch
        {
            ('=', '=') => (LuaTokenType.Eq, 2),
            ('~', '=') => (LuaTokenType.NotEq, 2),
            ('<', '=') => (LuaTokenType.LessEq, 2),
            ('>', '=') => (LuaTokenType.GreaterEq, 2),
            ('.', '.') when Peek(2) == '.' => (LuaTokenType.DotDotDot, 3),
            ('.', '.') => (LuaTokenType.DotDot, 2),
            _ => (SingleCharType(c), 1),
        };

        string text = _src.Substring(_pos, len);
        for (int i = 0; i < len; i++)
        {
            Advance();
        }
        return new LuaToken(type, text, line, col);
    }

    private static LuaTokenType SingleCharType(char c) => c switch
    {
        '+' => LuaTokenType.Plus,
        '-' => LuaTokenType.Minus,
        '*' => LuaTokenType.Star,
        '/' => LuaTokenType.Slash,
        '%' => LuaTokenType.Percent,
        '^' => LuaTokenType.Caret,
        '#' => LuaTokenType.Hash,
        '<' => LuaTokenType.Less,
        '>' => LuaTokenType.Greater,
        '=' => LuaTokenType.Assign,
        '(' => LuaTokenType.LParen,
        ')' => LuaTokenType.RParen,
        '{' => LuaTokenType.LBrace,
        '}' => LuaTokenType.RBrace,
        '[' => LuaTokenType.LBracket,
        ']' => LuaTokenType.RBracket,
        ';' => LuaTokenType.Semicolon,
        ':' => LuaTokenType.Colon,
        ',' => LuaTokenType.Comma,
        '.' => LuaTokenType.Dot,
        _ => throw new FormatException($"Unexpected character '{c}' (0x{(int)c:X2})"),
    };
}
