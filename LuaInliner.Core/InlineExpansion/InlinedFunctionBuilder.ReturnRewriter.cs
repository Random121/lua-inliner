using System.Collections.Immutable;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace LuaInliner.Core.InlineExpansion;

internal sealed partial class InlinedFunctionBuilder
{
    /// <summary>
    /// Rewrite all return statements to emulate a return in a inlined function.
    /// </summary>
    private sealed class ReturnRewriter : LuaSyntaxRewriter
    {
        public static SyntaxNode Rewrite(
            SyntaxNode node,
            IReadOnlyList<IdentifierNameSyntax> returnVariableIdentifiers
        )
        {
            ReturnRewriter rewriter = new(returnVariableIdentifiers);
            return rewriter.Visit(node);
        }

        private readonly ImmutableArray<PrefixExpressionSyntax> _returnVariableIdentifiers;

        private ReturnRewriter(IReadOnlyList<IdentifierNameSyntax> returnVariableIdentifiers)
        {
            _returnVariableIdentifiers = returnVariableIdentifiers
                .Cast<PrefixExpressionSyntax>()
                .ToImmutableArray();
        }

        public override SyntaxList<TNode> VisitList<TNode>(SyntaxList<TNode> list)
        {
            if (typeof(TNode) != typeof(StatementSyntax))
            {
                return base.VisitList(list);
            }

            List<StatementSyntax> statements = new(list.Count);

            foreach (StatementSyntax originalStatement in list.Cast<StatementSyntax>())
            {
                // We don't visit inside function declaration nodes since all
                // the returns inside are irrelevant to the current function
                if (IsFunctionNode(originalStatement))
                {
                    statements.Add(originalStatement);
                    continue;
                }

                StatementSyntax statement = (StatementSyntax)Visit(originalStatement);

                if (!statement.IsKind(SyntaxKind.ReturnStatement))
                {
                    statements.Add(statement);
                    continue;
                }

                ReturnStatementSyntax returnStatement = (ReturnStatementSyntax)statement;
                SeparatedSyntaxList<ExpressionSyntax> returnValues = returnStatement.Expressions;

                IEnumerable<PrefixExpressionSyntax> neededReturnIdentifiers =
                    _returnVariableIdentifiers.Take(returnValues.Count);

                var returnValueAssignment = SyntaxFactory.AssignmentStatement(
                    SyntaxFactory.SeparatedList(neededReturnIdentifiers),
                    returnValues
                );

                statements.Add(returnValueAssignment);

                // Emulate the normal control flow of a function when
                // encountering a return (stopping execution at that point).
                // This works as we are wrapping the entire function with a
                // dummy loop statement (repeat, while, for, etc) that only does one iteration.
                statements.Add(SyntaxConstants.BREAK_STATEMENT);
            }

            return SyntaxFactory.List(statements.Cast<TNode>());
        }

        /// <summary>
        /// We don't descent into inner functions since they contain returns
        /// that are irrelevant to the outer function.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public override SyntaxNode? VisitAnonymousFunctionExpression(
            AnonymousFunctionExpressionSyntax node
        )
        {
            return node;
        }

        /// <summary>
        /// Returns whether the node is a function.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private static bool IsFunctionNode(SyntaxNode node)
        {
            // TODO: this method is duplicated from InlineFunctionCollector
            // so place both in a helper class somewhere

            return node.Kind() switch
            {
                SyntaxKind.AnonymousFunctionExpression
                or SyntaxKind.LocalFunctionDeclarationStatement
                or SyntaxKind.FunctionDeclarationStatement
                    => true,
                _ => false
            };
        }
    }
}
