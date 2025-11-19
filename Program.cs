using System.Net;
using CrlMonitor.Crl;
using CrlMonitor.Fetching;
using CrlMonitor.Reporting;
using CrlMonitor.Runner;
using CrlMonitor.Validation;
using CrlMonitor.Health;
using CrlMonitor.Licensing;
using CrlMonitor.Logging;
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
            var autoAcceptEula = HasFlag(args, "--accept-eula");
            var configPath = ResolveConfigPath(args);
            LoggingSetup.Initialize(configPath);
            LoggingSetup.LogStartup();

            await EulaAcceptanceManager.EnsureAcceptedAsync(cancellationToken, autoAcceptEula).ConfigureAwait(false);
            await LicenseBootstrapper.EnsureLicensedAsync(cancellationToken).ConfigureAwait(false);

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
        finally
        {
            LoggingSetup.Shutdown();
        }
    }

    private static async Task RunAsync(RunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        HttpClient httpClient;
        if (options.UseSystemProxy)
        {
#pragma warning disable CA2000 // HttpClient disposes handler when disposeHandler: true
            var handler = new HttpClientHandler
#pragma warning restore CA2000
            {
                UseProxy = true,
                Proxy = WebRequest.GetSystemWebProxy(),
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
                CheckCertificateRevocationList = true
            };
            httpClient = new HttpClient(handler, disposeHandler: true);
        }
        else
        {
#pragma warning disable CA2000 // HttpClient disposes handler when disposeHandler: true
            var handler = new HttpClientHandler
#pragma warning restore CA2000
            {
                CheckCertificateRevocationList = true
            };
            httpClient = new HttpClient(handler, disposeHandler: true);
        }

        using (httpClient)
        {
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

    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveConfigPath(string[] args)
    {
        // Filter out flags to find config path
        var nonFlags = args.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToArray();

        if (nonFlags.Length == 0 || string.IsNullOrWhiteSpace(nonFlags[0]))
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

        return Path.GetFullPath(nonFlags[0]);
    }

    private static void ReportError(string message)
    {
#pragma warning disable CA1303 // CLI tool emits English-only error messages; no localization planned
        Console.Error.WriteLine($"ERROR: {message}");
#pragma warning restore CA1303
    }

    private static void PrintUsage()
    {
#pragma warning disable CA1303 // CLI tool emits English-only usage instructions; no localization planned
        Console.WriteLine("Usage: CrlMonitor [--accept-eula] <path-to-config.json>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --accept-eula    Automatically accept EULA (for automated deployments)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  CrlMonitor config.json");
        Console.WriteLine("  CrlMonitor --accept-eula config.json");
        Console.WriteLine("  CrlMonitor ./configs/prod.json");
        Console.WriteLine();
        Console.WriteLine("If no argument is supplied, the application looks for 'config.json' in the executable directory.");
#pragma warning restore CA1303
    }
}

internal static class FetcherSchemes
{
    public static readonly string[] Http = ["http", "https"];
    public static readonly string[] Ldap = ["ldap", "ldaps"];
    public static readonly string[] File = ["file"];
}
