using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Text;
using LuaInliner.Common;
using LuaInliner.Core.Collectors;
using System.Collections.Immutable;

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

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        // Check diagnostics from parsing
        {
            var parseDiagnostics = tree.GetDiagnostics().ToImmutableArray();

            if (parseDiagnostics.Any(diagnostic => diagnostic.Severity >= _errorOnSeverity))
            {
                return Result.Err<SyntaxNode, ImmutableArray<Diagnostic>>(parseDiagnostics);
            }

            // Keep these diagnostics in case any errors occur later one as well
            diagnostics.AddRange(parseDiagnostics);
        }

        SyntaxNode root = tree.GetRoot();

        var res = InlineFunctionCollector.Collect(root);

        if (res.IsOk)
        {
            foreach (var val in res.Ok.Value)
            {
                Console.WriteLine(val);
            }
        }

        return Result.Ok<SyntaxNode, ImmutableArray<Diagnostic>>(root);
    }
}
