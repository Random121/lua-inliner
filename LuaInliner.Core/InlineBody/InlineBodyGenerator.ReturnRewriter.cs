using System.Collections.Immutable;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace LuaInliner.Core.InlineBody;

internal static partial class InlineBodyGenerator
{
    /// <summary>
    /// Rewrite return statements within the inline body.
    /// </summary>
    private sealed class ReturnRewriter : LuaSyntaxRewriter
    {
        public static SyntaxNode Rewrite(SyntaxNode node, IList<string> returnVariableNames)
        {
            ReturnRewriter rewriter = new(returnVariableNames);
            return rewriter.Visit(node);
        }

        private readonly ImmutableArray<PrefixExpressionSyntax> _returnVariableIdentifiers;

        private ReturnRewriter(IList<string> returnVariableNames)
        {
            _returnVariableIdentifiers = returnVariableNames
                .Select(SyntaxFactory.IdentifierName)
                .Cast<PrefixExpressionSyntax>()
                .ToImmutableArray();
        }

        public override SyntaxList<TNode> VisitList<TNode>(SyntaxList<TNode> list)
        {
            if (typeof(TNode) != typeof(StatementSyntax))
            {
                return base.VisitList(list);
            }

            List<StatementSyntax> statements = [];

            foreach (StatementSyntax originalStatement in list.Cast<StatementSyntax>())
            {
                bool isFunctionDeclaration = originalStatement.Kind() switch
                {
                    SyntaxKind.LocalFunctionDeclarationStatement
                    or SyntaxKind.FunctionDeclarationStatement
                        => true,
                    _ => false
                };

                // We don't visit inside function declaration nodes since all
                // the returns inside are irrelevant to the current function
                if (isFunctionDeclaration)
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

                AssignmentStatementSyntax assignment = SyntaxFactory.AssignmentStatement(
                    SyntaxFactory.SeparatedList(neededReturnIdentifiers),
                    returnValues
                );

                statements.Add(assignment);

                // Emulate the normal control flow of a function when
                // encountering a return (stopping execution at that point).
                // This works as we are wrapping the entire function with a
                // dummy loop statement (repeat, while, for, etc) that only does one iteration.
                statements.Add(SyntaxConstants.BREAK_STATEMENT);
            }

            return SyntaxFactory.List(statements.Cast<TNode>());
        }

        /// <summary>
        /// Override this visit method so we don't visit inside this node
        /// since all the returns inside are irrelevant to the current function.
        /// Since this is not a statement, it can't be handled inside of VisitList().
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public override SyntaxNode? VisitAnonymousFunctionExpression(
            AnonymousFunctionExpressionSyntax node
        )
        {
            return node;
        }
    }
}
