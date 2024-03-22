using System.Collections.Immutable;
using System.Diagnostics;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using LuaInliner.Core.Collectors;
using LuaInliner.Core.Extensions;
using LuaInliner.Core.InlineExpansion;
using LuaInliner.Core.Naming;

namespace LuaInliner.Core.Inlining;

/// <summary>
/// Rewriter to perform inlining.
/// </summary>
internal sealed partial class InlineRewriter : LuaSyntaxRewriter
{
    public static SyntaxNode Rewrite(
        SyntaxNode node,
        Script script,
        IReadOnlyList<InlineFunctionCall> inlineFunctionCalls
    )
    {
        InlineRewriter rewriter = new(script, inlineFunctionCalls);
        return rewriter.Visit(node);
    }

    private readonly Script _script;

    private readonly ImmutableDictionary<
        FunctionCallExpressionSyntax,
        InlineFunctionCall
    > _inlineFunctionCallLookup;

    private readonly Dictionary<StatementSyntax, InliningEdits> _inliningEdits = [];

    private readonly Dictionary<SyntaxNode, MultipleReturnsEdits> _multipleReturnsEdits = [];

    private InlineRewriter(Script script, IReadOnlyList<InlineFunctionCall> inlineFunctionCalls)
    {
        _script = script;
        _inlineFunctionCallLookup = inlineFunctionCalls.ToImmutableDictionary(call =>
            call.CallExpressionNode
        );
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
            StatementSyntax statement = (StatementSyntax)Visit(originalStatement);

            if (_inliningEdits.TryGetValue(originalStatement, out InliningEdits? edits))
            {
                statements.AddRange(edits.Insertions);

                if (edits.RemoveCallingStatement)
                {
                    continue;
                }
            }

            statements.Add(statement);
        }

