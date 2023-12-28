using Loretta.CodeAnalysis.Text;

namespace LuaInliner.CLI;

internal static class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("No input file provided.");
        }

        string filePath = args[0];

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine("Specific input file does not exist.");
        }

        SourceText fileSourceText = GetFileSourceText(filePath);
    }

    static SourceText GetFileSourceText(string filePath)
    {
        SourceText fileSourceText;

        using (FileStream stream = File.OpenRead(filePath))
        {
            fileSourceText = SourceText.From(stream);
        }

        return fileSourceText;
    }
}
