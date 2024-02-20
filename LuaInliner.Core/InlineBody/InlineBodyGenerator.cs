using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using LuaInliner.Core.Collectors;

namespace LuaInliner.Core.InlineBody;

/// <summary>
/// Generates the function body for the inline expansion.
/// </summary>
internal static partial class InlineBodyGenerator
{
    public static SyntaxList<StatementSyntax> Generate(
        InlineFunctionInfo calledFunction,
        SeparatedSyntaxList<ExpressionSyntax> arguments,
        List<string> returnVariableNames
    )
    {
        StatementListSyntax functionBody = SyntaxFactory.StatementList(calledFunction.Body);

        StatementListSyntax returnReplacedBody = (StatementListSyntax)
            ReturnRewriter.Rewrite(functionBody, returnVariableNames);

        StatementListSyntax bodyWithArguments = GenerateBodyWithParameters(
            returnReplacedBody,
            calledFunction.Parameters,
            arguments
        );

        RepeatUntilStatementSyntax wrappedInLoop = SyntaxFactory
            .RepeatUntilStatement(bodyWithArguments, SyntaxConstants.TRUE_LITERAL)
            .NormalizeWhitespace()
            .WithTrailingTrivia(SyntaxConstants.EOL_TRIVIA);

        // Special case, no function returns
        // GenerateReturnVariableDeclaration() expects at least one return variable
        if (returnVariableNames.Count == 0)
        {
            return SyntaxFactory.List<StatementSyntax>([wrappedInLoop]);
        }

        LocalVariableDeclarationStatementSyntax returnVariableDeclaration =
            GenerateReturnVariableDeclaration(returnVariableNames)
                .NormalizeWhitespace()
                .WithTrailingTrivia(SyntaxConstants.EOL_TRIVIA);

        return SyntaxFactory.List<StatementSyntax>([returnVariableDeclaration, wrappedInLoop]);
    }

    /// <summary>
    /// Generates the body of the function with the initialized parameters prepended.
    /// </summary>
    /// <param name="body"></param>
    /// <param name="parameters"></param>
    /// <param name="arguments"></param>
    /// <returns></returns>
    private static StatementListSyntax GenerateBodyWithParameters(
        StatementListSyntax body,
        SeparatedSyntaxList<NamedParameterSyntax> parameters,
        SeparatedSyntaxList<ExpressionSyntax> arguments
    )
    {
        // Special case, no parameters
        // GenerateInitializedParameters() expects at least one parameter
        if (parameters.Count == 0)
        {
            return SyntaxFactory.StatementList(body.Statements);
        }

        LocalVariableDeclarationStatementSyntax initializedParameters =
            GenerateInitializedParameters(parameters, arguments)
                .NormalizeWhitespace()
                .WithTrailingTrivia(SyntaxConstants.EOL_TRIVIA);

        return SyntaxFactory.StatementList(
            (StatementSyntax[])[initializedParameters, ..body.Statements]
        );
    }

    /// <summary>
    /// Generates parameters initialized with their corresponding argument
    /// </summary>
    /// <param name="parameters"></param>
    /// <param name="arguments"></param>
    /// <returns></returns>
    private static LocalVariableDeclarationStatementSyntax GenerateInitializedParameters(
        SeparatedSyntaxList<NamedParameterSyntax> parameters,
        SeparatedSyntaxList<ExpressionSyntax> arguments
    )
    {
        SeparatedSyntaxList<LocalDeclarationNameSyntax> parameterNames =
            SyntaxFactory.SeparatedList(
                parameters.Select(parameter => SyntaxFactory.LocalDeclarationName(parameter.Name))
            );

        return SyntaxFactory.LocalVariableDeclarationStatement(parameterNames, arguments);
    }

    /// <summary>
    /// Generate the declaration for the return variables with an initial value of <c>nil</c>.
    /// </summary>
    /// <param name="returnVariableNames"></param>
    /// <returns></returns>
    private static LocalVariableDeclarationStatementSyntax GenerateReturnVariableDeclaration(
        List<string> returnVariableNames
    )
    {
        IEnumerable<LocalDeclarationNameSyntax> variableNames = returnVariableNames.Select(
            SyntaxFactory.LocalDeclarationName
        );

        IEnumerable<ExpressionSyntax> placeholderValues = Enumerable
            .Repeat(SyntaxConstants.NIL_LITERAL, returnVariableNames.Count)
            .Cast<ExpressionSyntax>();

        return SyntaxFactory.LocalVariableDeclarationStatement(
            SyntaxFactory.SeparatedList(variableNames),
            SyntaxFactory.SeparatedList(placeholderValues)
        );
    }
}
