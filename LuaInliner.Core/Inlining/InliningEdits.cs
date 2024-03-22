using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace LuaInliner.Core.Inlining;

/// <summary>
/// Edits to perform on the AST to inline a statement.
/// </summary>
internal record class InliningEdits
{
    // NOTE: We need this to be a reference type since we are mutating it
    // after storing it in a temporary variable.

    /// <summary>
    /// Statements to be inserted in front of the current statement.
    /// </summary>
    public List<StatementSyntax> Insertions = [];

    /// <summary>
    /// Whether the calling statement should be removed.<br/>
    /// This is usually used if the calling statement doesn't have a return value.
    /// </summary>
    public bool RemoveCallingStatement;
}
