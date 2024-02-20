using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using LuaInliner.Common;
using System.Collections.Immutable;

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
    public static Result<
        ImmutableArray<InlineFunctionCallInfo>,
        ImmutableArray<Diagnostic>
    > Collect(SyntaxNode node, Script script, ImmutableArray<InlineFunctionInfo> inlineFunctionInfo)
    {
        InlineFunctionCallCollector collector = new(script, inlineFunctionInfo);
        collector.Visit(node);

        ImmutableArray<Diagnostic> diagnostics = collector._diagnostics.ToImmutableArray();

        return diagnostics.Any()
            ? Result.Err<ImmutableArray<InlineFunctionCallInfo>, ImmutableArray<Diagnostic>>(
                diagnostics
            )
            : Result.Ok<ImmutableArray<InlineFunctionCallInfo>, ImmutableArray<Diagnostic>>(
                collector._calls.ToImmutableArray()
            );
    }

    private readonly ImmutableArray<InlineFunctionCallInfo>.Builder _calls =
        ImmutableArray.CreateBuilder<InlineFunctionCallInfo>();

    private readonly ImmutableArray<Diagnostic>.Builder _diagnostics =
        ImmutableArray.CreateBuilder<Diagnostic>();

    private readonly Script _script;

    /// <summary>
    /// Maps a function declaration node to its inline function information.
    /// </summary>
    private readonly ImmutableDictionary<
        LocalFunctionDeclarationStatementSyntax,
        InlineFunctionInfo
    > _inlineFunctionInfoMapping;

    private InlineFunctionCallCollector(
        Script script,
        ImmutableArray<InlineFunctionInfo> inlineFunctionInfo
    )
        : base(SyntaxWalkerDepth.Node)
    {
        _script = script;
        _inlineFunctionInfoMapping = inlineFunctionInfo.ToImmutableDictionary(
            funcInfo => funcInfo.DeclarationNode
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

        InlineFunctionInfo? inlineFunctionInfo = _inlineFunctionInfoMapping.GetValueOrDefault(
            (LocalFunctionDeclarationStatementSyntax)calledFunctionDeclaration
        );

        // Not a call to an inline function
        if (inlineFunctionInfo is null)
        {
            base.VisitFunctionCallExpression(node);
            return;
        }

        InlineFunctionCallInfo callInfo = new(inlineFunctionInfo, node);

        _calls.Add(callInfo);

        base.VisitFunctionCallExpression(node);
    }
}
