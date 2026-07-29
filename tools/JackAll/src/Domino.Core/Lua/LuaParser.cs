namespace Domino.Core.Lua;

/// <summary>
/// A recursive-descent parser for the small Lua subset Domino scripts actually use. Comments are
/// interleaved into the statement list as <see cref="CommentStmt"/> rather than discarded, so a
/// `system\` node's reflection-box header (and anything else) round-trips.
/// </summary>
public sealed class LuaParser
{
    private readonly List<LuaToken> _tokens;
    private int _pos;

    private LuaParser(List<LuaToken> tokens) => _tokens = tokens;

    public static LuaChunk Parse(string source)
    {
        var tokens = StripNestedComments(LuaLexer.Tokenize(source));
        var parser = new LuaParser(tokens);
        var stmts = parser.ParseStatementsUntil(LuaTokenType.Eof);
        parser.Expect(LuaTokenType.Eof);
        return new LuaChunk(stmts);
    }

    /// <summary>
    /// Drops comment tokens that fall inside `(...)`/`{...}`/`[...]` nesting — e.g. a trailing
    /// `Out = {}, -- Intentional, not a bug.` field comment in a table constructor. Only
    /// <see cref="ParseStatement"/> understands comment tokens (as <see cref="CommentStmt"/>); nothing
    /// in expression parsing expects one to appear mid-expression. Comments between statements at
    /// bracket depth 0 — which is where reflection-box metadata and doc headers actually live — are
    /// left untouched.
    /// </summary>
    private static List<LuaToken> StripNestedComments(List<LuaToken> tokens)
    {
        var result = new List<LuaToken>(tokens.Count);
        int depth = 0;
        foreach (var tok in tokens)
        {
            switch (tok.Type)
            {
                case LuaTokenType.LParen or LuaTokenType.LBrace or LuaTokenType.LBracket:
                    depth++;
                    break;
                case LuaTokenType.RParen or LuaTokenType.RBrace or LuaTokenType.RBracket:
                    depth--;
                    break;
            }

            if (tok.Type == LuaTokenType.Comment && depth > 0)
            {
                continue;
            }
            result.Add(tok);
        }
        return result;
    }

    private LuaToken Cur => _tokens[_pos];
    private LuaTokenType CurType => Cur.Type;

    private LuaToken Advance()
    {
        var t = _tokens[_pos];
        if (_pos < _tokens.Count - 1)
        {
            _pos++;
        }
        return t;
    }

    private bool Check(LuaTokenType type) => CurType == type;

    private bool Match(LuaTokenType type)
    {
        if (!Check(type))
        {
            return false;
        }
        Advance();
        return true;
    }

    private LuaToken Expect(LuaTokenType type)
    {
        if (!Check(type))
        {
            throw new FormatException($"Expected {type} but found {Cur} at line {Cur.Line}");
        }
        return Advance();
    }

    // --- Statements ------------------------------------------------------------------------

    private static readonly HashSet<LuaTokenType> BlockEnders = new()
    {
        LuaTokenType.Eof, LuaTokenType.KwEnd, LuaTokenType.KwElse, LuaTokenType.KwElseif,
        LuaTokenType.KwUntil,
    };

    private List<LuaStmt> ParseStatementsUntil(params LuaTokenType[] enders)
    {
        var enderSet = enders.Length > 0 ? new HashSet<LuaTokenType>(enders) : BlockEnders;
        var stmts = new List<LuaStmt>();
        while (!enderSet.Contains(CurType) && CurType != LuaTokenType.Eof)
        {
            var stmt = ParseStatement();
            if (stmt is not null)
            {
                stmts.Add(stmt);
            }
        }
        return stmts;
    }

    private LuaStmt? ParseStatement()
    {
        switch (CurType)
        {
            case LuaTokenType.Comment:
            {
                var tok = Advance();
                bool isLong = tok.Text.Contains('\n');
                return new CommentStmt(tok.Text, isLong);
            }
            case LuaTokenType.Semicolon:
                Advance();
                return null;
            case LuaTokenType.KwLocal:
                return ParseLocal();
            case LuaTokenType.KwFunction:
                return ParseFunctionDecl();
            case LuaTokenType.KwIf:
                return ParseIf();
            case LuaTokenType.KwFor:
                return ParseFor();
            case LuaTokenType.KwWhile:
                return ParseWhile();
            case LuaTokenType.KwRepeat:
                return ParseRepeat();
            case LuaTokenType.KwDo:
            {
                Advance();
                var body = ParseStatementsUntil(LuaTokenType.KwEnd);
                Expect(LuaTokenType.KwEnd);
                return new DoStmt(body);
            }
            case LuaTokenType.KwReturn:
            {
                Advance();
                var values = new List<LuaExpr>();
                if (!BlockEnders.Contains(CurType) && CurType != LuaTokenType.Semicolon)
                {
                    values.Add(ParseExpr());
                    while (Match(LuaTokenType.Comma))
                    {
                        values.Add(ParseExpr());
                    }
                }
                Match(LuaTokenType.Semicolon);
                return new ReturnStmt(values);
            }
            case LuaTokenType.KwBreak:
                Advance();
                return new BreakStmt();
            default:
                return ParseExprStatement();
        }
    }

