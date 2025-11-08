using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Crl;
using CrlMonitor.Fetching;
using CrlMonitor.Models;
using CrlMonitor.Reporting;
using CrlMonitor.Runner;
using CrlMonitor.Validation;
using CrlMonitor.Health;
using CrlMonitor.State;

namespace CrlMonitor;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        return await ExecuteAsync(args, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            var configPath = ResolveConfigPath(args);
            var options = ConfigLoader.Load(configPath);
            await RunAsync(options, cancellationToken).ConfigureAwait(false);
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

    private static async Task RunAsync(RunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var httpClient = new HttpClient();
        var httpFetcher = new HttpCrlFetcher(httpClient);
        var ldapFetcher = new LdapCrlFetcher(new SystemLdapConnectionFactory());
        var fileFetcher = new FileCrlFetcher();
        var resolver = new FetcherResolver(new[]
        {
            new FetcherMapping(FetcherSchemes.Http, httpFetcher),
            new FetcherMapping(FetcherSchemes.Ldap, ldapFetcher),
            new FetcherMapping(FetcherSchemes.File, fileFetcher)
        });
        using var stateStore = new FileStateStore(options.StateFilePath);
        var runner = new CrlCheckRunner(
            resolver,
            new CrlParser(SignatureValidationMode.CaCertificate),
            new CrlSignatureValidator(),
            new CrlHealthEvaluator(),
            stateStore);
        var requests = BuildRequests(options.Crls);
        var run = await runner.RunAsync(
            requests,
            options.FetchTimeout,
            options.MaxParallelFetches,
            cancellationToken).ConfigureAwait(false);

        var reporters = BuildReporters(options);
        await reporters.ReportAsync(run, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<CrlConfigEntry> BuildRequests(IReadOnlyList<CrlConfigEntry> entries)
    {
        return entries ?? Array.Empty<CrlConfigEntry>();
    }

    private static CompositeReporter BuildReporters(RunOptions options)
    {
        var reporters = new List<IReporter>();
        if (options.ConsoleReports)
        {
            reporters.Add(new ConsoleReporter());
        }

        if (options.CsvReports)
        {
            reporters.Add(new CsvReporter(options.CsvOutputPath, options.CsvAppendTimestamp));
        }

        return new CompositeReporter(reporters);
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

internal static class FetcherSchemes
{
    public static readonly string[] Http = { "http", "https" };
    public static readonly string[] Ldap = { "ldap", "ldaps" };
    public static readonly string[] File = { "file" };
}
