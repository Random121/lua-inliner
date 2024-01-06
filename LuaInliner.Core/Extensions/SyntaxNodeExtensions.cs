using Loretta.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace LuaInliner.Core.Extensions;

internal static class SyntaxNodeExtensions
{
    /// <summary>
    /// Creates a new node from this node with the specified leading trivia removed.
    /// </summary>
    /// <typeparam name="TRoot">The type of the root node.</typeparam>
    /// <param name="root">The root of the tree of nodes.</param>
    /// <param name="trivia">The trivia to be removed; a descendant of the root node.</param>
    /// <returns></returns>
    public static TRoot RemoveLeadingTrivia<TRoot>(this TRoot root, SyntaxTrivia trivia)
        where TRoot : SyntaxNode
    {
        SyntaxTriviaList oldLeadingTrivias = root.GetLeadingTrivia();
        SyntaxTriviaList newLeadingTrivias = oldLeadingTrivias.Remove(trivia);

        return root.WithLeadingTrivia(newLeadingTrivias);
    }
}
