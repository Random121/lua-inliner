using Loretta.CodeAnalysis.Lua;

namespace LuaInliner.Core.Naming;

internal class UniqueNameGenerator
{
    /// <summary>
    /// Number added to variable names to make them unique.<br/>
    /// Generated sequentially; each new variable name would have an
    /// id that is incremented by one from the last.
    /// </summary>
    private uint _nameId;

    /// <summary>
    /// Generates a variable name that is unique with the scope specified.
    /// <br/><br/>
    /// Variable name is in the format of <example><c>[PREFIX]__[NAME_ID]</c></example>。
    /// </summary>
    /// <param name="scope"></param>
    /// <param name="prefix"></param>
    /// <returns>Unique variable name</returns>
    /// <exception cref="Exception"> Unable to generate unique variable name with the specific prefix</exception>
    public string GetUniqueName(IScope scope, string prefix)
    {
        HashSet<string> usedVariableNames = GetUsedVariableNames(scope);

        while (_nameId < uint.MaxValue)
        {
            string name = $"{prefix}__{_nameId}";

            _nameId++;

            // Accept if its not a Lua keyword and not taken in the scope
            if (
                SyntaxFacts.GetKeywordKind(name) == SyntaxKind.IdentifierToken
                && !usedVariableNames.Contains(name)
            )
            {
                return name;
            }
        }

        throw new Exception($"Too many variables with name that starts with '{prefix}'");
    }

    /// <summary>
    /// Gets all the used variable names within the scope.
    /// </summary>
    /// <param name="scope"></param>
    /// <returns></returns>
    public static HashSet<string> GetUsedVariableNames(IScope scope)
    {
        HashSet<string> usedNames = [];

        // Need this extra variable since we want it to be nullable
        IScope? currentScope = scope;

        while (currentScope is not null)
        {
            foreach (IVariable variable in currentScope.DeclaredVariables)
            {
                usedNames.Add(variable.Name);
            }

            currentScope = currentScope.ContainingScope;
        }

        return usedNames;
    }
}
