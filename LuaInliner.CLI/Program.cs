using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Text;
using LuaInliner.Common;
using LuaInliner.Core;
using System.Collections.Immutable;

namespace LuaInliner.CLI;

internal static class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("No input file provided.");
            return;
        }

        string filePath = args[0];

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine("Specified input file does not exist.");
            return;
        }

        SourceText fileSourceText = GetFileSourceText(filePath);

        LuaParseOptions luaParseOptions = new(LuaSyntaxOptions.Luau);
        Inliner inliner = new(luaParseOptions);

        Result<SyntaxNode, ImmutableArray<Diagnostic>> inlineResult = inliner.InlineFile(fileSourceText);

        if (inlineResult.IsErr)
        {
            ImmutableArray<Diagnostic> diagnostics = inlineResult.Err.Value;

            Console.Error.WriteLine("Found errors while inlining:");

            foreach (Diagnostic diagnostic in diagnostics)
            {
                Console.Error.WriteLine(diagnostic);
            }

            return;
        }

        Console.WriteLine("Inlining was successful. Writing to file...");

        SyntaxNode rewrittenRoot = inlineResult.Ok.Value;

        using (FileStream stream = File.Open("output.lua", FileMode.Create, FileAccess.Write))
        using (StreamWriter writer = new(stream))
        {
            rewrittenRoot.WriteTo(writer);
        }

        Console.WriteLine("Finished writing inlining result to file.");
    }

    private static SourceText GetFileSourceText(string filePath)
    {
        SourceText fileSourceText;

        using (FileStream stream = File.OpenRead(filePath))
        {
            fileSourceText = SourceText.From(stream);
        }

        return fileSourceText;
    }
}
