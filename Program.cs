using System;
using System.IO;

namespace CrlMonitor;

internal static class Program
{
    public static int Main(string[] args)
    {
        return Execute(args);
    }

    private static int Execute(string[] args)
    {
        try
        {
            var configPath = ResolveConfigPath(args);
            var options = ConfigLoader.Load(configPath);
            Run(options);
            return 0;
        }
        catch (FileNotFoundException ex)
        {
            ReportError(ex.Message);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            ReportError(ex.Message);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            ReportError(ex.Message);
            return 1;
        }
    }

    private static string ResolveConfigPath(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            throw new InvalidOperationException("Configuration path argument is required.");
        }

        var candidate = args[0];
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new InvalidOperationException("Configuration path argument must not be empty.");
        }

        return Path.GetFullPath(candidate);
    }

    private static void Run(RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Placeholder runner wiring; future work will invoke the orchestrator.
#pragma warning disable CA1303 // CLI output intentionally literal until reporters are wired.
        Console.WriteLine($"Loaded {options.Crls.Count} CRL definitions.");
#pragma warning restore CA1303
    }

    private static void ReportError(string message)
    {
#pragma warning disable CA1303
        Console.Error.WriteLine($"ERROR: {message}");
#pragma warning restore CA1303
    }

    private static void PrintUsage()
    {
#pragma warning disable CA1303
        Console.WriteLine("Usage: CrlMonitor <path-to-config.json>");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  CrlMonitor config.json");
        Console.WriteLine("  CrlMonitor ./configs/prod.json");
#pragma warning restore CA1303
    }
}
