using Loretta.CodeAnalysis.Lua.Syntax;

namespace LuaInliner.Core.Collectors;

/// <summary>
/// Represents a call to an inlinable function.
/// </summary>
/// <param name="CalledFunction"></param>
/// <param name="CallExpressionNode"></param>
internal record class InlineFunctionCall(
    InlineFunction CalledFunction,
    FunctionCallExpressionSyntax CallExpressionNode
);
