using CrlMonitor.Crl;
using CrlMonitor.Fetching;
using CrlMonitor.Reporting;
using CrlMonitor.Runner;
using CrlMonitor.Validation;
using CrlMonitor.Health;
using CrlMonitor.Licensing;
using CrlMonitor.Notifications.Alerts;
using CrlMonitor.Notifications.Email;
using CrlMonitor.Notifications.Reports;
using CrlMonitor.State;
using CrlMonitor.Eula;

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
            AnnounceDebugBuild();
            await EulaAcceptanceManager.EnsureAcceptedAsync(cancellationToken).ConfigureAwait(false);
            await LicenseBootstrapper.EnsureLicensedAsync(cancellationToken).ConfigureAwait(false);
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

        var reportingStatus = new ReportingStatus();
        var reporters = BuildReporters(options, stateStore, reportingStatus);
        await reporters.ReportAsync(run, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<CrlConfigEntry> BuildRequests(IReadOnlyList<CrlConfigEntry> entries)
    {
        return entries ?? Array.Empty<CrlConfigEntry>();
    }

    private static CompositeReporter BuildReporters(RunOptions options, IStateStore stateStore, ReportingStatus reportingStatus)
    {
        var reporters = new List<IReporter>();
        if (options.CsvReports)
        {
            var csvPath = CsvReporter.ResolveOutputPath(options.CsvOutputPath, options.CsvAppendTimestamp);
            reporters.Add(new CsvReporter(csvPath, reportingStatus));
        }

        if (options.HtmlReportEnabled && !string.IsNullOrWhiteSpace(options.HtmlReportPath))
        {
            reporters.Add(new HtmlReporter(options.HtmlReportPath, reportingStatus));
        }

        var emailClient = new SmtpEmailClient();

        if (options.Reports != null && options.Reports.Enabled)
        {
            reporters.Add(new EmailReportReporter(options.Reports, emailClient, stateStore, reportingStatus, options.HtmlReportUrl));
        }

        if (options.Alerts != null && options.Alerts.Enabled)
        {
            reporters.Add(new AlertReporter(options.Alerts, emailClient, stateStore, options.HtmlReportUrl));
        }

        if (options.ConsoleReports)
        {
            reporters.Add(new ConsoleReporter(reportingStatus, options.ConsoleVerbose));
        }

        return new CompositeReporter(reporters);
    }

    private static string ResolveConfigPath(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            var defaultPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "config.json"));
            if (File.Exists(defaultPath))
            {
                return defaultPath;
            }

            PrintUsage();
            throw new InvalidOperationException(
                $"Configuration path argument is required (default '{defaultPath}' not found).");
        }

        return Path.GetFullPath(args[0]);
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
        Console.WriteLine();
        Console.WriteLine("If no argument is supplied, the application looks for 'config.json' in the executable directory.");
#pragma warning restore CA1303
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void AnnounceDebugBuild()
    {
        var originalColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("DEBUG VERSION");
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }
}

internal static class FetcherSchemes
{
    public static readonly string[] Http = ["http", "https"];
    public static readonly string[] Ldap = ["ldap", "ldaps"];
    public static readonly string[] File = ["file"];
}
