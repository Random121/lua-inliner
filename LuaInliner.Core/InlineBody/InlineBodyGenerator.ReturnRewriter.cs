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
        public static SyntaxNode Rewrite(SyntaxNode node, List<string> returnVariableNames)
        {
            ReturnRewriter rewriter = new(returnVariableNames);
            return rewriter.Visit(node);
        }

        private readonly List<string> _returnVariableNames;

        private ReturnRewriter(List<string> returnVariableNames)
        {
            _returnVariableNames = returnVariableNames;
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

                // We don't visit inside this node since all the returns inside are irrelevant
                // to the current function.
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

                SeparatedSyntaxList<ExpressionSyntax> returnValues = (
                    (ReturnStatementSyntax)statement
                ).Expressions;

                // Note that for the .Take() we can trust that there is always
                // enough return variable names since (if the InlineRewriter is working correctly)
                // the number of variable names we generate matches the max number of values returned.
                SeparatedSyntaxList<PrefixExpressionSyntax> returnVariableIdentifiers =
                    SyntaxFactory.SeparatedList(
                        _returnVariableNames
                            .Take(returnValues.Count)
                            .Select(SyntaxFactory.IdentifierName)
                            .Cast<PrefixExpressionSyntax>()
                    );

                AssignmentStatementSyntax assignment = SyntaxFactory.AssignmentStatement(
                    returnVariableIdentifiers,
                    returnValues
                );

                statements.Add(assignment);

                // Emulate the normal control flow of a function when encountering a return
                // (stopping execution at that point).
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