    private LuaStmt ParseLocal()
    {
        Expect(LuaTokenType.KwLocal);
        var names = new List<string> { Expect(LuaTokenType.Identifier).Text };
        while (Match(LuaTokenType.Comma))
        {
            names.Add(Expect(LuaTokenType.Identifier).Text);
        }
        var values = new List<LuaExpr>();
        if (Match(LuaTokenType.Assign))
        {
            values.Add(ParseExpr());
            while (Match(LuaTokenType.Comma))
            {
                values.Add(ParseExpr());
            }
        }
        Match(LuaTokenType.Semicolon);
        return new LocalStmt(names, values);
    }

    private LuaStmt ParseFunctionDecl()
    {
        Expect(LuaTokenType.KwFunction);
        var path = new List<string> { Expect(LuaTokenType.Identifier).Text };
        while (Match(LuaTokenType.Dot))
        {
            path.Add(Expect(LuaTokenType.Identifier).Text);
        }
        bool isMethod = false;
        if (Match(LuaTokenType.Colon))
        {
            path.Add(Expect(LuaTokenType.Identifier).Text);
            isMethod = true;
        }

        Expect(LuaTokenType.LParen);
        var parameters = new List<string>();
        if (!Check(LuaTokenType.RParen))
        {
            parameters.Add(ParseParam());
            while (Match(LuaTokenType.Comma))
            {
                parameters.Add(ParseParam());
            }
        }
        Expect(LuaTokenType.RParen);

        var body = ParseStatementsUntil(LuaTokenType.KwEnd);
        Expect(LuaTokenType.KwEnd);
        Match(LuaTokenType.Semicolon);
        return new FunctionDeclStmt(path, isMethod, parameters, body);
    }

    private string ParseParam()
    {
        if (Match(LuaTokenType.DotDotDot))
        {
            return "...";
        }
        return Expect(LuaTokenType.Identifier).Text;
    }

    private LuaStmt ParseIf()
    {
        Expect(LuaTokenType.KwIf);
        var clauses = new List<IfClause>();
        var cond = ParseExpr();
        Expect(LuaTokenType.KwThen);
        var body = ParseStatementsUntil(LuaTokenType.KwEnd, LuaTokenType.KwElse, LuaTokenType.KwElseif);
        clauses.Add(new IfClause(cond, body));

        while (Check(LuaTokenType.KwElseif))
        {
            Advance();
            var c2 = ParseExpr();
            Expect(LuaTokenType.KwThen);
            var b2 = ParseStatementsUntil(LuaTokenType.KwEnd, LuaTokenType.KwElse, LuaTokenType.KwElseif);
            clauses.Add(new IfClause(c2, b2));
        }

        List<LuaStmt>? elseBody = null;
        if (Match(LuaTokenType.KwElse))
        {
            elseBody = ParseStatementsUntil(LuaTokenType.KwEnd);
        }
        Expect(LuaTokenType.KwEnd);
        Match(LuaTokenType.Semicolon);
        return new IfStmt(clauses, elseBody);
    }

    private LuaStmt ParseFor()
    {
        Expect(LuaTokenType.KwFor);
        string firstName = Expect(LuaTokenType.Identifier).Text;

        if (Match(LuaTokenType.Assign))
        {
            var start = ParseExpr();
            Expect(LuaTokenType.Comma);
            var stop = ParseExpr();
            LuaExpr? step = null;
            if (Match(LuaTokenType.Comma))
            {
                step = ParseExpr();
            }
            Expect(LuaTokenType.KwDo);
            var body = ParseStatementsUntil(LuaTokenType.KwEnd);
            Expect(LuaTokenType.KwEnd);
            Match(LuaTokenType.Semicolon);
            return new NumericForStmt(firstName, start, stop, step, body);
        }

        var names = new List<string> { firstName };
        while (Match(LuaTokenType.Comma))
        {
            names.Add(Expect(LuaTokenType.Identifier).Text);
        }
        Expect(LuaTokenType.KwIn);
        var iterators = new List<LuaExpr> { ParseExpr() };
        while (Match(LuaTokenType.Comma))
        {
            iterators.Add(ParseExpr());
        }
        Expect(LuaTokenType.KwDo);
        var genBody = ParseStatementsUntil(LuaTokenType.KwEnd);
        Expect(LuaTokenType.KwEnd);
        Match(LuaTokenType.Semicolon);
        return new GenericForStmt(names, iterators, genBody);
    }

