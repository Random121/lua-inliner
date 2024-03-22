using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace LuaInliner.Core.InlineExpansion;

/// <summary>
/// Builds a inlined (inline expanded) function.
/// </summary>
/// <param name="body"></param>
/// <param name="parameters"></param>
/// <param name="arguments"></param>
/// <param name="returnVariableIdentifiers"></param>
internal sealed partial class InlinedFunctionBuilder(
    IReadOnlyList<StatementSyntax> body,
    IReadOnlyList<NamedParameterSyntax> parameters,
    IReadOnlyList<ExpressionSyntax> arguments,
    IReadOnlyList<IdentifierNameSyntax> returnVariableIdentifiers
)
{
    public IReadOnlyList<StatementSyntax> ToStatements()
    {
        StatementListSyntax bodyStatements = SyntaxFactory.StatementList(body);
        StatementListSyntax returnReplacedBody = (StatementListSyntax)
            ReturnRewriter.Rewrite(bodyStatements, returnVariableIdentifiers);

        StatementListSyntax bodyWithParameters = GetBodyWithParameters(returnReplacedBody);

        var wrappedBody = NormalizeStyle(
            SyntaxFactory.RepeatUntilStatement(bodyWithParameters, SyntaxConstants.TRUE_LITERAL)
        );

        return GetWrappedBodyWithReturnVariables(wrappedBody);
    }

    /// <summary>
    /// Prepends the return variables onto the wrapped body.
    /// </summary>
    /// <param name="wrappedBody"></param>
    /// <returns></returns>
    private List<StatementSyntax> GetWrappedBodyWithReturnVariables(StatementSyntax wrappedBody)
    {
        List<StatementSyntax> statements = [];

        if (returnVariableIdentifiers.Count != 0)
        {
            statements.Add(NormalizeStyle(GetReturnVariableDeclaration()));
        }

        statements.Add(wrappedBody);

        return statements;
    }

    /// <summary>
    /// Prepends the parameters onto the body.
    /// </summary>
    /// <param name="body"></param>
    /// <returns></returns>
    private StatementListSyntax GetBodyWithParameters(StatementListSyntax body)
    {
        List<StatementSyntax> statements = [];

        if (parameters.Count != 0)
        {
            statements.Add(NormalizeStyle(GetInitializedParameters()));
        }

        statements.AddRange(body.Statements);

        return SyntaxFactory.StatementList(statements);
    }

    /// <summary>
    /// Get the parameters initialized with their corresponding argument value
    /// </summary>
    /// <returns></returns>
    private LocalVariableDeclarationStatementSyntax GetInitializedParameters()
    {
        var parameterNames = parameters.Select(parameter =>
            SyntaxFactory.LocalDeclarationName(parameter.Name)
        );

        var parameterList = SyntaxFactory.SeparatedList(parameterNames);
        var argumentList = SyntaxFactory.SeparatedList(arguments);

        return SyntaxFactory.LocalVariableDeclarationStatement(parameterList, argumentList);
    }

    /// <summary>
    /// Generate the declaration for the return variables with an initial value of <c>nil</c>
    /// </summary>
    /// <returns></returns>
    private LocalVariableDeclarationStatementSyntax GetReturnVariableDeclaration()
    {
        var returnVariables = returnVariableIdentifiers.Select(SyntaxFactory.LocalDeclarationName);
        var placeholderValues = Enumerable
            .Repeat(SyntaxConstants.NIL_LITERAL, returnVariableIdentifiers.Count)
            .Cast<ExpressionSyntax>();

        return SyntaxFactory.LocalVariableDeclarationStatement(
            SyntaxFactory.SeparatedList(returnVariables),
            SyntaxFactory.SeparatedList(placeholderValues)
        );
    }

    private static TNode NormalizeStyle<TNode>(TNode node)
        where TNode : SyntaxNode
    {
        return node.NormalizeWhitespace().WithTrailingTrivia(SyntaxConstants.EOL_TRIVIA);
    }
}
