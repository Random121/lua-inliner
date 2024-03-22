using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace LuaInliner.Core.Inlining;

/// <summary>
/// Edits to perform on the AST to support multiple returns from inline functions.
/// </summary>
/// <param name="ReturnIdentifierNames"></param>
internal record class MultipleReturnsEdits(List<IdentifierNameSyntax> ReturnIdentifierNames);
