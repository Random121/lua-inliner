using Loretta.CodeAnalysis.Lua;

namespace LuaInliner.Core.Naming;

/// <summary>
/// Generator for unique identifier names.
/// </summary>
/// <param name="scope"></param>
internal sealed class UniqueNameGenerator(IScope scope)
{
    /// <summary>
    /// Number added to variable names to make them unique.<br/>
    /// Generated sequentially; each new variable name would have an
    /// id that is incremented by one from the last.
    /// </summary>
    private static uint _uniqueNameId;

    /// <summary>
    /// Already used variables names within the scope of the name generator.
    /// </summary>
    private readonly HashSet<string> _takenVariableNames = GetUsedVariableNames(scope);

    /// <summary>
    /// Generates a variable name that is unique with the scope specified.
    /// <br/><br/>
    /// Variable name is in the format of <example><c>[PREFIX]__[NAME_ID]</c></example>.
    /// </summary>
    /// <param name="prefix"></param>
    /// <returns>Unique variable name</returns>
    /// <exception cref="Exception"> Unable to generate unique variable name with the specific prefix</exception>
    public string GetUniqueName(string prefix)
    {
        while (_uniqueNameId < uint.MaxValue)
        {
            string name = $"{prefix}__{_uniqueNameId}";

            _uniqueNameId++;

            if (ValidUniqueName(name))
            {
                return name;
            }
        }

        throw new Exception($"Too many variables with name that starts with '{prefix}'");
    }

    /// <summary>
    /// Returns whether the name is valid: it should not be a Lua keyword and
    /// is not taken in the current scope.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    private bool ValidUniqueName(string name)
    {
        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.IdentifierToken
            && !_takenVariableNames.Contains(name);
    }

    /// <summary>
    /// Gets all the used variable names within the scope.
    /// </summary>
    /// <param name="scope"></param>
    /// <returns></returns>
    private static HashSet<string> GetUsedVariableNames(IScope scope)
    {
        HashSet<string> usedNames = [];

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
