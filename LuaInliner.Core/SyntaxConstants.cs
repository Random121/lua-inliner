using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace LuaInliner.Core;

/// <summary>
/// Helper class containing some commonly used <see cref="LuaSyntaxNode"/>.
/// </summary>
internal static class SyntaxConstants
{
    public static readonly BreakStatementSyntax BREAK_STATEMENT = SyntaxFactory.BreakStatement();

    public static readonly LiteralExpressionSyntax NIL_LITERAL = SyntaxFactory.LiteralExpression(
        SyntaxKind.NilLiteralExpression
    );
    public static readonly LiteralExpressionSyntax TRUE_LITERAL = SyntaxFactory.LiteralExpression(
        SyntaxKind.TrueLiteralExpression
    );

    public static readonly SyntaxTrivia EOL_TRIVIA = SyntaxFactory.EndOfLine(Environment.NewLine);
}
