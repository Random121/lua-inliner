using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;
using LuaInliner.Core.Collectors;
using LuaInliner.Core.Extensions;
using LuaInliner.Core.Generators;

namespace LuaInliner.Core.Rewriters;

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

internal sealed class InlineRewriter : LuaSyntaxRewriter
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
    private readonly Dictionary<StatementSyntax, InlineTask> _inlineTaskLookup = new();

    private InlineRewriter(Script script, ImmutableArray<InlineFunctionCallInfo> functionCallInfos)
    {
        _script = script;
        _callInfoLookup = functionCallInfos.ToImmutableDictionary(info => info.CallExpression);
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

            if (_inlineTaskLookup.TryGetValue(statement, out InlineTask? inlineTask))
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

        SeparatedSyntaxList<ExpressionSyntax> neededArguments = GetNeededCallArguments(
            arguments,
            calledFunction.Parameters.Count
        );

        StatementSyntax parentStatement = GetParentStatementOfExpression(node);
        IScope currentScope = _script.GetScope(node)!;

        List<string> returnVariableNames = new(calledFunction.MaxReturnCount);

        for (int i = 0; i < calledFunction.MaxReturnCount; i++)
        {
            // TODO: Make this more performant since it is retrieving the
            // used variable names every iteration
            string name = _nameGenerator.GetUniqueName(currentScope, "__inline_return");

            returnVariableNames.Add(name);
        }

        InlineTask task = _inlineTaskLookup.GetOrCreate(parentStatement);

        

        // TODO: implement

        return default;
    }

    private static SeparatedSyntaxList<ExpressionSyntax> GetNeededCallArguments(
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
            var nilExpressions = Enumerable.Repeat(
                SyntaxFactory.LiteralExpression(SyntaxKind.NilLiteralExpression),
                numberOfNilsNeeded
            );

            return SyntaxFactory.SeparatedList(arguments.AddRange(nilExpressions));
        }

        // Amount of arguments is smaller or equal to number of expected arguments
        return SyntaxFactory.SeparatedList(arguments.Take(parameterCount));
    }

    private static StatementSyntax GetParentStatementOfExpression(ExpressionSyntax node)
    {
        // We don't know the exact type of the parent statement, but we do
        // know it is always contained within a statement list.
        static bool isParentStatement(StatementSyntax node) =>
            node.Parent.IsKind(SyntaxKind.StatementList);

        StatementSyntax? parentStatement = node.FirstAncestorOrSelf(
            (Func<StatementSyntax, bool>)isParentStatement
        );

        // This should never be null as an ExpressionSyntax should
        // always be contained in a statement which then must also be in a statement list
        Debug.Assert(parentStatement is not null);

        return parentStatement;
    }

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
