using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Text;

namespace LuaInliner.Core;

public sealed class Inliner
{
    private readonly LuaParseOptions _luaParseOptions;

    /// <summary>
    /// Minimum severity encountered during parsing before an error
    /// is thrown.
    /// </summary>
    private readonly DiagnosticSeverity _errorOnSeverity;

    public Inliner(
        LuaParseOptions luaParseOptions,
        DiagnosticSeverity errorOnSeverity = DiagnosticSeverity.Error
    )
    {
        _luaParseOptions = luaParseOptions;
        _errorOnSeverity = errorOnSeverity;
    }

    public static SyntaxNode InlineFile(SourceText source)
    {
        return default;
    }
}
