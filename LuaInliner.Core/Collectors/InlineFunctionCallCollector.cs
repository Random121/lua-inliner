using System.Collections;
using System.Collections.Immutable;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using LuaInliner.Common;
using LuaInliner.Core.Extensions;

namespace LuaInliner.Core.Collectors;

internal record class InlineFunctionCallInfo(
    InlineFunctionInfo CalledFunction,
    FunctionCallExpressionSyntax CallExpressionNode
);

/// <summary>
/// Collect function calls to inline functions.
/// </summary>
internal sealed class InlineFunctionCallCollector : LuaSyntaxWalker
{
    public static Result<IList<InlineFunctionCallInfo>, IList<Diagnostic>> Collect(
        SyntaxNode node,
        Script script,
        IList<InlineFunctionInfo> inlineFunctionInfo
    )
    {
        InlineFunctionCallCollector collector = new(script, inlineFunctionInfo);
        collector.Visit(node);

        List<Diagnostic> diagnostics = collector._diagnostics;

        return diagnostics.Count != 0
            ? Result.Err<IList<InlineFunctionCallInfo>, IList<Diagnostic>>(diagnostics)
            : Result.Ok<IList<InlineFunctionCallInfo>, IList<Diagnostic>>(collector._calls);
    }

    private readonly List<InlineFunctionCallInfo> _calls = [];
    private readonly List<Diagnostic> _diagnostics = [];

    private readonly Script _script;

    /// <summary>
    /// Maps a function declaration node to its inline function information.
    /// </summary>
    private readonly ImmutableDictionary<
        LocalFunctionDeclarationStatementSyntax,
        InlineFunctionInfo
    > _functionInfoLookup;

    private InlineFunctionCallCollector(Script script, IList<InlineFunctionInfo> inlineFunctionInfo)
        : base(SyntaxWalkerDepth.Node)
    {
        _script = script;
        _functionInfoLookup = inlineFunctionInfo.ToImmutableDictionary(functionInfo =>
            functionInfo.DeclarationNode
        );
    }

    public override void VisitFunctionCallExpression(FunctionCallExpressionSyntax node)
    {
        IVariable? calledFunction = _script.GetVariable(node.Expression);

        // Don't inline external or indirect function calls
        if (calledFunction is null || calledFunction.Kind != VariableKind.Local)
        {
            base.VisitFunctionCallExpression(node);
            return;
        }

        SyntaxNode calledFunctionDeclaration = calledFunction.Declaration!;

        // We don't inline global functions because
        // (1) Loretta doesn't provide scoping information for it
        // (2) We can't determine the correct global function definition to use if there are multiple ones
        //     or if they are in conditional statements
        if (!calledFunctionDeclaration.IsKind(SyntaxKind.LocalFunctionDeclarationStatement))
        {
            base.VisitFunctionCallExpression(node);
            return;
        }

        // TODO: Move this check to a separate step in the inline process
        //       or to the inline function collection step

        // Cannot inline recursive calls
        if (calledFunctionDeclaration.Contains(node))
        {
            Diagnostic recursiveDiagnostic = Diagnostic.Create(
                InlinerDiagnostics.CannotInlineRecursiveFunction,
                node.GetLocation()
            );

            _diagnostics.Add(recursiveDiagnostic);

            base.VisitFunctionCallExpression(node);
            return;
        }

        bool isInlineFunctionCall = _functionInfoLookup.TryGetValue(
            (LocalFunctionDeclarationStatementSyntax)calledFunctionDeclaration,
            out InlineFunctionInfo? inlineFunctionInfo
        );

        // Not a call to an inline function
        if (!isInlineFunctionCall || inlineFunctionInfo is null)
        {
            base.VisitFunctionCallExpression(node);
            return;
        }

        InlineFunctionCallInfo inlineFunctionCallInfo = new(inlineFunctionInfo, node);

        _calls.Add(inlineFunctionCallInfo);

        base.VisitFunctionCallExpression(node);
    }
}
