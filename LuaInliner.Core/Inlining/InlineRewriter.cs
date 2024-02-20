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
/// Tasks to perform to inline a statement.<br/>
/// </summary>
internal record class InlineTask
{
    // We are using a record class rather than a record struct for this
    // since record classes are reference types and we need to mutate
    // this object within an array while storing it in a temporary variable
    // (without creating a copy like in the case with record structs).

    /// <summary>
    /// Inline results to be added in front of the current statement.
    /// </summary>
    public List<StatementSyntax> StatementsToAdd = [];

    /// <summary>
    /// Whether the calling statement should be removed if it doesn't need a return value.
    /// </summary>
    public bool RemoveCallingStatement;
}

/// <summary>
/// Task to insert multiple returns.
/// </summary>
/// <param name="ReturnIdentifierNames"></param>
internal record class MultipleReturnsTask(
    ImmutableArray<IdentifierNameSyntax> ReturnIdentifierNames
);

internal sealed partial class InlineRewriter : LuaSyntaxRewriter
{
    public static SyntaxNode Rewrite(
        SyntaxNode node,
        Script script,
        ImmutableArray<InlineFunctionCallInfo> functionCallInfos
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
    /// Mapping between a statement that is being inlined and the tasks that need to be performed.
    /// </summary>
    private readonly Dictionary<StatementSyntax, InlineTask> _inlineTaskLookup = [];

    /// <summary>
    /// Key is the node which contains the return.
    /// </summary>
    public Dictionary<SyntaxNode, MultipleReturnsTask> _multipleReturnsTaskLookup = [];

    private InlineRewriter(Script script, ImmutableArray<InlineFunctionCallInfo> functionCallInfos)
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

            if (_inlineTaskLookup.TryGetValue(originalStatement, out InlineTask? inlineTask))
            {
                statements.AddRange(inlineTask.StatementsToAdd);

                // This is an ExpressionStatement, so we remove it since it doesn't
                // use the returns and would cause an error in the Lua script
                // if it is left in.
                if (inlineTask.RemoveCallingStatement)
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

        if (!isInlineCall || callInfo == null)
        {
            return base.VisitFunctionCallExpression(node);
        }

        // Visit arguments first to handle nested inline function calls,
        // innermost calls must be inlined before outer calls (returns from inner calls
        // are needed to inline the outer calls).
        SeparatedSyntaxList<ExpressionSyntax> arguments = NormalizeCallArgument(
            (FunctionArgumentSyntax)Visit(node.Argument)
        );

        InlineFunctionInfo calledFunction = callInfo.CalledFunction;
        StatementSyntax parentStatement = GetParentStatementOfExpression(node);
        IScope currentScope = _script.GetScope(node)!;

        SeparatedSyntaxList<ExpressionSyntax> neededArguments = GetActualCallArguments(
            arguments,
            calledFunction.Parameters.Count
        );

        List<string> returnVariableNames = new(calledFunction.MaxReturnCount);

        for (int i = 0; i < calledFunction.MaxReturnCount; i++)
        {
            // TODO: Make this more performant since it is retrieving the
            // used variable names every iteration
            string name = _nameGenerator.GetUniqueName(currentScope, "__inline_return");

            returnVariableNames.Add(name);
        }

        SyntaxList<StatementSyntax> inlineBody = InlineBodyGenerator.Generate(
            calledFunction,
            neededArguments,
            returnVariableNames
        );

        InlineTask inlineTask = _inlineTaskLookup.GetOrCreate(parentStatement);

        inlineTask.StatementsToAdd.AddRange(inlineBody);

        SyntaxNode parentNode = node.Parent!;

        Debug.Assert(parentNode != null, "Parent node of function call is somehow null.");

        // Handle the case where we can't have a return value
        if (parentNode.IsKind(SyntaxKind.ExpressionStatement))
        {
            inlineTask.RemoveCallingStatement = true;

            // Can return anything since the function call will be removed
            // along with its parent statement later on
            return node;
        }

        // Functions without a explicit return value implicitly returns nil
        if (returnVariableNames.Count == 0)
        {
            return SyntaxConstants.NIL_LITERAL;
        }

        ImmutableArray<IdentifierNameSyntax> returnIdentifierNames = returnVariableNames
            .Select(SyntaxFactory.IdentifierName)
            .ToImmutableArray();

        if (ShouldReturnAllValues(node))
        {
            MultipleReturnsTask multipleReturnsTask = new(returnIdentifierNames);

            SyntaxNode returnContainingNode = parentNode.IsKind(SyntaxKind.UnkeyedTableField)
                ? parentNode.Parent!
                : parentNode;

            _multipleReturnsTaskLookup.Add(returnContainingNode, multipleReturnsTask);

            return node;
        }

        return returnIdentifierNames[0].WithTriviaFrom(node);
    }

    /// <summary>
    /// Determines whether the current call should return all of its values.
    /// <br/><br/>
    /// The only scenario where we have the function return all of its values is when it is
    /// last in a list of expressions.
    /// </summary>
    /// <param name="call">Function call node</param>
    /// <returns></returns>
    private static bool ShouldReturnAllValues(FunctionCallExpressionSyntax call)
    {
        SyntaxNode parentNode = call.Parent!;

        // Table values are constructed differently from the other "expression list" style nodes
        if (parentNode.IsKind(SyntaxKind.UnkeyedTableField))
        {
            var tableExpression = (TableConstructorExpressionSyntax)parentNode.Parent!;

            return tableExpression.Fields.Last().Equals(parentNode);
        }

        // List of expressions which will contain the return values
        SeparatedSyntaxList<SyntaxNode>? containingExpressionList = parentNode switch
        {
            EqualsValuesClauseSyntax parent => parent.Values,
            ExpressionListFunctionArgumentSyntax parent => parent.Expressions,
            ReturnStatementSyntax parent => parent.Expressions,
            _ => null,
        };

        if (!containingExpressionList.HasValue)
        {
            return false;
        }

        return containingExpressionList.Value.Last().Equals(call);
    }

    /// <summary>
    /// Gets the actual argument values that is passed into the function.
    /// <br/><br/>
    /// This means that if an argument value is missing in a position which expects one (has a corresponding parameter),
    /// then the value will be made <c>nil</c>.
    /// </summary>
    /// <param name="arguments"></param>
    /// <param name="parameterCount"></param>
    /// <returns></returns>
    private static SeparatedSyntaxList<ExpressionSyntax> GetActualCallArguments(
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
        // when the function is called, it will be nil.
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
    /// Gets the direct parent <see cref="StatementSyntax"/> of the current expression.
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

        Debug.Assert(parentStatement is not null);

        return parentStatement;
    }

    /// <summary>
    /// Normalizes the arguments of a function call.
    /// </summary>
    /// <param name="argument"></param>
    /// <returns></returns>
    /// <exception cref="UnreachableException"></exception>
    private static SeparatedSyntaxList<ExpressionSyntax> NormalizeCallArgument(
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
            _ => throw new UnreachableException("Impossible function call argument type")
        };
    }
}