    private LuaStmt ParseWhile()
    {
        Expect(LuaTokenType.KwWhile);
        var cond = ParseExpr();
        Expect(LuaTokenType.KwDo);
        var body = ParseStatementsUntil(LuaTokenType.KwEnd);
        Expect(LuaTokenType.KwEnd);
        Match(LuaTokenType.Semicolon);
        return new WhileStmt(cond, body);
    }

    private LuaStmt ParseRepeat()
    {
        Expect(LuaTokenType.KwRepeat);
        var body = ParseStatementsUntil(LuaTokenType.KwUntil);
        Expect(LuaTokenType.KwUntil);
        var cond = ParseExpr();
        Match(LuaTokenType.Semicolon);
        return new RepeatStmt(body, cond);
    }

    /// <summary>An assignment (`a, b = 1, 2`) or a bare call statement (`self:Out();`) — both start
    /// with a "suffixed expression" (name followed by any run of `.field`/`[expr]`/`(args)`/`:m(args)`),
    /// and Lua only disambiguates once it sees whether an `=`/`,` follows.</summary>
    private LuaStmt ParseExprStatement()
    {
        var first = ParseSuffixedExpr();
        if (Check(LuaTokenType.Assign) || Check(LuaTokenType.Comma))
        {
            var targets = new List<LuaExpr> { first };
            while (Match(LuaTokenType.Comma))
            {
                targets.Add(ParseSuffixedExpr());
            }
            Expect(LuaTokenType.Assign);
            var values = new List<LuaExpr> { ParseExpr() };
            while (Match(LuaTokenType.Comma))
            {
                values.Add(ParseExpr());
            }
            Match(LuaTokenType.Semicolon);
            return new AssignStmt(targets, values);
        }

        Match(LuaTokenType.Semicolon);
        if (first is CallExpr or MethodCallExpr)
        {
            return new CallStmt(first);
        }
        throw new FormatException($"Expected assignment or call statement at line {Cur.Line}, got expression {first}");
    }

    // --- Expressions -------------------------------------------------------------------------

    private LuaExpr ParseExpr() => ParseOr();

    private LuaExpr ParseOr()
    {
        var left = ParseAnd();
        while (Check(LuaTokenType.KwOr))
        {
            Advance();
            left = new BinaryExpr("or", left, ParseAnd());
        }
        return left;
    }

    private LuaExpr ParseAnd()
    {
        var left = ParseComparison();
        while (Check(LuaTokenType.KwAnd))
        {
            Advance();
            left = new BinaryExpr("and", left, ParseComparison());
        }
        return left;
    }

    private static readonly Dictionary<LuaTokenType, string> ComparisonOps = new()
    {
        [LuaTokenType.Less] = "<", [LuaTokenType.Greater] = ">", [LuaTokenType.LessEq] = "<=",
        [LuaTokenType.GreaterEq] = ">=", [LuaTokenType.NotEq] = "~=", [LuaTokenType.Eq] = "==",
    };

    private LuaExpr ParseComparison()
    {
        var left = ParseConcat();
        while (ComparisonOps.TryGetValue(CurType, out var op))
        {
            Advance();
            left = new BinaryExpr(op, left, ParseConcat());
        }
        return left;
    }

    private LuaExpr ParseConcat()
    {
        var left = ParseAdditive();
        if (Check(LuaTokenType.DotDot))
        {
            Advance();
            return new BinaryExpr("..", left, ParseConcat()); // right-assoc
        }
        return left;
    }

