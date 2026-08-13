using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Tools.Domino.Graphs;

/// <summary>
/// Classifies a `user\` mission graph's parsed <see cref="CompilationUnitSyntax"/> into the closed set of
/// statement shapes BlackBox's codegen actually emits (see <see cref="UserGraphStmt"/>). This is a shape
/// recognizer, not a graph builder — it does not resolve `f_N_...` handler names into edges or dedupe
/// `Boxes[PathID(...)]` occurrences into nodes; that's graph reconstruction (Phase 2), built on top of
/// this classified, statement-level structure.
/// </summary>
public static class UserGraphParser
{
    public static UserGraph Parse(CompilationUnitSyntax root)
    {
        var functions = new List<UserGraphFunction>();
        var topLevelOther = new List<StatementSyntax>();

        foreach (StatementSyntax stmt in root.Statements.Statements)
        {
            if (stmt is FunctionDeclarationStatementSyntax
                {
                    Name: MethodFunctionNameSyntax { BaseName: SimpleFunctionNameSyntax { Name.Text: "export" }, Name.Text: var fnName },
                } fn)
            {
                var parameters = fn.Parameters.Parameters
                    .Select(p => p is NamedParameterSyntax named ? named.Name : "...")
                    .ToList();
                var body = fn.Body.Statements.Select(ClassifyStmt).ToList();
                functions.Add(new UserGraphFunction(fnName, parameters, body, fn.SpanStart));
            }
            else
            {
                topLevelOther.Add(stmt);
            }
        }

        return new UserGraph(functions, topLevelOther);
    }

    private static UserGraphStmt ClassifyStmt(StatementSyntax stmt) => stmt switch
    {
        ExpressionStatementSyntax e => ClassifyExpressionStmt(e) ?? new OtherStmt(stmt),
        AssignmentStatementSyntax a => ClassifyAssignmentStmt(a) ?? new OtherStmt(stmt),
        _ => new OtherStmt(stmt),
    };

    // The six expression-statement shapes are mutually exclusive, so their order carries no meaning.
    private static UserGraphStmt? ClassifyExpressionStmt(ExpressionStatementSyntax stmt) =>
        TryMatchRegisterBox(stmt)
        ?? TryMatchCallOwnHandler(stmt)
        ?? TryMatchFireControlIn(stmt)
        ?? TryMatchFireOwnPin(stmt)
        ?? TryMatchLoadResource(stmt)
        ?? TryMatchTraceConnection(stmt);

    // Tried in order; the first match wins, and several shapes overlap (`self.box_X_N = cbox:CreateBox(...)`
    // is also a graph-field set, `Box._graph = self;` also a box-field assignment, `Box.Field = Box.Pin;`
    // both a box-field assignment and a data read), so the relative order is load-bearing.
    private static UserGraphStmt? ClassifyAssignmentStmt(AssignmentStatementSyntax stmt) =>
        TryMatchCreateBox(stmt)
        ?? TryMatchRebindSelfToGraph(stmt)
        ?? TryMatchSetGraphBackref(stmt)
        ?? TryMatchWireDynamicPin(stmt)
        ?? TryMatchBoxFieldAssignment(stmt)
        ?? TryMatchReadData(stmt)
        ?? TryMatchSetGraphField(stmt);

