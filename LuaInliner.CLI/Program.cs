using System.Diagnostics;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Text;
using LuaInliner.Common;
using LuaInliner.Core;

namespace LuaInliner.CLI;

using DiagnosticList = IReadOnlyList<Diagnostic>;

internal static class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Expected arguments: <input file> <output file>");
            return;
        }

        string inputFilePath = args[0];
        string outputFilePath = args[1];

        if (!File.Exists(inputFilePath))
        {
            Console.Error.WriteLine("Specified input file does not exist.");
            return;
        }

        long inlineStartTime = Stopwatch.GetTimestamp();

        SourceText fileSourceText = GetFileSourceText(inputFilePath);

        LuaParseOptions luaParseOptions = new(LuaSyntaxOptions.Luau);
        Inliner inliner = new(luaParseOptions);

        Result<SyntaxNode, DiagnosticList> inlineResult = inliner.InlineFile(fileSourceText);

        if (inlineResult.IsErr)
        {
            DiagnosticList diagnostics = inlineResult.Err.Value;

            Console.Error.WriteLine("Found errors while inlining:");

            foreach (Diagnostic diagnostic in diagnostics)
            {
                Console.Error.WriteLine(diagnostic);
            }

            return;
        }

        TimeSpan inlineTimeTaken = Stopwatch.GetElapsedTime(inlineStartTime);

        Console.WriteLine($"Inlining completed in {inlineTimeTaken.TotalSeconds} seconds.");

        SyntaxNode rewrittenRoot = inlineResult.Ok.Value;

        using (FileStream stream = File.Open(outputFilePath, FileMode.Create, FileAccess.Write))
        using (StreamWriter writer = new(stream))
        {
            rewrittenRoot.WriteTo(writer);
        }

        Console.WriteLine("Wrote inlining result to file.");
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
