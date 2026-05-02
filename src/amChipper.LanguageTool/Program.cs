using amChipper.App.Services;

namespace amChipper.LanguageTool;

/// <summary>
/// Minimal release tool for creating and validating amChipper language packs.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Runs the language-pack command line utility.
    /// </summary>
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string command = args.FirstOrDefault() ?? "help";
        string output = Value(args, "--output") ?? Value(args, "--input") ?? Path.Combine(AppContext.BaseDirectory, "lang");
        string? language = Value(args, "--language");

        try
        {
            return command.ToLowerInvariant() switch
            {
                "export" or "lang-export" => Export(output, language),
                "check" or "lang-check" => Check(output),
                _ => Help()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"amChipper.LanguageTool failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Export(string output, string? language)
    {
        AppHelpContent.ExportLanguageFiles(output, language, overwrite: true);
        Console.WriteLine($"Exported language packs to {output}");
        return 0;
    }

    private static int Check(string input)
    {
        var lines = AppHelpContent.ValidateLanguageFiles(input);
        foreach (string line in lines)
            Console.WriteLine(line);
        return lines.Any(line => line.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase)) ? 2 : 0;
    }

    private static int Help()
    {
        Console.WriteLine("amChipper.LanguageTool");
        Console.WriteLine("  lang-export --output <folder> [--language <name>]");
        Console.WriteLine("  lang-check  --input <folder>");
        Console.WriteLine("  export      --output <folder> [--language <name>]");
        Console.WriteLine("  check       --input <folder>");
        return 0;
    }

    private static string? Value(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }
}