    private LuaExpr ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (CurType is LuaTokenType.Plus or LuaTokenType.Minus)
        {
            string op = Advance().Type == LuaTokenType.Plus ? "+" : "-";
            left = new BinaryExpr(op, left, ParseMultiplicative());
        }
        return left;
    }

    private LuaExpr ParseMultiplicative()
    {
        var left = ParseUnary();
        while (CurType is LuaTokenType.Star or LuaTokenType.Slash or LuaTokenType.Percent)
        {
            string op = Advance().Type switch { LuaTokenType.Star => "*", LuaTokenType.Slash => "/", _ => "%" };
            left = new BinaryExpr(op, left, ParseUnary());
        }
        return left;
    }

    private LuaExpr ParseUnary()
    {
        if (CurType is LuaTokenType.KwNot or LuaTokenType.Hash or LuaTokenType.Minus)
        {
            string op = Advance().Type switch { LuaTokenType.KwNot => "not", LuaTokenType.Hash => "#", _ => "-" };
            return new UnaryExpr(op, ParseUnary());
        }
        return ParsePow();
    }

    private LuaExpr ParsePow()
    {
        var left = ParsePrimary();
        if (Check(LuaTokenType.Caret))
        {
            Advance();
            return new BinaryExpr("^", left, ParseUnary()); // right-assoc, binds tighter than unary
        }
        return left;
    }

    private LuaExpr ParsePrimary()
    {
        switch (CurType)
        {
            case LuaTokenType.KwNil:
                Advance();
                return new NilExpr();
            case LuaTokenType.KwTrue:
                Advance();
                return new TrueExpr();
            case LuaTokenType.KwFalse:
                Advance();
                return new FalseExpr();
            case LuaTokenType.DotDotDot:
                Advance();
                return new VarargExpr();
            case LuaTokenType.Number:
                return new NumberExpr(Advance().Text);
            case LuaTokenType.String:
                return new StringExpr(Advance().Text);
            case LuaTokenType.LBrace:
                return ParseTableConstructor();
            default:
                return ParseSuffixedExpr();
        }
    }

    /// <summary>A name/paren-expr followed by any number of `.field`, `[expr]`, `(args)`, `:m(args)` suffixes.</summary>
    private LuaExpr ParseSuffixedExpr()
    {
        LuaExpr expr;
        if (Match(LuaTokenType.LParen))
        {
            expr = ParseExpr();
            Expect(LuaTokenType.RParen);
        }
        else
        {
            expr = new NameExpr(Expect(LuaTokenType.Identifier).Text);
        }

        while (true)
        {
            switch (CurType)
            {
                case LuaTokenType.Dot:
                    Advance();
                    expr = new FieldAccessExpr(expr, Expect(LuaTokenType.Identifier).Text);
                    break;
                case LuaTokenType.LBracket:
                    Advance();
                    var key = ParseExpr();
                    Expect(LuaTokenType.RBracket);
                    expr = new IndexAccessExpr(expr, key);
                    break;
                case LuaTokenType.Colon:
                {
                    Advance();
                    string method = Expect(LuaTokenType.Identifier).Text;
                    var args = ParseArgs();
                    expr = new MethodCallExpr(expr, method, args);
                    break;
                }
                case LuaTokenType.LParen:
                case LuaTokenType.String:
                case LuaTokenType.LBrace:
                    expr = new CallExpr(expr, ParseArgs());
                    break;
                default:
                    return expr;
            }
        }
    }

    private List<LuaExpr> ParseArgs()
    {
        if (Check(LuaTokenType.String))
        {
            return [new StringExpr(Advance().Text)];
        }
        if (Check(LuaTokenType.LBrace))
        {
            return [ParseTableConstructor()];
        }
        Expect(LuaTokenType.LParen);
        var args = new List<LuaExpr>();
        if (!Check(LuaTokenType.RParen))
        {
            args.Add(ParseExpr());
            while (Match(LuaTokenType.Comma))
            {
                args.Add(ParseExpr());
            }
        }
        Expect(LuaTokenType.RParen);
        return args;
    }

    private LuaExpr ParseTableConstructor()
    {
        Expect(LuaTokenType.LBrace);
        var fields = new List<TableField>();
        while (!Check(LuaTokenType.RBrace))
        {
            if (Check(LuaTokenType.LBracket))
            {
                Advance();
                var key = ParseExpr();
                Expect(LuaTokenType.RBracket);
                Expect(LuaTokenType.Assign);
                fields.Add(new TableKeyedField(key, ParseExpr()));
            }
            else if (Check(LuaTokenType.Identifier) && _tokens[_pos + 1].Type == LuaTokenType.Assign)
            {
                string name = Advance().Text;
                Advance(); // '='
                fields.Add(new TableNamedField(name, ParseExpr()));
            }
            else
            {
                fields.Add(new TablePositionalField(ParseExpr()));
            }

            if (!Match(LuaTokenType.Comma) && !Match(LuaTokenType.Semicolon))
            {
                break;
            }
        }
        Expect(LuaTokenType.RBrace);
        return new TableConstructorExpr(fields);
    }
}
