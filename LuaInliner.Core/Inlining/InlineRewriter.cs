using System.Collections.Immutable;
using System.Diagnostics;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using LuaInliner.Core.Collectors;
using LuaInliner.Core.Extensions;
using LuaInliner.Core.InlineBody;
using LuaInliner.Core.Naming;

namespace LuaInliner.Core.Inlining;

/// <summary>
/// Edits to perform on the AST to inline a statement.
/// </summary>
internal record class InliningEdits
{
    // We are using a record class rather than a record struct for this
    // since record classes are reference types and we need to mutate
    // this object within an array while storing it in a temporary variable
    // (without creating a copy like in the case with record structs).

    /// <summary>
    /// Statements to be inserted in front of the current statement.
    /// </summary>
    public List<StatementSyntax> Insertions = [];

    /// <summary>
    /// Whether the calling statement should be removed.<br/>
    /// This is usually used if the calling statement doesn't have a return value.
    /// </summary>
    public bool RemoveCallingStatement = false;
}

/// <summary>
/// Edits to perform on the AST to support multiple returns from inline functions.
/// </summary>
/// <param name="ReturnIdentifierNames"></param>
internal record class MultipleReturnsEdits(
    ImmutableArray<IdentifierNameSyntax> ReturnIdentifierNames
);

internal sealed partial class InlineRewriter : LuaSyntaxRewriter
{
    public static SyntaxNode Rewrite(
        SyntaxNode node,
        Script script,
        IList<InlineFunctionCallInfo> functionCallInfos
    )
    {
        InlineRewriter rewriter = new(script, functionCallInfos);
        return rewriter.Visit(node);
    }

    private readonly Script _script;

    private readonly ImmutableDictionary<
        FunctionCallExpressionSyntax,
        InlineFunctionCallInfo
    > _callInfoLookup;

    private readonly UniqueNameGenerator _nameGenerator = new();

    /// <summary>
    /// Mapping between a statement that is being inlined and the edits that need to be performed.
    /// </summary>
    private readonly Dictionary<StatementSyntax, InliningEdits> _inliningEditsLookup = [];

    /// <summary>
    /// Lookup for the edits needed using the node which contains the return.
    /// </summary>
    public readonly Dictionary<SyntaxNode, MultipleReturnsEdits> _multipleReturnsEditsLookup = [];

    private InlineRewriter(Script script, IList<InlineFunctionCallInfo> functionCallInfos)
    {
        _script = script;
        _callInfoLookup = functionCallInfos.ToImmutableDictionary(info => info.CallExpressionNode);
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

            if (_inliningEditsLookup.TryGetValue(originalStatement, out InliningEdits? edits))
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
        bool isInlineCall = _callInfoLookup.TryGetValue(node, out InlineFunctionCallInfo? callInfo);

        if (!isInlineCall || callInfo is null)
        {
            return base.VisitFunctionCallExpression(node);
        }

        // Visit (or inline) arguments first to handle nested inline function calls
        // as the returns from inner calls are needed to inline the outer calls
        SeparatedSyntaxList<ExpressionSyntax> arguments = GetNormalizedCallArgument(
            (FunctionArgumentSyntax)Visit(node.Argument)
        );

        InlineFunctionInfo calledFunction = callInfo.CalledFunction;
        StatementSyntax parentStatement = GetParentStatementOfExpression(node);
        IScope currentScope = _script.GetScope(node)!;

        SeparatedSyntaxList<ExpressionSyntax> argumentValues = GetActualArgumentValues(
            arguments,
            calledFunction.Parameters.Count
        );

        List<string> returnVariableNames = GenerateReturnVariableNames(
            calledFunction.MaxReturnCount,
            currentScope
        );

        ImmutableArray<IdentifierNameSyntax> returnVariableIdentifiers = returnVariableNames
            .Select(SyntaxFactory.IdentifierName)
            .ToImmutableArray();

        SyntaxList<StatementSyntax> inlineFunctionBody = InlineBodyGenerator.Generate(
            calledFunction,
            argumentValues,
            returnVariableNames
        );

        InliningEdits inliningEdits = _inliningEditsLookup.GetOrCreate(parentStatement);
        SyntaxNode parentNode = node.Parent!;

        Debug.Assert(parentNode is not null, "Function call node does not have a parent");

        inliningEdits.Insertions.AddRange(inlineFunctionBody);

        // We have to remove this node if it is an ExpressionStatement since the return values
        // are not used and would cause an error in the Lua script if it is left in.
        if (parentNode.IsKind(SyntaxKind.ExpressionStatement))
        {
            inliningEdits.RemoveCallingStatement = true;

            // Can return anything since it will be removed later on
            return node;
        }

        // Functions without a explicit return value implicitly returns nil
        // (this isn't true in all cases but it is a good enough substitute)
        if (returnVariableNames.Count == 0)
        {
            return SyntaxConstants.NIL_LITERAL;
        }

        if (ShouldReturnAllValues(node))
        {
            MultipleReturnsEdits multipleReturnEdits = new(returnVariableIdentifiers);

            SyntaxNode returnValueContainingNode = parentNode.IsKind(SyntaxKind.UnkeyedTableField)
                ? parentNode.Parent!
                : parentNode;

            _multipleReturnsEditsLookup.Add(returnValueContainingNode, multipleReturnEdits);

            return node;
        }

        // Only insert the first return value
        return returnVariableIdentifiers[0].WithTriviaFrom(node);
    }

    /// <summary>
    /// Generates a list of return variable names that are unique to the current scope.
    /// </summary>
    /// <param name="maxReturnCount"></param>
    /// <param name="scope"></param>
    /// <returns></returns>
    private List<string> GenerateReturnVariableNames(int maxReturnCount, IScope scope)
    {
        const string RETURN_VARIABLE_NAME_PREFIX = "__inline_return";

        List<string> returnVariableNames = new(maxReturnCount);

        for (int i = 0; i < maxReturnCount; i++)
        {
            // TODO: Make this more performant since it is retrieving the
            // used variable names for the scope every iteration
            string name = _nameGenerator.GetUniqueName(scope, RETURN_VARIABLE_NAME_PREFIX);

            returnVariableNames.Add(name);
        }

        return returnVariableNames;
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
        // We don't know the exact type of the parent statement, but we do
        // know it is always contained within a statement list.
        static bool isParentStatement(StatementSyntax node) =>
            node.Parent.IsKind(SyntaxKind.StatementList);

        StatementSyntax? parentStatement = node.FirstAncestorOrSelf(
            (Func<StatementSyntax, bool>)isParentStatement
        );

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
