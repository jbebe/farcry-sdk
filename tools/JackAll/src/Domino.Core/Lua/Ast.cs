namespace Domino.Core.Lua;

// A deliberately small AST — just enough of Lua's grammar to parse real Domino output (both the
// mechanical `user\` graph shape and the small hand-written `system\` node bodies) and regenerate
// equivalent text on save. Not a general-purpose Lua parser: no anonymous function expressions,
// varargs, or `repeat`/`while` support beyond what's needed to fall through cleanly if encountered.

public abstract record LuaStmt;

public sealed record LuaChunk(IReadOnlyList<LuaStmt> Statements);

/// <summary>`lhs1, lhs2 = rhs1, rhs2` (also covers the single-target case).</summary>
public sealed record AssignStmt(IReadOnlyList<LuaExpr> Targets, IReadOnlyList<LuaExpr> Values) : LuaStmt;

/// <summary>`local name1, name2 = rhs1, rhs2` (Values may be empty: `local x;`).</summary>
public sealed record LocalStmt(IReadOnlyList<string> Names, IReadOnlyList<LuaExpr> Values) : LuaStmt;

/// <summary>A call expression used as a statement, e.g. `self:Out();`.</summary>
public sealed record CallStmt(LuaExpr Call) : LuaStmt;

/// <summary>
/// `function Name(...) ... end` / `function Name.Field(...) ... end` / `function Name:Method(...) ... end`.
/// <paramref name="IsMethod"/> distinguishes the colon form (implicit `self` parameter).
/// </summary>
public sealed record FunctionDeclStmt(
    IReadOnlyList<string> NamePath,
    bool IsMethod,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<LuaStmt> Body) : LuaStmt;

public sealed record IfClause(LuaExpr Condition, IReadOnlyList<LuaStmt> Body);

public sealed record IfStmt(
    IReadOnlyList<IfClause> Clauses,
    IReadOnlyList<LuaStmt>? ElseBody) : LuaStmt;

/// <summary>`for name1, name2 in expr do ... end` (Domino's codegen only ever iterates one expr).</summary>
public sealed record GenericForStmt(
    IReadOnlyList<string> Names,
    IReadOnlyList<LuaExpr> Iterators,
    IReadOnlyList<LuaStmt> Body) : LuaStmt;

/// <summary>`for name = start, stop[, step] do ... end`.</summary>
public sealed record NumericForStmt(
    string Name,
    LuaExpr Start,
    LuaExpr Stop,
    LuaExpr? Step,
    IReadOnlyList<LuaStmt> Body) : LuaStmt;

public sealed record WhileStmt(LuaExpr Condition, IReadOnlyList<LuaStmt> Body) : LuaStmt;

public sealed record RepeatStmt(IReadOnlyList<LuaStmt> Body, LuaExpr Condition) : LuaStmt;

public sealed record DoStmt(IReadOnlyList<LuaStmt> Body) : LuaStmt;

public sealed record ReturnStmt(IReadOnlyList<LuaExpr> Values) : LuaStmt;

public sealed record BreakStmt : LuaStmt;

/// <summary>A `-- ...` or `--[[ ... ]]` comment, preserved as its own statement so header blocks and
/// per-line documentation survive a parse/regenerate round trip in their original position.</summary>
public sealed record CommentStmt(string Text, bool IsLong) : LuaStmt;

// --- Expressions -------------------------------------------------------------------------------

public abstract record LuaExpr;

public sealed record NilExpr : LuaExpr;
public sealed record TrueExpr : LuaExpr;
public sealed record FalseExpr : LuaExpr;
public sealed record VarargExpr : LuaExpr;
public sealed record NumberExpr(string Raw) : LuaExpr;
public sealed record StringExpr(string Value) : LuaExpr;
public sealed record NameExpr(string Name) : LuaExpr;

/// <summary>`target.field`</summary>
public sealed record FieldAccessExpr(LuaExpr Target, string Field) : LuaExpr;

/// <summary>`target[key]`</summary>
public sealed record IndexAccessExpr(LuaExpr Target, LuaExpr Key) : LuaExpr;

/// <summary>`callee(args)`</summary>
public sealed record CallExpr(LuaExpr Callee, IReadOnlyList<LuaExpr> Args) : LuaExpr;

/// <summary>`target:method(args)` — kept distinct from <see cref="CallExpr"/> to preserve colon-call syntax.</summary>
public sealed record MethodCallExpr(LuaExpr Target, string Method, IReadOnlyList<LuaExpr> Args) : LuaExpr;

public sealed record UnaryExpr(string Op, LuaExpr Operand) : LuaExpr;
public sealed record BinaryExpr(string Op, LuaExpr Left, LuaExpr Right) : LuaExpr;

public abstract record TableField;
public sealed record TablePositionalField(LuaExpr Value) : TableField;
public sealed record TableNamedField(string Name, LuaExpr Value) : TableField;
public sealed record TableKeyedField(LuaExpr Key, LuaExpr Value) : TableField;

public sealed record TableConstructorExpr(IReadOnlyList<TableField> Fields) : LuaExpr;
