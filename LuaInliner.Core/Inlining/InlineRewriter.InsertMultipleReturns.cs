using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace LuaInliner.Core.Inlining;

internal sealed partial class InlineRewriter : LuaSyntaxRewriter
{
    // NOTE: The node we use when looking up the edits must be the original unvisited node
    // since changes may have occured during the base visit and the edits were added
    // based on the original node. However, the changes must be applied to the visited node
    // since we don't want to lose changes.

    public override SyntaxNode? VisitEqualsValuesClause(EqualsValuesClauseSyntax node)
    {
        var visitedNode = (EqualsValuesClauseSyntax)base.VisitEqualsValuesClause(node)!;

        return GetNodeWithAddedReturns(
            node,
            visitedNode,
            visitedNode.Values,
            visitedNode.WithValues
        );
    }

    public override SyntaxNode? VisitExpressionListFunctionArgument(
        ExpressionListFunctionArgumentSyntax node
    )
    {
        var visitedNode = (ExpressionListFunctionArgumentSyntax)
            base.VisitExpressionListFunctionArgument(node)!;

        return GetNodeWithAddedReturns(
            node,
            visitedNode,
            visitedNode.Expressions,
            visitedNode.WithExpressions
        );
    }

    public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
    {
        var visitedNode = (ReturnStatementSyntax)base.VisitReturnStatement(node)!;

        return GetNodeWithAddedReturns(
            node,
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

        return GetNodeWithAddedReturns(
            node,
            visitedNode,
            visitedNode.Fields,
            visitedNode.WithFields,
            SyntaxFactory.UnkeyedTableField
        );
    }

    /// <summary>
    /// Generic method to adding extra returns onto a expression list node.
    /// </summary>
    /// <typeparam name="TNode"></typeparam>
    /// <typeparam name="TElement">Type of the element within the expression list</typeparam>
    /// <param name="lookupNode">Node used to lookup information about multiple return edits (the original unvisited node)</param>
    /// <param name="actualNode">Node which is the most current (the visited node)</param>
    /// <param name="oldValues"></param>
    /// <param name="replaceValuesFunction"></param>
    /// <param name="identifierTransformer">Optional transformer function that converts <see cref="IdentifierNameSyntax"/> to <typeparamref name="TElement"/></param>
    /// <returns></returns>
    private TNode GetNodeWithAddedReturns<TNode, TElement>(
        TNode lookupNode,
        TNode actualNode,
        SeparatedSyntaxList<TElement> oldValues,
        Func<SeparatedSyntaxList<TElement>, TNode> replaceValuesFunction,
        Func<IdentifierNameSyntax, TElement>? identifierTransformer = null
    )
        where TNode : LuaSyntaxNode
        where TElement : LuaSyntaxNode
    {
        bool handleMultipleReturns = _multipleReturnsEdits.TryGetValue(
            lookupNode,
            out MultipleReturnsEdits? multipleReturnsEdits
        );

        if (!handleMultipleReturns || multipleReturnsEdits is null)
        {
            return actualNode;
        }

        List<IdentifierNameSyntax> returnVariableIdentifiers =
            multipleReturnsEdits.ReturnIdentifierNames;

        IEnumerable<TElement> identifierValues =
            identifierTransformer != null
                ? returnVariableIdentifiers.Select(identifierTransformer)
                : returnVariableIdentifiers.Cast<TElement>();

        SeparatedSyntaxList<TElement> newValues = oldValues
            .RemoveAt(oldValues.Count - 1)
            .AddRange(identifierValues);

        return replaceValuesFunction(newValues)
            .NormalizeWhitespace()
            .WithTrailingTrivia(SyntaxConstants.EOL_TRIVIA);
    }
}
