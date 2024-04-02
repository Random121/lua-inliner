using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Text;
using LuaInliner.Common;
using LuaInliner.Core.Collectors;
using LuaInliner.Core.Inlining;

namespace LuaInliner.Core;

using DiagnosticList = IReadOnlyList<Diagnostic>;

/// <summary>
/// Represents a lua inliner.
/// </summary>
/// <param name="luaParseOptions"></param>
/// <param name="minimumErrorSeverity">Minimum severity encountered during parsing before an error is thrown.</param>
public sealed class Inliner(
    LuaParseOptions luaParseOptions,
    DiagnosticSeverity minimumErrorSeverity = DiagnosticSeverity.Error
)
{
    /// <summary>
    /// Inlines all functions within a file.
    /// </summary>
    /// <param name="source"></param>
    /// <returns></returns>
    public Result<SyntaxNode, DiagnosticList> InlineFile(SourceText source)
    {
        SyntaxTree tree = LuaSyntaxTree.ParseText(source, luaParseOptions);

        List<Diagnostic> diagnostics = [];

        {
            var parseDiagnostics = tree.GetDiagnostics();

            diagnostics.AddRange(parseDiagnostics);

            if (ShouldErrorWithDiagnostics(parseDiagnostics))
            {
                return Result.Err<SyntaxNode, DiagnosticList>(diagnostics);
            }
        }

        SyntaxNode root = tree.GetRoot();
        Script script = new([tree]);

        var inlineFunctions = InlineFunctionCollector.Collect(root);

        if (inlineFunctions.IsErr)
        {
            var inlineFunctionDiagnostics = inlineFunctions.Err.Value;

            diagnostics.AddRange(inlineFunctionDiagnostics);

            if (ShouldErrorWithDiagnostics(inlineFunctionDiagnostics))
            {
                return Result.Err<SyntaxNode, DiagnosticList>(diagnostics);
            }
        }

        var inlineFunctionCalls = InlineFunctionCallCollector.Collect(
            root,
            script,
            inlineFunctions.Ok.Value
        );

        if (inlineFunctionCalls.IsErr)
        {
            var inlineFunctionCallDiagnostics = inlineFunctions.Err.Value;

            diagnostics.AddRange(inlineFunctionCallDiagnostics);

            if (ShouldErrorWithDiagnostics(inlineFunctionCallDiagnostics))
            {
                return Result.Err<SyntaxNode, DiagnosticList>(diagnostics);
            }
        }

        SyntaxNode rewritten = InlineRewriter.Rewrite(root, script, inlineFunctionCalls.Ok.Value);

        return Result.Ok<SyntaxNode, DiagnosticList>(rewritten);
    }

    /// <summary>
    /// Returns whether the diagnostics should result in an error.
    /// </summary>
    /// <param name="diagnostics"></param>
    /// <returns></returns>
    private bool ShouldErrorWithDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics.Any(diagnostic => diagnostic.Severity >= minimumErrorSeverity);
    }
}
