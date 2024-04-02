using System.Collections.Immutable;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using LuaInliner.Common;
using LuaInliner.Core.Extensions;

namespace LuaInliner.Core.Collectors;

using DiagnosticList = IReadOnlyList<Diagnostic>;
using InlineFunctionList = IReadOnlyList<InlineFunction>;

/// <summary>
/// Collect functions that have been marked as inline.
/// </summary>
internal sealed class InlineFunctionCollector : LuaSyntaxWalker
{
    public static Result<InlineFunctionList, DiagnosticList> Collect(SyntaxNode node)
    {
        InlineFunctionCollector collector = new();
        collector.Visit(node);

        DiagnosticList diagnostics = collector._diagnostics;

        return diagnostics.Count != 0
            ? Result.Err<InlineFunctionList, DiagnosticList>(diagnostics)
            : Result.Ok<InlineFunctionList, DiagnosticList>(collector._inlineFunctions);
    }

    private readonly List<InlineFunction> _inlineFunctions = [];
    private readonly HashSet<SyntaxTrivia> _validInlineDirectives = [];
    private readonly List<Diagnostic> _diagnostics = [];

    private InlineFunctionCollector()
        : base(SyntaxWalkerDepth.Trivia) { }

    public override void VisitLocalFunctionDeclarationStatement(
        LocalFunctionDeclarationStatementSyntax node
    )
    {
        SyntaxTriviaList bodyLeadingTrivia = GetFunctionBodyLeadingTrivia(node);
        Result<SyntaxTrivia, Unit> inlineDirective = GetInlineDirective(bodyLeadingTrivia);

        // Not an inline function
        if (inlineDirective.IsErr)
        {
            base.VisitLocalFunctionDeclarationStatement(node);
            return;
        }

        var namedParameters = GetNamedParameters(node);

        if (namedParameters.Err is { HasValue: true, Value: Diagnostic varargDiagnostic })
        {
            _diagnostics.Add(varargDiagnostic);

            base.VisitLocalFunctionDeclarationStatement(node);
            return;
        }

        SyntaxTrivia inlineDirectiveTrivia = inlineDirective.Ok.Value;

        // We don't want to keep the inline directive in case the user plans
        // on running the file through the inliner multiple times
        SyntaxList<StatementSyntax> cleanedFunctionBody = RemoveLeadingTriviaFromStatements(
            node.Body.Statements,
            inlineDirectiveTrivia
        );

        SeparatedSyntaxList<NamedParameterSyntax> parameters = namedParameters.Ok.Value;

        // Get the return statement nodes for the current function
        IReadOnlyList<ReturnStatementSyntax> returns = GetReturnStatements(node);

        int maxReturnCount = returns.Any() ? returns.Max(node => node.Expressions.Count) : 0;

        InlineFunction inlineFunction =
            new(node, parameters, cleanedFunctionBody, returns, maxReturnCount);

        _inlineFunctions.Add(inlineFunction);

        // Keep track of all valid usages of the inline directive
        // so we can check for all invalid usages
        _validInlineDirectives.Add(inlineDirectiveTrivia);

        base.VisitLocalFunctionDeclarationStatement(node);
    }

    /// <summary>
    /// Validate the usages of the inline directive.
    /// </summary>
    /// <param name="trivia"></param>
    public override void VisitTrivia(SyntaxTrivia trivia)
    {
        if (IsInlineDirective(trivia) && !_validInlineDirectives.Contains(trivia))
        {
            var diagnostic = Diagnostic.Create(
                InlinerDiagnostics.InvalidInlineDirectiveUsage,
                trivia.GetLocation()
            );

            _diagnostics.Add(diagnostic);
        }

        base.VisitTrivia(trivia);
    }

    /// <summary>
    /// Returns all the <see cref="ReturnStatementSyntax"/> within a function.
    /// </summary>
    /// <param name="function"></param>
    /// <returns></returns>
    private static IReadOnlyList<ReturnStatementSyntax> GetReturnStatements(
        LocalFunctionDeclarationStatementSyntax function
    )
    {
        StatementListSyntax body = function.Body;

        // We don't descent into inner functions since they contain returns
        // that are irrelevant to the outer function
        return body.DescendantNodes(node => !IsFunctionNode(node))
            .Where(node => node.IsKind(SyntaxKind.ReturnStatement))
            .Cast<ReturnStatementSyntax>()
            .ToImmutableArray();
    }

