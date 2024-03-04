using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace LuaInliner.Core.Inlining;

internal sealed partial class InlineRewriter : LuaSyntaxRewriter
{
    public override SyntaxNode? VisitEqualsValuesClause(EqualsValuesClauseSyntax node)
    {
        var visitedNode = (EqualsValuesClauseSyntax)base.VisitEqualsValuesClause(node)!;

        return GetNodeWithAddedReturns(visitedNode, visitedNode.Values, visitedNode.WithValues);
    }

    public override SyntaxNode? VisitExpressionListFunctionArgument(
        ExpressionListFunctionArgumentSyntax node
    )
    {
        var visitedNode = (ExpressionListFunctionArgumentSyntax)
            base.VisitExpressionListFunctionArgument(node)!;

        return GetNodeWithAddedReturns(
            visitedNode,
            visitedNode.Expressions,
            visitedNode.WithExpressions
        );
    }

    public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
    {
        var visitedNode = (ReturnStatementSyntax)base.VisitReturnStatement(node)!;

        return GetNodeWithAddedReturns(
            visitedNode,
            visitedNode.Expressions,
            visitedNode.WithExpressions
        );
    }

    public override SyntaxNode? VisitTableConstructorExpression(
        TableConstructorExpressionSyntax node
    )
    {
        var visitedNode = (TableConstructorExpressionSyntax)
            base.VisitTableConstructorExpression(node)!;

        // Hacky code below since table nodes are structured differently from the other
        // "expression list" like nodes

        bool handleMultipleReturns = _multipleReturnsEditsLookup.TryGetValue(
            visitedNode,
            out MultipleReturnsEdits? multipleReturnsEdits
        );

        if (!handleMultipleReturns || multipleReturnsEdits is null)
        {
            return visitedNode;
        }

        SeparatedSyntaxList<TableFieldSyntax> oldValues = visitedNode.Fields;

        IEnumerable<UnkeyedTableFieldSyntax> identifierFields =
            multipleReturnsEdits.ReturnIdentifierNames.Select(SyntaxFactory.UnkeyedTableField);

        SeparatedSyntaxList<TableFieldSyntax> newValues = oldValues
            .RemoveAt(oldValues.Count - 1)
            .AddRange(identifierFields);

        return visitedNode
            .WithFields(newValues)
            .NormalizeWhitespace()
            .WithTrailingTrivia(SyntaxConstants.EOL_TRIVIA);
    }

    private TNode GetNodeWithAddedReturns<TNode>(
        TNode node,
        SeparatedSyntaxList<ExpressionSyntax> oldValues,
        Func<SeparatedSyntaxList<ExpressionSyntax>, TNode> replaceValuesFunc
    )
        where TNode : SyntaxNode
    {
        bool handleMultipleReturns = _multipleReturnsEditsLookup.TryGetValue(
            node,
            out MultipleReturnsEdits? multipleReturnsEdits
        );

        if (!handleMultipleReturns || multipleReturnsEdits is null)
        {
            return node;
        }

        IList<IdentifierNameSyntax> returnIdentifiers = multipleReturnsEdits.ReturnIdentifierNames;

        SeparatedSyntaxList<ExpressionSyntax> newValues = oldValues
            .RemoveAt(oldValues.Count - 1)
            .AddRange(returnIdentifiers);

        return replaceValuesFunc(newValues)
            .NormalizeWhitespace()
            .WithTrailingTrivia(SyntaxConstants.EOL_TRIVIA);
    }
}
