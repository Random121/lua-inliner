using System.Collections.Immutable;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Text;
using LuaInliner.Common;
using LuaInliner.Core.Collectors;
using LuaInliner.Core.Inlining;

namespace LuaInliner.Core;

public sealed class Inliner(
    LuaParseOptions luaParseOptions,
    DiagnosticSeverity errorOnSeverity = DiagnosticSeverity.Error
)
{
    private readonly LuaParseOptions _luaParseOptions = luaParseOptions;

    /// <summary>
    /// Minimum severity encountered during parsing before an error
    /// is thrown.
    /// </summary>
    private readonly DiagnosticSeverity _errorOnSeverity = errorOnSeverity;

    public Result<SyntaxNode, ImmutableArray<Diagnostic>> InlineFile(SourceText source)
    {
        SyntaxTree tree = LuaSyntaxTree.ParseText(source, _luaParseOptions);

        List<Diagnostic> diagnostics = [];

        // Check diagnostics from parsing
        {
            var parseDiagnostics = tree.GetDiagnostics().ToImmutableArray();

            // Add current diagnostics
            diagnostics.AddRange(parseDiagnostics);

            if (ShouldErrorWithDiagnostics(parseDiagnostics))
            {
                return Result.Err<SyntaxNode, ImmutableArray<Diagnostic>>(
                    diagnostics.ToImmutableArray()
                );
            }
        }

        SyntaxNode root = tree.GetRoot();
        Script script = new([tree]);

        var inlineFunctionInfos = InlineFunctionCollector.Collect(root);

        if (inlineFunctionInfos.IsErr)
        {
            var IFIDiagnostics = inlineFunctionInfos.Err.Value;

            // Add current diagnostics
            diagnostics.AddRange(IFIDiagnostics);

            if (ShouldErrorWithDiagnostics(IFIDiagnostics))
            {
                return Result.Err<SyntaxNode, ImmutableArray<Diagnostic>>(
                    diagnostics.ToImmutableArray()
                );
            }
        }

        var inlineCallInfo = InlineFunctionCallCollector.Collect(
            root,
            script,
            inlineFunctionInfos.Ok.Value
        );

        if (inlineCallInfo.IsErr)
        {
            var ICIDiagnostics = inlineFunctionInfos.Err.Value;

            // Add current diagnostics
            diagnostics.AddRange(ICIDiagnostics);

            if (ShouldErrorWithDiagnostics(ICIDiagnostics))
            {
                return Result.Err<SyntaxNode, ImmutableArray<Diagnostic>>(
                    diagnostics.ToImmutableArray()
                );
            }
        }

        SyntaxNode rewritten = InlineRewriter.Rewrite(root, script, inlineCallInfo.Ok.Value);

        return Result.Ok<SyntaxNode, ImmutableArray<Diagnostic>>(rewritten);
    }

    private bool ShouldErrorWithDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics.Any(diagnostic => diagnostic.Severity >= _errorOnSeverity);
    }
}
