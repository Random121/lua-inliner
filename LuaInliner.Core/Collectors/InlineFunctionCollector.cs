using DotNext.Collections.Generic;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using LuaInliner.Common;
using LuaInliner.Core.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace LuaInliner.Core.Collectors;

/// <summary>
/// Information about an inline function
/// </summary>
/// <param name="DeclarationNode"></param>
/// <param name="Parameters"></param>
/// <param name="Body"></param>
/// <param name="MaxReturnCount">Maximum number of values which the function returns</param>
internal record InlineFunctionInfo(
    LocalFunctionDeclarationStatementSyntax DeclarationNode,
    SeparatedSyntaxList<NamedParameterSyntax> Parameters,
    SyntaxList<StatementSyntax> Body,
    int MaxReturnCount
);

/// <summary>
/// Collect functions which have been marked as being inline.
/// </summary>
internal sealed class InlineFunctionCollector : LuaSyntaxWalker
{
    public static Result<ImmutableArray<InlineFunctionInfo>, ImmutableArray<Diagnostic>> Collect(
        SyntaxNode node
    )
    {
        InlineFunctionCollector collector = new();
        collector.Visit(node);

        ImmutableArray<Diagnostic> diagnostics = collector._diagnostics.ToImmutableArray();

        return diagnostics.Any()
            ? Result.Err<ImmutableArray<InlineFunctionInfo>, ImmutableArray<Diagnostic>>(
                diagnostics
            )
            : Result.Ok<ImmutableArray<InlineFunctionInfo>, ImmutableArray<Diagnostic>>(
                collector._functions.ToImmutableArray()
            );
    }

    private readonly ImmutableArray<InlineFunctionInfo>.Builder _functions =
        ImmutableArray.CreateBuilder<InlineFunctionInfo>();

    private readonly ImmutableArray<Diagnostic>.Builder _diagnostics =
        ImmutableArray.CreateBuilder<Diagnostic>();

    private InlineFunctionCollector()
        : base(SyntaxWalkerDepth.Node) { }

    public override void VisitLocalFunctionDeclarationStatement(
        LocalFunctionDeclarationStatementSyntax node
    )
    {
        SyntaxTriviaList leadingBodyTrivia = GetFunctionBodyLeadingTrivia(node);
        Result<SyntaxTrivia, Unit> inlineDirective = GetInlineDirective(leadingBodyTrivia);

        // Not an inline function
        if (inlineDirective.IsErr)
        {
            base.VisitLocalFunctionDeclarationStatement(node);
            return;
        }

        SyntaxTrivia inlineDirectiveTrivia = inlineDirective.Ok.Value;

        // Remove the inline directive from the function body
        SyntaxList<StatementSyntax> cleanFunctionBody = GetStatementsWithoutInlineDirective(
            node.Body.Statements,
            inlineDirectiveTrivia
        );

        var namedParameters = GetNamedParametersFromFunction(node);

        if (namedParameters.IsOk)
        {
            SeparatedSyntaxList<NamedParameterSyntax> parameters = namedParameters.Ok.Value;

            // Get the return statement nodes for the current function.
            // We don't descent into inner functions since they contain returns
            // that are irrelevant to the outer function.
            ImmutableArray<ReturnStatementSyntax> returnNodes = node.Body
                .DescendantNodes(
                    node =>
                        node.Kind() switch
                        {
                            SyntaxKind.AnonymousFunctionExpression
                            or SyntaxKind.LocalFunctionDeclarationStatement
                            or SyntaxKind.FunctionDeclarationStatement
                                => false,
                            _ => true
                        }
                )
                .Where(node => node.IsKind(SyntaxKind.ReturnStatement))
                .Cast<ReturnStatementSyntax>()
                .ToImmutableArray();

            int maxReturnCount = returnNodes.Max(node => node.Expressions.Count);

            InlineFunctionInfo info = new(node, parameters, cleanFunctionBody, maxReturnCount);

            _functions.Add(info);
        }
        else
        {
            _diagnostics.Add(namedParameters.Err.Value);
        }

        base.VisitLocalFunctionDeclarationStatement(node);
    }

    private static SyntaxList<StatementSyntax> GetStatementsWithoutInlineDirective(
        SyntaxList<StatementSyntax> statements,
        SyntaxTrivia inlineDirectiveTrivia
    )
    {
        if (!statements.Any())
        {
            return statements;
        }

        StatementSyntax firstStatement = statements.First();

        return statements.Replace(
            firstStatement,
            firstStatement.RemoveLeadingTrivia(inlineDirectiveTrivia)
        );
    }

    private static Result<
        SeparatedSyntaxList<NamedParameterSyntax>,
        Diagnostic
    > GetNamedParametersFromFunction(LocalFunctionDeclarationStatementSyntax function)
    {
        SeparatedSyntaxList<ParameterSyntax> parameters = function.Parameters.Parameters;

        // There should not be any vararg parameters.
        // Only the last parameter in a function can be vararg, so
        // just check that.
        if (parameters.Any() && parameters.Last().IsKind(SyntaxKind.VarArgParameter))
        {
            var diagnostic = Diagnostic.Create(
                InlinerDiagnostics.CannotInlineVariadicFunction,
                parameters.Last().GetLocation()
            );

            return Result.Err<SeparatedSyntaxList<NamedParameterSyntax>, Diagnostic>(diagnostic);
        }

        // Since we already did checking for vararg parameters, we guarantee all the parameters are of the right type,
        // so force cast all elements.
        ImmutableArray<NamedParameterSyntax> namedParameters = parameters
            .Cast<NamedParameterSyntax>()
            .ToImmutableArray();

        return Result.Ok<SeparatedSyntaxList<NamedParameterSyntax>, Diagnostic>(
            SyntaxFactory.SeparatedList(namedParameters)
        );
    }

    /// <summary>
    /// Gets a valid inline directive from a trivia list.
    /// A valid inline directive must be on the first line of the function body.
    /// </summary>
    /// <param name="trivias"></param>
    /// <returns></returns>
    private static Result<SyntaxTrivia, Unit> GetInlineDirective(SyntaxTriviaList trivias)
    {
        // FIXME: Add a dynamic way of determining the inline directive (user customizable)
        const string INLINE_DIRECTIVE = "--!!INLINE_FUNCTION";

        if (!trivias.Any())
        {
            return Result.Err<SyntaxTrivia, Unit>(Unit.Default);
        }

        SyntaxTrivia firstNonWhitespaceTrivia = trivias.FirstOrDefault(
            trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia)
        );

        if (
            !firstNonWhitespaceTrivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || firstNonWhitespaceTrivia.ToString() != INLINE_DIRECTIVE
        )
        {
            return Result.Err<SyntaxTrivia, Unit>(Unit.Default);
        }

        return Result.Ok<SyntaxTrivia, Unit>(firstNonWhitespaceTrivia);
    }

    /// <summary>
    /// Gets the leading trivia of the function body in a function declaration.
    /// </summary>
    /// <param name="function"></param>
    /// <returns></returns>
    private static SyntaxTriviaList GetFunctionBodyLeadingTrivia(
        LocalFunctionDeclarationStatementSyntax function
    )
    {
        SyntaxList<StatementSyntax> bodyStatements = function.Body.Statements;

        return bodyStatements.Any() switch
        {
            true => bodyStatements.First().GetLeadingTrivia(),
            // Trivia within the body of an empty function is always attached
            // to the end keyword
            false => function.EndKeyword.LeadingTrivia,
        };
    }
}