        return SyntaxFactory.List(statements.Cast<TNode>());
    }

    public override SyntaxNode? VisitFunctionCallExpression(FunctionCallExpressionSyntax node)
    {
        bool isInlineCall = _inlineFunctionCallLookup.TryGetValue(
            node,
            out InlineFunctionCall? inlineFunctionCall
        );

        if (!isInlineCall || inlineFunctionCall is null)
        {
            return base.VisitFunctionCallExpression(node);
        }

        // Visit (or inline) arguments first to handle nested inline function calls
        // as the returns from inner calls are needed to inline the outer calls
        SeparatedSyntaxList<ExpressionSyntax> arguments = GetNormalizedCallArgument(
            (FunctionArgumentSyntax)Visit(node.Argument)
        );

        InlineFunction calledFunction = inlineFunctionCall.CalledFunction;
        StatementSyntax parentStatement = GetParentStatementOfExpression(node);
        IScope currentScope = _script.GetScope(node)!;
        SyntaxNode parentNode = node.Parent!;

        Debug.Assert(parentNode is not null, "Function call node does not have a parent");

        SeparatedSyntaxList<ExpressionSyntax> argumentValues = GetActualArgumentValues(
            arguments,
            calledFunction.Parameters.Count
        );

        List<IdentifierNameSyntax> returnVariableIdentifiers = GenerateReturnVariableIdentifiers(
            currentScope,
            calledFunction.MaxReturnCount
        );

        IReadOnlyList<StatementSyntax> inlinedFunctionStatements = new InlinedFunctionBuilder(
            calledFunction.Body,
            calledFunction.Parameters,
            argumentValues,
            returnVariableIdentifiers
        ).ToStatements();

        InliningEdits inliningEdits = _inliningEdits.GetOrCreate(parentStatement);

        inliningEdits.Insertions.AddRange(inlinedFunctionStatements);

        // We have to remove this node if it is an ExpressionStatement since the return values
        // are not used and would cause an error in the Lua script if it is left in.
        if (parentNode.IsKind(SyntaxKind.ExpressionStatement))
        {
            inliningEdits.RemoveCallingStatement = true;

            // Can return anything since it will be removed later on
            return node;
        }

        // Functions without a explicit return value implicitly returns nil
        // (this isn't the exact behaviour but is a good enough substitute).
        if (returnVariableIdentifiers.Count == 0)
        {
            return SyntaxConstants.NIL_LITERAL;
        }

        if (ShouldReturnAllValues(node))
        {
            MultipleReturnsEdits multipleReturnEdits = new(returnVariableIdentifiers);

            SyntaxNode returnValueContainingNode = parentNode.Kind() switch
            {
                SyntaxKind.UnkeyedTableField => parentNode.Parent!,
                _ => parentNode
            };

            _multipleReturnsEdits.Add(returnValueContainingNode, multipleReturnEdits);

            // Can return anything since it will be removed later on
            return node;
        }

        // Only insert the first return value
        return returnVariableIdentifiers[0].WithTriviaFrom(node);
    }

    /// <summary>
    /// Generates a list of return variable identifiers that are unique to the current scope.
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    private static List<IdentifierNameSyntax> GenerateReturnVariableIdentifiers(
        IScope scope,
        int count
    )
    {
        // TODO: make this user configurable
        const string RETURN_VARIABLE_NAME_PREFIX = "__inline_return";

        UniqueNameGenerator nameGenerator = new(scope);
        List<IdentifierNameSyntax> returnVariableIdentifiers = new(count);

        for (int i = 0; i < count; i++)
        {
            string name = nameGenerator.GetUniqueName(RETURN_VARIABLE_NAME_PREFIX);
            var identifier = SyntaxFactory.IdentifierName(name);

            returnVariableIdentifiers.Add(identifier);
        }

        return returnVariableIdentifiers;
    }

    /// <summary>
    /// Returns whether a function call should return all of its return values.
    /// <br/><br/>
    /// The only scenario where a function call would return all of its return values is when it is
    /// last in a list of expressions.
    /// </summary>
    /// <param name="call">Function call node</param>
    /// <returns></returns>
    private static bool ShouldReturnAllValues(FunctionCallExpressionSyntax call)
    {
        SyntaxNode parentNode = call.Parent!;

        // Table values are structured differently from the other "expression list" like nodes
        if (parentNode.IsKind(SyntaxKind.UnkeyedTableField))
        {
            var tableExpression = (TableConstructorExpressionSyntax)parentNode.Parent!;

            return tableExpression.Fields.Last().Equals(parentNode);
        }

        SeparatedSyntaxList<SyntaxNode>? containingExpressionList = parentNode switch
        {
            EqualsValuesClauseSyntax parent => parent.Values,
            ExpressionListFunctionArgumentSyntax parent => parent.Expressions,
            ReturnStatementSyntax parent => parent.Expressions,
            _ => null,
        };

        // Function call is not in an expression list
        if (!containingExpressionList.HasValue)
        {
            return false;
        }

        return containingExpressionList.Value.Last().Equals(call);
    }

    /// <summary>
    /// Gets the actual argument values that is passed into the function.
    /// <br/><br/>
    /// If a parameter isn't given a corresponding argument value, it will be given <c>nil</c> instead.
    /// </summary>
    /// <param name="arguments"></param>
    /// <param name="parameterCount"></param>
    /// <returns></returns>
    private static SeparatedSyntaxList<ExpressionSyntax> GetActualArgumentValues(
        SeparatedSyntaxList<ExpressionSyntax> arguments,
        int parameterCount
    )
    {
        // Function doesn't expect any arguments
        if (parameterCount == 0)
        {
            return SyntaxFactory.SeparatedList<ExpressionSyntax>();
        }

        int argumentCount = arguments.Count;

        // If a parameter does not have a corresponding argument value
        // when the function is called, it will be initialized with nil
        if (argumentCount < parameterCount)
        {
            int numberOfNilsNeeded = parameterCount - argumentCount;
            var nilExpressions = Enumerable.Repeat(SyntaxConstants.NIL_LITERAL, numberOfNilsNeeded);

            return SyntaxFactory.SeparatedList(arguments.AddRange(nilExpressions));
        }

        // Amount of arguments is smaller or equal to number of expected arguments
        return SyntaxFactory.SeparatedList(arguments.Take(parameterCount));
    }

    /// <summary>
    /// Gets the direct parent of the current expression that is of type <see cref="StatementSyntax"/>.
    /// </summary>
    /// <param name="node"></param>
    /// <returns></returns>
    private static StatementSyntax GetParentStatementOfExpression(ExpressionSyntax node)
    {
        StatementSyntax? parentStatement = node.FirstAncestorOrSelf<StatementSyntax>();

        Debug.Assert(parentStatement is not null, "Expression is not contained within a statement");

        return parentStatement;
    }

    /// <summary>
    /// Normalizes the arguments of a function call to be a list of expressions.
    /// </summary>
    /// <param name="argument"></param>
    /// <returns></returns>
    /// <exception cref="UnreachableException"></exception>
    private static SeparatedSyntaxList<ExpressionSyntax> GetNormalizedCallArgument(
        FunctionArgumentSyntax argument
    )
    {
        return argument switch
        {
            ExpressionListFunctionArgumentSyntax arg => arg.Expressions,
            StringFunctionArgumentSyntax arg
                => SyntaxFactory.SeparatedList<ExpressionSyntax>(
                    ImmutableArray.Create(arg.Expression)
                ),
            TableConstructorFunctionArgumentSyntax arg
                => SyntaxFactory.SeparatedList<ExpressionSyntax>(
                    ImmutableArray.Create(arg.TableConstructor)
                ),
            _ => throw new UnreachableException("Unknown function call argument type")
        };
    }
}
