using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Serilog;
using Serilog.Events;
using Standard.Licensing;

namespace CrlMonitor.Logging;

/// <summary>
/// Configures Serilog for application logging.
/// </summary>
internal static class LoggingSetup
{
    /// <summary>
    /// Initialises the global Serilog logger using configuration from the provided file path.
    /// </summary>
    /// <param name="configPath">Path to the configuration JSON file.</param>
    public static void Initialize(string configPath)
    {
        var loggingConfig = LoadLoggingConfig(configPath);
        var logFilePath = ResolveLogFilePath(loggingConfig.LogFilePath);
        var logLevel = ParseLogLevel(loggingConfig.MinLevel);
        var rollingInterval = ParseRollingInterval(loggingConfig.RollingInterval);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.File(
                path: logFilePath,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                rollingInterval: rollingInterval,
                retainedFileCountLimit: loggingConfig.RetainedFileCountLimit,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    /// <summary>
    /// Logs startup information including version, OS, runtime, and directories.
    /// </summary>
    public static void LogStartup()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "unknown";
        var buildConfig = GetBuildConfiguration();
        var osDescription = RuntimeInformation.OSDescription;
        var runtimeVersion = Environment.Version.ToString();
        var currentDir = Directory.GetCurrentDirectory();
        var appDir = AppContext.BaseDirectory;

        Log.Information("CRL Monitor starting");
        Log.Information("Version: {Version}", version);
        Log.Information("OS: {OS}", osDescription);
        Log.Information(".NET Runtime: {Runtime}", runtimeVersion);
        Log.Information("Build configuration: {BuildConfig}", buildConfig);
        Log.Information("Current working directory: {CurrentDir}", currentDir);
        Log.Information("Application directory: {AppDir}", appDir);
    }

    /// <summary>
    /// Logs license validation information.
    /// </summary>
    /// <param name="licensePath">Path to the license file.</param>
    /// <param name="fileSize">Size of the license file in bytes.</param>
    /// <param name="license">The validated license object, or null if validation failed.</param>
    /// <param name="isValid">Whether the license validation succeeded.</param>
    public static void LogLicenseInfo(string licensePath, long fileSize, License? license, bool isValid)
    {
        Log.Information("License file found at {LicensePath} ({FileSize} bytes)", licensePath, fileSize);
        Log.Information("License validation: {Status}", isValid ? "VALID" : "INVALID");

        if (license != null)
        {
            Log.Information("License type: {LicenseType}", license.Type.ToString());
            Log.Information("License expires: {ExpiryDate}", license.Expiration);

            var daysUntilExpiry = (license.Expiration - DateTime.Now).Days;
            Log.Information("Days until expiration: {Days}", daysUntilExpiry);
        }
    }

    /// <summary>
    /// Logs trial status information.
    /// </summary>
    /// <param name="daysRemaining">Number of days remaining in the trial period.</param>
    public static void LogTrialStatus(int daysRemaining)
    {
        Log.Information("Trial period: VALID ({Days} days remaining)", daysRemaining);
    }

    /// <summary>
    /// Flushes and closes the logger on application shutdown.
    /// </summary>
    public static void Shutdown()
    {
        Log.CloseAndFlush();
    }

    private static LoggingConfig LoadLoggingConfig(string configPath)
    {
        var json = File.ReadAllText(configPath);
        var doc = JsonDocument.Parse(json);
        var loggingElement = doc.RootElement.GetProperty("logging");

        return new LoggingConfig {
            MinLevel = loggingElement.GetProperty("min_level").GetString() ?? "Information",
            LogFilePath = loggingElement.GetProperty("log_file_path").GetString() ?? "CrlMonitor.log",
            RollingInterval = loggingElement.GetProperty("rolling_interval").GetString() ?? "Day",
            RetainedFileCountLimit = loggingElement.GetProperty("retained_file_count_limit").GetInt32()
        };
    }

    private static string ResolveLogFilePath(string configuredPath)
    {
        // If absolute path, use as-is
        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        // Use same directory as EULA acceptance file
        var eulaDir = GetEulaAcceptanceDirectory();
        return Path.Combine(eulaDir, configuredPath);
    }

    private static string GetEulaAcceptanceDirectory()
    {
        // Match logic from EulaAcceptanceManager
        if (OperatingSystem.IsWindows())
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "RedKestrel",
                "CrlMonitor");

            if (TryEnsureDirectoryWritable(directory))
            {
                return directory;
            }
        }

        // Fallback to executable directory
        return GetExecutableDirectory();
    }

    private static bool TryEnsureDirectoryWritable(string directory)
    {
#pragma warning disable CA1031 // Defensive: test if directory writable
        try
        {
            _ = Directory.CreateDirectory(directory);
            return true;
        }
        catch
        {
            return false;
        }
#pragma warning restore CA1031
    }

    private static string GetExecutableDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            return Path.GetFullPath(baseDirectory);
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var processDirectory = Path.GetDirectoryName(Path.GetFullPath(processPath));
            if (!string.IsNullOrWhiteSpace(processDirectory))
            {
                return processDirectory!;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static LogEventLevel ParseLogLevel(string level)
    {
        return level.ToUpperInvariant() switch {
            "VERBOSE" => LogEventLevel.Verbose,
            "DEBUG" => LogEventLevel.Debug,
            "INFORMATION" => LogEventLevel.Information,
            "WARNING" => LogEventLevel.Warning,
            "ERROR" => LogEventLevel.Error,
            "FATAL" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }

    private static RollingInterval ParseRollingInterval(string interval)
    {
        return interval.ToUpperInvariant() switch {
            "INFINITE" => RollingInterval.Infinite,
            "YEAR" => RollingInterval.Year,
            "MONTH" => RollingInterval.Month,
            "DAY" => RollingInterval.Day,
            "HOUR" => RollingInterval.Hour,
            "MINUTE" => RollingInterval.Minute,
            _ => RollingInterval.Day
        };
    }

    private static string GetBuildConfiguration()
    {
#if DEBUG
        return "DEBUG";
#else
        return "RELEASE";
#endif
    }

    private sealed class LoggingConfig
    {
        public string MinLevel { get; init; } = "Information";
        public string LogFilePath { get; init; } = "CrlMonitor.log";
        public string RollingInterval { get; init; } = "Day";
        public int RetainedFileCountLimit { get; init; } = 7;
    }
}