    /// <summary>
    /// Returns whether the node is a function.
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    private static bool IsFunctionNode(SyntaxNode node)
    {
        return node.Kind() switch
        {
            SyntaxKind.AnonymousFunctionExpression
            or SyntaxKind.LocalFunctionDeclarationStatement
            or SyntaxKind.FunctionDeclarationStatement
                => true,
            _ => false
        };
    }

    /// <summary>
    /// Removes the specified leading trivia from the statements.
    /// </summary>
    /// <param name="statements"></param>
    /// <param name="trivia"></param>
    /// <returns></returns>
    private static SyntaxList<StatementSyntax> RemoveLeadingTriviaFromStatements(
        SyntaxList<StatementSyntax> statements,
        SyntaxTrivia trivia
    )
    {
        if (!statements.Any())
        {
            return statements;
        }

        StatementSyntax leadingStatement = statements.First();
        StatementSyntax withoutLeadingTrivia = leadingStatement.RemoveLeadingTrivia(trivia);

        return statements.Replace(leadingStatement, withoutLeadingTrivia);
    }

    /// <summary>
    /// Returns all named parameters from the function.
    /// </summary>
    /// <param name="function"></param>
    /// <returns></returns>
    private static Result<SeparatedSyntaxList<NamedParameterSyntax>, Diagnostic> GetNamedParameters(
        LocalFunctionDeclarationStatementSyntax function
    )
    {
        SeparatedSyntaxList<ParameterSyntax> parameters = function.Parameters.Parameters;

        // We can't inline any vararg parameters. Since only the last parameter can be vararg,
        // we only need to check that
        if (parameters.Any() && parameters.Last().IsKind(SyntaxKind.VarArgParameter))
        {
            var diagnostic = Diagnostic.Create(
                InlinerDiagnostics.CannotInlineVariadicFunction,
                parameters.Last().GetLocation()
            );

            return Result.Err<SeparatedSyntaxList<NamedParameterSyntax>, Diagnostic>(diagnostic);
        }

        var namedParameters = SyntaxFactory.SeparatedList(parameters.Cast<NamedParameterSyntax>());

        return Result.Ok<SeparatedSyntaxList<NamedParameterSyntax>, Diagnostic>(namedParameters);
    }

    /// <summary>
    /// Gets a valid inline directive from a trivia list.
    /// A valid inline directive must be on the first line of the function body.
    /// </summary>
    /// <param name="trivias"></param>
    /// <returns></returns>
    private static Result<SyntaxTrivia, Unit> GetInlineDirective(SyntaxTriviaList trivias)
    {
        if (!trivias.Any())
        {
            return Result.Err<SyntaxTrivia, Unit>(Unit.Default);
        }

        SyntaxTrivia firstNonWhitespaceTrivia = trivias.FirstOrDefault(trivia =>
            !trivia.IsKind(SyntaxKind.WhitespaceTrivia)
        );

        if (!IsInlineDirective(firstNonWhitespaceTrivia))
        {
            return Result.Err<SyntaxTrivia, Unit>(Unit.Default);
        }

        return Result.Ok<SyntaxTrivia, Unit>(firstNonWhitespaceTrivia);
    }

    /// <summary>
    /// Returns whether the trivia is an inline directive.
    /// </summary>
    /// <param name="trivia"></param>
    /// <returns></returns>
    private static bool IsInlineDirective(SyntaxTrivia trivia)
    {
        // TODO: Add a dynamic way of determining the inline directive (user customizable)
        const string INLINE_DIRECTIVE = "--!!INLINE_FUNCTION";

        return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            && trivia.ToString() == INLINE_DIRECTIVE;
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
        SyntaxList<StatementSyntax> statements = function.Body.Statements;

        // Leading trivia of the function body is always attached
        // to the first token that proceeds the function signature
        return statements.Any()
            ? statements.First().GetLeadingTrivia()
            : function.EndKeyword.LeadingTrivia;
    }
}