    // cbox:RegisterBox("Domino/System/X.lua");
    private static UserGraphStmt? TryMatchRegisterBox(ExpressionStatementSyntax stmt) =>
        stmt is
        {
            Expression: MethodCallExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Name: "cbox" },
                Identifier.Text: "RegisterBox",
                Argument: ExpressionListFunctionArgumentSyntax { Expressions: [var pathArg] },
            },
        } && AsString(pathArg) is { } path
            ? new RegisterBoxStmt(path)
            : null;

    // self._type.HandlerName(self);  (own en_N/ex_N/OnEnter_.../OnExit_... helper)
    private static UserGraphStmt? TryMatchCallOwnHandler(ExpressionStatementSyntax stmt) =>
        stmt is
        {
            Expression: FunctionCallExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Name: "self" }, MemberName.Text: "_type" },
                    MemberName.Text: var handler,
                },
                Argument: ExpressionListFunctionArgumentSyntax { Expressions: [IdentifierNameSyntax { Name: "self" }] },
            },
        }
            ? new CallOwnHandlerStmt(handler)
            : null;

    // Box._type.PinName(Box);
    private static UserGraphStmt? TryMatchFireControlIn(ExpressionStatementSyntax stmt) =>
        stmt is
        {
            Expression: FunctionCallExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax { MemberName.Text: "_type" } typeTarget, MemberName.Text: var pinName },
            },
        } && TryParseBoxRef(typeTarget.Expression) is { } box
            ? new FireControlInStmt(box, pinName)
            : null;

    // self:PinName();  (fire own exposed control-out pin)
    private static UserGraphStmt? TryMatchFireOwnPin(ExpressionStatementSyntax stmt) =>
        stmt is
        {
            Expression: MethodCallExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Name: "self" },
                Identifier.Text: var pinName,
                Argument: ExpressionListFunctionArgumentSyntax { Expressions: [] },
            },
        }
            ? new FireOwnPinStmt(pinName)
            : null;

    // cbox:LoadResource("name", "CResourceType");
    private static UserGraphStmt? TryMatchLoadResource(ExpressionStatementSyntax stmt) =>
        stmt is
        {
            Expression: MethodCallExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Name: "cbox" },
                Identifier.Text: "LoadResource",
                Argument: ExpressionListFunctionArgumentSyntax { Expressions: [var nameArg, var typeArg] },
            },
        } && AsString(nameArg) is { } name && AsString(typeArg) is { } type
            ? new LoadResourceStmt(name, type)
            : null;

    // CDominoManager_GetInstance():TraceConnection("doc", "src.Pin", "dst.Pin", srcBox, dstBox);  (debug builds only)
    private static UserGraphStmt? TryMatchTraceConnection(ExpressionStatementSyntax stmt) =>
        stmt is
        {
            Expression: MethodCallExpressionSyntax
            {
                Expression: FunctionCallExpressionSyntax { Expression: IdentifierNameSyntax { Name: "CDominoManager_GetInstance" }, Argument: ExpressionListFunctionArgumentSyntax { Expressions: [] } },
                Identifier.Text: "TraceConnection",
                Argument: ExpressionListFunctionArgumentSyntax { Expressions: [var docArg, var srcPinArg, var dstPinArg, var srcBoxExpr, var dstBoxExpr] },
            },
        } && AsString(docArg) is { } doc && AsString(srcPinArg) is { } srcPin && AsString(dstPinArg) is { } dstPin
            ? new TraceConnectionStmt(doc, srcPin, dstPin, srcBoxExpr, dstBoxExpr)
            : null;

    // self[N] = cbox:CreateBox("path");  /  self.box_TypeName_N = cbox:CreateBox("path");
    private static UserGraphStmt? TryMatchCreateBox(AssignmentStatementSyntax stmt) =>
        stmt is
        {
            Variables: [var target],
            EqualsValues.Values:
            [
                MethodCallExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Name: "cbox" },
                    Identifier.Text: "CreateBox",
                    Argument: ExpressionListFunctionArgumentSyntax { Expressions: [var pathArg] },
                },
            ],
        } && TryParseBoxRef(target) is { } box && AsString(pathArg) is { } path
            ? new CreateBoxStmt(box, path)
            : null;

    // self = self._graph;
    private static UserGraphStmt? TryMatchRebindSelfToGraph(AssignmentStatementSyntax stmt) =>
        stmt is
        {
            Variables: [IdentifierNameSyntax { Name: "self" }],
            EqualsValues.Values: [MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Name: "self" }, MemberName.Text: "_graph" }],
        }
            ? new RebindSelfToGraphStmt()
            : null;

    // Box._graph = self;
    private static UserGraphStmt? TryMatchSetGraphBackref(AssignmentStatementSyntax stmt) =>
        stmt is
        {
            Variables: [MemberAccessExpressionSyntax { MemberName.Text: "_graph" } target],
            EqualsValues.Values: [IdentifierNameSyntax { Name: "self" }],
        } && TryParseBoxRef(target.Expression) is { } box
            ? new SetGraphBackrefStmt(box)
            : null;

    // Box.PinName[N] = self._type.f_N_...;  /  Box.PinName[N] = DummyFunction;  (dynamic control-out pin)
    private static UserGraphStmt? TryMatchWireDynamicPin(AssignmentStatementSyntax stmt) =>
        stmt is
        {
            Variables: [ElementAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax { MemberName.Text: var pinName } pinTarget, KeyExpression: var idxExpr }],
            EqualsValues.Values: [var value],
        }
        && TryParseBoxRef(pinTarget.Expression) is { } box
        && AsInt(idxExpr) is { } idx
        && TryParseWireTarget(value, out string? handler)
            ? new WireControlOutStmt(box, pinName, (int)idx, handler)
            : null;

    // Box.PinName = self._type.f_N_...;  /  Box.PinName = DummyFunction;  /  Box.ParamName = value;
    private static UserGraphStmt? TryMatchBoxFieldAssignment(AssignmentStatementSyntax stmt)
    {
        if (stmt is
            {
                Variables: [MemberAccessExpressionSyntax { MemberName.Text: var fieldName } target],
                EqualsValues.Values: [var value],
            }
            && TryParseBoxRef(target.Expression) is { } box)
        {
            return TryParseWireTarget(value, out string? handler)
                ? new WireControlOutStmt(box, fieldName, null, handler)
                : new SetParamStmt(box, fieldName, value);
        }
        return null;
    }

    // Target = Box.PinName;  (reading a data-out value into graph state)
    private static UserGraphStmt? TryMatchReadData(AssignmentStatementSyntax stmt) =>
        stmt is { Variables: [var target], EqualsValues.Values: [var value] }
        && TryParseBoxPinRead(value) is { } read
            ? new ReadDataStmt(target, read.Box, read.Pin)
            : null;

    // self.FieldName = value;  (plain graph-level variable init, not a box operation)
    private static UserGraphStmt? TryMatchSetGraphField(AssignmentStatementSyntax stmt) =>
        stmt is
        {
            Variables: [MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Name: "self" }, MemberName.Text: var fieldName }],
            EqualsValues.Values: [var value],
        }
            ? new SetGraphFieldStmt(fieldName, value)
            : null;

    /// <summary>Recognizes a `Box.PinName` read - the value side of both `self.Var = self[N].Pin;` and
    /// the rarer direct `self[14].Entity = self[8].ObjectEntity;`. Shared with
    /// <see cref="DataFlowResolver"/> so the box-reference grammar is stated once.</summary>
    internal static (BoxRef Box, string Pin)? TryParseBoxPinRead(ExpressionSyntax expr) =>
        expr is MemberAccessExpressionSyntax { MemberName.Text: var pin } access
        && TryParseBoxRef(access.Expression) is { } box
            ? (box, pin)
            : null;

    /// <summary>Recognizes `self[N]`, `self.box_TypeName_N`, or `Boxes[PathID("path")]`.</summary>
    private static BoxRef? TryParseBoxRef(ExpressionSyntax expr) => expr switch
    {
        ElementAccessExpressionSyntax { Expression: IdentifierNameSyntax { Name: "self" }, KeyExpression: var key }
            when AsInt(key) is { } slot => new InstanceBoxRef(slot),
        MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Name: "self" }, MemberName.Text: var name }
            when name.StartsWith("box_", StringComparison.Ordinal) => new NamedInstanceBoxRef(name),
        ElementAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Name: "Boxes" },
            KeyExpression: FunctionCallExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Name: "PathID" },
                Argument: ExpressionListFunctionArgumentSyntax { Expressions: [var pathArg] },
            },
        } when AsString(pathArg) is { } path => new PooledBoxRef(path),
        _ => null,
    };

    /// <summary>Recognizes `self._type.HandlerName`.</summary>
    private static string? TryParseSelfTypeHandler(ExpressionSyntax expr) =>
        expr is MemberAccessExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Name: "self" }, MemberName.Text: "_type" },
            MemberName.Text: var handler,
        }
            ? handler
            : null;

    /// <summary>Recognizes a control-out wire's value side: `self._type.HandlerName` (wired,
    /// <paramref name="handler"/> set) or `DummyFunction` (unwired, null).</summary>
    private static bool TryParseWireTarget(ExpressionSyntax expr, out string? handler)
    {
        handler = TryParseSelfTypeHandler(expr);
        return handler is not null || expr is IdentifierNameSyntax { Name: "DummyFunction" };
    }

    private static string? AsString(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit && lit.Kind() == SyntaxKind.StringLiteralExpression ? lit.Token.ValueText : null;

    /// <summary>Reads the literal's raw source text rather than <c>SyntaxToken.Value</c> - Loretta
    /// decodes numeric literals as <see cref="double"/>, which silently loses precision on the
    /// 19-digit entity-ID-sized integers this corpus sometimes uses (box slots themselves are always
    /// small, but this keeps the helper correct for any numeric literal, not just the common case).</summary>
    private static long? AsInt(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit && lit.Kind() == SyntaxKind.NumericalLiteralExpression && long.TryParse(lit.Token.Text, out long v)
            ? v
            : null;
}
