using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace Domino.Core.Graphs;

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

    private static UserGraphStmt ClassifyStmt(StatementSyntax stmt)
    {
        switch (stmt)
        {
            // cbox:RegisterBox("Domino/System/X.lua");
            case ExpressionStatementSyntax
            {
                Expression: MethodCallExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Name: "cbox" },
                    Identifier.Text: "RegisterBox",
                    Argument: ExpressionListFunctionArgumentSyntax { Expressions: [var pathArg] },
                },
            } when AsString(pathArg) is { } registerPath:
                return new RegisterBoxStmt(registerPath);

            // self._type.HandlerName(self);  (own en_N/ex_N/OnEnter_.../OnExit_... helper)
            case ExpressionStatementSyntax
            {
                Expression: FunctionCallExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax
                    {
                        Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Name: "self" }, MemberName.Text: "_type" },
                        MemberName.Text: var ownHandler,
                    },
                    Argument: ExpressionListFunctionArgumentSyntax { Expressions: [IdentifierNameSyntax { Name: "self" }] },
                },
            }:
                return new CallOwnHandlerStmt(ownHandler);

            // Box._type.PinName(Box);
            case ExpressionStatementSyntax
            {
                Expression: FunctionCallExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax { MemberName.Text: "_type" } typeTarget, MemberName.Text: var pinName },
                },
            } when TryParseBoxRef(typeTarget.Expression) is { } fireBox:
                return new FireControlInStmt(fireBox, pinName);

            // self:PinName();  (fire own exposed control-out pin)
            case ExpressionStatementSyntax
            {
                Expression: MethodCallExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Name: "self" },
                    Identifier.Text: var ownPin,
                    Argument: ExpressionListFunctionArgumentSyntax { Expressions: [] },
                },
            }:
                return new FireOwnPinStmt(ownPin);

            // cbox:LoadResource("name", "CResourceType");
            case ExpressionStatementSyntax
            {
                Expression: MethodCallExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Name: "cbox" },
                    Identifier.Text: "LoadResource",
                    Argument: ExpressionListFunctionArgumentSyntax { Expressions: [var resNameArg, var resTypeArg] },
                },
            } when AsString(resNameArg) is { } resName && AsString(resTypeArg) is { } resType:
                return new LoadResourceStmt(resName, resType);

            // CDominoManager_GetInstance():TraceConnection("doc", "src.Pin", "dst.Pin", srcBox, dstBox);  (debug builds only)
            case ExpressionStatementSyntax
            {
                Expression: MethodCallExpressionSyntax
                {
                    Expression: FunctionCallExpressionSyntax { Expression: IdentifierNameSyntax { Name: "CDominoManager_GetInstance" }, Argument: ExpressionListFunctionArgumentSyntax { Expressions: [] } },
                    Identifier.Text: "TraceConnection",
                    Argument: ExpressionListFunctionArgumentSyntax { Expressions: [var docArg, var srcPinArg, var dstPinArg, var srcBoxExpr, var dstBoxExpr] },
                },
            } when AsString(docArg) is { } doc && AsString(srcPinArg) is { } srcPin && AsString(dstPinArg) is { } dstPin:
                return new TraceConnectionStmt(doc, srcPin, dstPin, srcBoxExpr, dstBoxExpr);

            // self[N] = cbox:CreateBox("path");  /  self.box_TypeName_N = cbox:CreateBox("path");
            case AssignmentStatementSyntax
            {
                Variables: [var createTarget],
                EqualsValues.Values:
                [
                    MethodCallExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax { Name: "cbox" },
                        Identifier.Text: "CreateBox",
                        Argument: ExpressionListFunctionArgumentSyntax { Expressions: [var createPathArg] },
                    },
                ],
            } when TryParseBoxRef(createTarget) is { } createdBox && AsString(createPathArg) is { } createPath:
                return new CreateBoxStmt(createdBox, createPath);

            // self = self._graph;
            case AssignmentStatementSyntax
            {
                Variables: [IdentifierNameSyntax { Name: "self" }],
                EqualsValues.Values: [MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Name: "self" }, MemberName.Text: "_graph" }],
            }:
                return new RebindSelfToGraphStmt();

            // Box._graph = self;
            case AssignmentStatementSyntax
            {
                Variables: [MemberAccessExpressionSyntax { MemberName.Text: "_graph" } graphTarget],
                EqualsValues.Values: [IdentifierNameSyntax { Name: "self" }],
            } when TryParseBoxRef(graphTarget.Expression) is { } backrefBox:
                return new SetGraphBackrefStmt(backrefBox);

            // Box.PinName[N] = self._type.f_N_...;  /  Box.PinName[N] = DummyFunction;  (dynamic control-out pin)
            case AssignmentStatementSyntax
            {
                Variables: [ElementAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax { MemberName.Text: var dynPinName } dynPinTarget, KeyExpression: var dynIdxExpr }],
                EqualsValues.Values: [var dynValue],
            } when TryParseBoxRef(dynPinTarget.Expression) is { } dynBox
                && AsInt(dynIdxExpr) is { } dynIdx
                && (IsDummyFunction(dynValue) || TryParseSelfTypeHandler(dynValue) is not null):
                return new WireControlOutStmt(dynBox, dynPinName, (int)dynIdx, TryParseSelfTypeHandler(dynValue));

            // Box.PinName = self._type.f_N_...;  /  Box.PinName = DummyFunction;  /  Box.ParamName = value;
            case AssignmentStatementSyntax
            {
                Variables: [MemberAccessExpressionSyntax { MemberName.Text: var fieldName } fieldTarget],
                EqualsValues.Values: [var fieldValue],
            } when TryParseBoxRef(fieldTarget.Expression) is { } fieldBox:
                if (IsDummyFunction(fieldValue) || TryParseSelfTypeHandler(fieldValue) is not null)
                {
                    return new WireControlOutStmt(fieldBox, fieldName, null, TryParseSelfTypeHandler(fieldValue));
                }
                return new SetParamStmt(fieldBox, fieldName, fieldValue);

            // Target = Box.PinName;  (reading a data-out value into graph state)
            case AssignmentStatementSyntax
            {
                Variables: [var readTarget],
                EqualsValues.Values: [MemberAccessExpressionSyntax { MemberName.Text: var readPin } readSource],
            } when TryParseBoxRef(readSource.Expression) is { } readBox:
                return new ReadDataStmt(readTarget, readBox, readPin);

            // self.FieldName = value;  (plain graph-level variable init, not a box operation)
            case AssignmentStatementSyntax
            {
                Variables: [MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Name: "self" }, MemberName.Text: var graphFieldName }],
                EqualsValues.Values: [var graphFieldValue],
            }:
                return new SetGraphFieldStmt(graphFieldName, graphFieldValue);

            default:
                return new OtherStmt(stmt);
        }
    }

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

    private static bool IsDummyFunction(ExpressionSyntax expr) => expr is IdentifierNameSyntax { Name: "DummyFunction" };

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
