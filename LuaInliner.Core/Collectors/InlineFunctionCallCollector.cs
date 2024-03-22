using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using LuaInliner.Common;
using LuaInliner.Core.Extensions;

namespace LuaInliner.Core.Collectors;

using DiagnosticList = IReadOnlyList<Diagnostic>;
using InlineFunctionCallList = IReadOnlyList<InlineFunctionCall>;

/// <summary>
/// Collect function calls to inline functions.
/// </summary>
internal sealed class InlineFunctionCallCollector : LuaSyntaxWalker
{
    public static Result<InlineFunctionCallList, DiagnosticList> Collect(
        SyntaxNode node,
        Script script,
        IReadOnlyList<InlineFunction> inlineFunctions
    )
    {
        InlineFunctionCallCollector collector = new(script, inlineFunctions);
        collector.Visit(node);

        DiagnosticList diagnostics = collector._diagnostics;
        InlineFunctionCallList calls = collector._inlineFunctionCalls;

        return diagnostics.Count != 0
            ? Result.Err<InlineFunctionCallList, DiagnosticList>(diagnostics)
            : Result.Ok<InlineFunctionCallList, DiagnosticList>(calls);
    }

    private readonly List<InlineFunctionCall> _inlineFunctionCalls = [];
    private readonly List<Diagnostic> _diagnostics = [];

    private readonly Script _script;

    private readonly ImmutableDictionary<
        LocalFunctionDeclarationStatementSyntax,
        InlineFunction
    > _inlineFunctionLookup;

    private InlineFunctionCallCollector(Script script, IEnumerable<InlineFunction> inlineFunctions)
        : base(SyntaxWalkerDepth.Node)
    {
        _script = script;
        _inlineFunctionLookup = inlineFunctions.ToImmutableDictionary(function =>
            function.DeclarationStatementNode
        );
    }

    public override void VisitFunctionCallExpression(FunctionCallExpressionSyntax node)
    {
        IVariable? calledFunction = _script.GetVariable(node.Expression);

        // Can't inline external or indirect function calls
        if (calledFunction is null || calledFunction.Kind != VariableKind.Local)
        {
            base.VisitFunctionCallExpression(node);
            return;
        }

        SyntaxNode calledFunctionDeclaration = calledFunction.Declaration!;

        // We don't inline global functions because
        // (1) Loretta doesn't provide scoping information for it
        // (2) We can't determine which global function definition to use if there are multiple ones
        //     or if their declaration depends on runtime information (such as within a conditional statement)
        if (!calledFunctionDeclaration.IsKind(SyntaxKind.LocalFunctionDeclarationStatement))
        {
            base.VisitFunctionCallExpression(node);
            return;
        }

        bool isInlineFunctionCall = _inlineFunctionLookup.TryGetValue(
            (LocalFunctionDeclarationStatementSyntax)calledFunctionDeclaration,
            out InlineFunction? inlineFunction
        );

        if (!isInlineFunctionCall || inlineFunction is null)
        {
            base.VisitFunctionCallExpression(node);
            return;
        }

        // TODO: Implement a way to inline recursive calls to a certain depth

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

        InlineFunctionCall inlineFunctionCall = new(inlineFunction, node);

        _inlineFunctionCalls.Add(inlineFunctionCall);

        base.VisitFunctionCallExpression(node);
    }
}
