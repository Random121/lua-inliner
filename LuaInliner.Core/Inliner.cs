using System.Collections.Immutable;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Text;
using LuaInliner.Common;
using LuaInliner.Core.Collectors;
using LuaInliner.Core.Inlining;

namespace LuaInliner.Core;

/// <summary>
/// Lua inliner
/// </summary>
/// <param name="luaParseOptions"></param>
/// <param name="minimumErrorSeverity">Minimum severity encountered during parsing before an error is thrown.</param>
public sealed class Inliner(
    LuaParseOptions luaParseOptions,
    DiagnosticSeverity minimumErrorSeverity = DiagnosticSeverity.Error
)
{
    public Result<SyntaxNode, IList<Diagnostic>> InlineFile(SourceText source)
    {
        SyntaxTree tree = LuaSyntaxTree.ParseText(source, luaParseOptions);

        List<Diagnostic> diagnostics = [];

        // Check diagnostics from parsing
        {
            var parseDiagnostics = tree.GetDiagnostics().ToImmutableArray();

            // Add current diagnostics
            diagnostics.AddRange(parseDiagnostics);

            if (ShouldErrorWithDiagnostics(parseDiagnostics))
            {
                return Result.Err<SyntaxNode, IList<Diagnostic>>(diagnostics);
            }
        }

        SyntaxNode root = tree.GetRoot();
        Script script = new([tree]);

        var inlineFunctionInfoList = InlineFunctionCollector.Collect(root);

        if (inlineFunctionInfoList.IsErr)
        {
            var functionInfoDiagnostics = inlineFunctionInfoList.Err.Value;

            // Add current diagnostics
            diagnostics.AddRange(functionInfoDiagnostics);

            if (ShouldErrorWithDiagnostics(functionInfoDiagnostics))
            {
                return Result.Err<SyntaxNode, IList<Diagnostic>>(diagnostics);
            }
        }

        var inlineFunctionCallInfoList = InlineFunctionCallCollector.Collect(
            root,
            script,
            inlineFunctionInfoList.Ok.Value
        );

        if (inlineFunctionCallInfoList.IsErr)
        {
            var callInfoDiagnostics = inlineFunctionInfoList.Err.Value;

            // Add current diagnostics
            diagnostics.AddRange(callInfoDiagnostics);

            if (ShouldErrorWithDiagnostics(callInfoDiagnostics))
            {
                return Result.Err<SyntaxNode, IList<Diagnostic>>(diagnostics);
            }
        }

        SyntaxNode rewritten = InlineRewriter.Rewrite(
            root,
            script,
            inlineFunctionCallInfoList.Ok.Value
        );

        return Result.Ok<SyntaxNode, IList<Diagnostic>>(rewritten);
    }

    private bool ShouldErrorWithDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics.Any(diagnostic => diagnostic.Severity >= minimumErrorSeverity);
    }
}
