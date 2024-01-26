using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace LuaInliner.Core.Inlining;

internal sealed partial class InlineRewriter : LuaSyntaxRewriter
{
    public override SyntaxNode? VisitEqualsValuesClause(EqualsValuesClauseSyntax node)
    {
        var visitedNode = (EqualsValuesClauseSyntax)base.VisitEqualsValuesClause(node)!;

        bool handleMultipleReturns = _multipleReturnsTaskLookup.TryGetValue(
            visitedNode,
            out MultipleReturnsTask? multipleReturnsTask
        );

        if (!handleMultipleReturns || multipleReturnsTask == null)
        {
            return visitedNode;
        }

        SeparatedSyntaxList<ExpressionSyntax> oldValues = visitedNode.Values;
        SeparatedSyntaxList<ExpressionSyntax> newValues = oldValues
            .RemoveAt(oldValues.Count - 1)
            .AddRange(multipleReturnsTask.ReturnIdentifierNames);

        return visitedNode
            .WithValues(newValues)
            .NormalizeWhitespace()
            .WithTrailingTrivia(SyntaxConstants.EOL_TRIVIA);
    }

    public override 
        ? VisitExpressionListFunctionArgument(
        ExpressionListFunctionArgumentSyntax node
    )
    {
        var visitedNode = (ExpressionListFunctionArgumentSyntax)
            base.VisitExpressionListFunctionArgument(node)!;

        bool handleMultipleReturns = _multipleReturnsTaskLookup.TryGetValue(
            visitedNode,
            out MultipleReturnsTask? multipleReturnsTask
        );

        if (!handleMultipleReturns || multipleReturnsTask == null)
        {
            return visitedNode;
        }

        SeparatedSyntaxList<ExpressionSyntax> oldValues = visitedNode.Expressions;
        SeparatedSyntaxList<ExpressionSyntax> newValues = oldValues
            .RemoveAt(oldValues.Count - 1)
            .AddRange(multipleReturnsTask.ReturnIdentifierNames);

        return visitedNode
            .WithExpressions(newValues)
            .NormalizeWhitespace()
            .WithTrailingTrivia(SyntaxConstants.EOL_TRIVIA);
    }

    public override SyntaxNode? VisitTableConstructorExpression(
        TableConstructorExpressionSyntax node
    )
    {
        var visitedNode = (TableConstructorExpressionSyntax)
            base.VisitTableConstructorExpression(node)!;

        bool handleMultipleReturns = _multipleReturnsTaskLookup.TryGetValue(
            visitedNode,
            out MultipleReturnsTask? multipleReturnsTask
        );

        if (!handleMultipleReturns || multipleReturnsTask == null)
        {
            return visitedNode;
        }

        SeparatedSyntaxList<TableFieldSyntax> oldValues = visitedNode.Fields;

        IEnumerable<UnkeyedTableFieldSyntax> identifierFields =
            multipleReturnsTask.ReturnIdentifierNames.Select(SyntaxFactory.UnkeyedTableField);

        SeparatedSyntaxList<TableFieldSyntax> newValues = oldValues
            .RemoveAt(oldValues.Count - 1)
            .AddRange(identifierFields);

        return visitedNode
            .WithFields(newValues)
            .NormalizeWhitespace()
            .WithTrailingTrivia(SyntaxConstants.EOL_TRIVIA);
    }

    public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
    {
        var visitedNode = (ReturnStatementSyntax)base.VisitReturnStatement(node)!;

        bool handleMultipleReturns = _multipleReturnsTaskLookup.TryGetValue(
            visitedNode,
            out MultipleReturnsTask? multipleReturnsTask
        );

        if (!handleMultipleReturns || multipleReturnsTask == null)
        {
            return visitedNode;
        }

        SeparatedSyntaxList<ExpressionSyntax> oldValues = visitedNode.Expressions;
        SeparatedSyntaxList<ExpressionSyntax> newValues = oldValues
            .RemoveAt(oldValues.Count - 1)
            .AddRange(multipleReturnsTask.ReturnIdentifierNames);

        return visitedNode
            .WithExpressions(newValues)
            .NormalizeWhitespace()
            .WithTrailingTrivia(SyntaxConstants.EOL_TRIVIA);
    }
}
