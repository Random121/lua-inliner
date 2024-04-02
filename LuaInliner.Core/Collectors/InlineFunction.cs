using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace LuaInliner.Core.Collectors;

/// <summary>
/// Represents an inline function.
/// </summary>
/// <param name="DeclarationStatementNode"></param>
/// <param name="Parameters"></param>
/// <param name="Body"></param>
/// <param name="MaxReturnCount">Maximum number of values which the function returns</param>
internal record class InlineFunction(
    LocalFunctionDeclarationStatementSyntax DeclarationStatementNode,
    SeparatedSyntaxList<NamedParameterSyntax> Parameters,
    SyntaxList<StatementSyntax> Body,
    IReadOnlyList<ReturnStatementSyntax> Returns,
    int MaxReturnCount
);
