using System.Text.Json;
using System.Text.Json.Serialization;
using CrlMonitor.Crl;
using CrlMonitor.Models;
using CrlMonitor.Notifications;

namespace CrlMonitor;

internal static class ConfigLoader
{
    private const double DefaultExpiryThreshold = 0.8;
    private const double MinExpiryThreshold = 0.1;
    private const double MaxExpiryThreshold = 1.0;
    private const long DefaultMaxCrlSizeBytes = 10 * 1024 * 1024;
    private const long MinMaxCrlSizeBytes = 1;
    private const long MaxMaxCrlSizeBytes = 100 * 1024 * 1024;
    private const string DefaultReportSubject = "CRL Health Report";
    private const string DefaultAlertPrefix = "[CRL Alert]";
    private const int MinFetchTimeoutSeconds = 1;
    private const int MaxFetchTimeoutSeconds = 600;
    private const int MinParallelFetches = 1;
    private const int MaxParallelFetches = 64;
    private const double MinAlertCooldownHours = 0;
    private const double MaxAlertCooldownHours = 168;
    private static readonly HashSet<string> SupportedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http",
        "https",
        "ldap",
        "ldaps",
        "file"
    };
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static RunOptions Load(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var absolutePath = Path.GetFullPath(configPath);
        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException("Configuration file not found.", absolutePath);
        }

        var configDirectory = Path.GetDirectoryName(absolutePath) ?? AppContext.BaseDirectory;
        using var stream = File.OpenRead(absolutePath);
        var document =
            JsonSerializer.Deserialize<ConfigDocument>(stream, SerializerOptions) ??
            throw new InvalidOperationException("Configuration file is empty.");

        var csvPath = RequirePath(document.CsvOutputPath, nameof(document.CsvOutputPath));
        var stateFilePath = RequirePath(document.StateFilePath, nameof(document.StateFilePath));
        var timeoutSeconds = document.FetchTimeoutSeconds ?? throw new InvalidOperationException("fetch_timeout_seconds is required.");
        var maxParallel = document.MaxParallelFetches ?? throw new InvalidOperationException("max_parallel_fetches is required.");
        if (timeoutSeconds is < MinFetchTimeoutSeconds or > MaxFetchTimeoutSeconds)
        {
            throw new InvalidOperationException($"fetch_timeout_seconds must be between {MinFetchTimeoutSeconds} and {MaxFetchTimeoutSeconds} seconds.");
        }

        if (maxParallel is < MinParallelFetches or > MaxParallelFetches)
        {
            throw new InvalidOperationException($"max_parallel_fetches must be between {MinParallelFetches} and {MaxParallelFetches}.");
        }

        var maxCrlSizeBytes = ResolveMaxCrlSize(document.MaxCrlSizeBytes, DefaultMaxCrlSizeBytes, "max_crl_size_bytes");
        var entries = BuildEntries(document.Uris, configDirectory, maxCrlSizeBytes);

        // Only parse SMTP if reports or alerts are enabled
        var smtpNeeded = (document.Reports?.Enabled == true) || (document.Alerts?.Enabled == true);
        var smtpOptions = smtpNeeded && document.Smtp != null ? ParseSmtp(document.Smtp, "smtp") : null;

        var reportOptions = ParseReportOptions(document.Reports, smtpOptions);
        var alertOptions = ParseAlertOptions(document.Alerts, smtpOptions);
        var htmlEnabled = document.HtmlReportEnabled ?? false;
        var htmlPath = ResolveOptionalPath(configDirectory, document.HtmlReportPath);
        var consoleVerbose = document.ConsoleVerbose ?? false;
        return htmlEnabled && string.IsNullOrWhiteSpace(htmlPath)
            ? throw new InvalidOperationException("html_report_path is required when html_report_enabled is true.")
            : new RunOptions(
            document.ConsoleReports ?? true,
            consoleVerbose,
            document.CsvReports ?? true,
            ResolvePath(configDirectory, csvPath),
            document.CsvAppendTimestamp ?? false,
            htmlEnabled,
            htmlPath,
            document.HtmlReportUrl,
            maxCrlSizeBytes,
            TimeSpan.FromSeconds(timeoutSeconds),
            maxParallel,
            ResolvePath(configDirectory, stateFilePath),
            entries,
            reportOptions,
            alertOptions);
    }


    private static List<CrlConfigEntry> BuildEntries(
        IReadOnlyList<CrlDocument>? documents,
        string baseDirectory,
        long defaultMaxCrlSizeBytes)
    {
        if (documents == null || documents.Count == 0)
        {
            throw new InvalidOperationException("At least one CRL entry is required.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<CrlConfigEntry>(documents.Count);
        foreach (var document in documents)
        {
            if (string.IsNullOrWhiteSpace(document.Uri))
            {
                throw new InvalidOperationException("Each CRL entry must specify a uri.");
            }

            if (!Uri.TryCreate(document.Uri, UriKind.Absolute, out var uri))
            {
                uri = TryCreateFileUri(document.Uri, baseDirectory) ?? throw new InvalidOperationException($"Invalid uri '{document.Uri}'.");
            }

            EnsureSupportedScheme(uri);
            if (!seen.Add(uri.ToString()))
            {
                throw new InvalidOperationException($"Found duplicate uri '{uri}'.");
            }

            var signatureMode = ParseSignatureMode(document.SignatureValidationMode);
            var caPath = ResolveCaPath(signatureMode, document.CaCertificatePath, baseDirectory, uri);
            var threshold = ParseExpiryThreshold(document.ExpiryThreshold, uri);
            var ldap = ParseLdap(document.Ldap, uri);
            var maxCrlSizeBytes = ResolveMaxCrlSize(
                document.MaxCrlSizeBytes,
                defaultMaxCrlSizeBytes,
                $"max_crl_size_bytes for {uri}");

            if (ldap != null && !IsLdapScheme(uri))
            {
                throw new InvalidOperationException($"LDAP credentials can only be specified for ldap/ldaps URIs. Offending URI: {uri}");
            }

            if (ldap == null && IsLdapScheme(uri) && document.Ldap != null)
            {
                throw new InvalidOperationException($"LDAP block for {uri} must specify both username and password.");
            }

            entries.Add(new CrlConfigEntry(uri, signatureMode, caPath, threshold, ldap, maxCrlSizeBytes));
        }

        return entries;
    }

    private static Uri? TryCreateFileUri(string value, string baseDirectory)
    {
        if (!value.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relativePart = value["file://".Length..];
        if (relativePart.StartsWith("./", StringComparison.Ordinal) || relativePart.StartsWith(".\\", StringComparison.Ordinal))
        {
            relativePart = relativePart[2..];
        }

        var combined = Path.Combine(baseDirectory, relativePart.TrimStart('/', '\\'));
        var fullPath = Path.GetFullPath(combined);
        return new Uri(fullPath);
    }

    private static SignatureValidationMode ParseSignatureMode(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? SignatureValidationMode.None
            : value.Equals("ca-cert", StringComparison.OrdinalIgnoreCase)
            ? SignatureValidationMode.CaCertificate
            : throw new InvalidOperationException("signature_validation_mode must be 'none' or 'ca-cert'.");
    }

    private static string? ResolveCaPath(
        SignatureValidationMode mode,
        string? caPath,
        string baseDirectory,
        Uri uri)
    {
        if (mode == SignatureValidationMode.None)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(caPath))
        {
            throw new InvalidOperationException($"ca_certificate_path is required when signature_validation_mode is ca-cert. Offending URI: {uri}");
        }

        var resolved = ResolvePath(baseDirectory, caPath);
        return !File.Exists(resolved)
            ? throw new InvalidOperationException($"ca_certificate_path '{caPath}' not found for URI {uri}.")
            : resolved;
    }

    private static double ParseExpiryThreshold(double? value, Uri uri)
    {
        var threshold = value ?? DefaultExpiryThreshold;
        return threshold is < MinExpiryThreshold or > MaxExpiryThreshold
            ? throw new InvalidOperationException($"expiry_threshold for {uri} must be between {MinExpiryThreshold} and {MaxExpiryThreshold}.")
            : threshold;
    }

    private static LdapCredentials? ParseLdap(LdapDocument? document, Uri uri)
    {
        return document == null
            ? null
            : string.IsNullOrWhiteSpace(document.Username) || string.IsNullOrWhiteSpace(document.Password)
            ? throw new InvalidOperationException($"ldap.username and ldap.password must be supplied for {uri}.")
            : new LdapCredentials(document.Username, document.Password);
    }

    private static void EnsureSupportedScheme(Uri uri)
    {
        if (!SupportedSchemes.Contains(uri.Scheme))
        {
            throw new InvalidOperationException($"URI '{uri}' uses unsupported scheme '{uri.Scheme}'. Allowed schemes: http, https, ldap, ldaps, file.");
        }
    }

    private static bool IsLdapScheme(Uri uri)
    {
        return uri.Scheme.Equals("ldap", StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePath(string baseDirectory, string path)
    {
        // Expand environment variables (e.g., %ProgramData%, $HOME)
        var expandedPath = Environment.ExpandEnvironmentVariables(path);
        return Path.IsPathRooted(expandedPath) ? expandedPath : Path.GetFullPath(Path.Combine(baseDirectory, expandedPath));
    }

    private static string? ResolveOptionalPath(string baseDirectory, string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : ResolvePath(baseDirectory, path);
    }

    private static string RequirePath(string? value, string name)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{name} is required.") : value;
    }

    private static ReportOptions? ParseReportOptions(ReportsDocument? document, SmtpOptions? smtp)
    {
        if (document == null || document.Enabled != true)
        {
            return null;
        }

        if (smtp == null)
        {
            throw new InvalidOperationException("smtp block is required when reports are enabled.");
        }

        var recipients = ParseRecipients(document.Recipients, "reports.recipients");
        var subject = string.IsNullOrWhiteSpace(document.Subject)
            ? DefaultReportSubject
            : document.Subject;
        var includeSummary = document.IncludeSummary ?? true;
        var includeFullCsv = document.IncludeFullCsv ?? true;
        var frequencyHours = document.ReportFrequencyHours;
        if (frequencyHours.HasValue)
        {
            if (frequencyHours.Value <= 0)
            {
                throw new InvalidOperationException("report_frequency_hours must be > 0.");
            }

            if (frequencyHours.Value > 8760)
            {
                throw new InvalidOperationException("report_frequency_hours must be <= 8760 (1 year).");
            }
        }

        return new ReportOptions(
            true,
            recipients,
            subject,
            includeSummary,
            includeFullCsv,
            frequencyHours,
            smtp);
    }

    private static AlertOptions? ParseAlertOptions(AlertsDocument? document, SmtpOptions? smtp)
    {
        if (document == null || document.Enabled != true)
        {
            return null;
        }

        var recipients = ParseRecipients(document.Recipients, "alerts.recipients");
        var statuses = ParseAlertStatuses(document.Statuses);

        var cooldownHours = document.CooldownHours ?? 6;
        if (cooldownHours is < MinAlertCooldownHours or > MaxAlertCooldownHours)
        {
            throw new InvalidOperationException($"alerts.cooldown_hours must be between {MinAlertCooldownHours} and {MaxAlertCooldownHours}.");
        }

        var subjectPrefix = string.IsNullOrWhiteSpace(document.SubjectPrefix)
            ? DefaultAlertPrefix
            : document.SubjectPrefix;
        var includeDetails = document.IncludeDetails ?? true;
        return smtp == null
            ? throw new InvalidOperationException("smtp block is required when alerts are enabled.")
            : new AlertOptions(
            true,
            recipients,
            statuses,
            TimeSpan.FromHours(cooldownHours),
            subjectPrefix,
            includeDetails,
            smtp);
    }

    private static List<CrlStatus> ParseAlertStatuses(List<string>? statuses)
    {
        var collected = new List<CrlStatus>();
        if (statuses != null)
        {
            foreach (var status in statuses)
            {
                if (string.IsNullOrWhiteSpace(status))
                {
                    continue;
                }

                collected.Add(ParseCrlStatus(status));
            }
        }

        return collected.Count == 0
            ? throw new InvalidOperationException("alerts.statuses must contain at least one status when alerts are enabled.")
            : collected;
    }

    private static CrlStatus ParseCrlStatus(string value)
    {
        return value.Trim().ToUpperInvariant() switch {
            "OK" => CrlStatus.Ok,
            "WARNING" => CrlStatus.Warning,
            "EXPIRING" => CrlStatus.Expiring,
            "EXPIRED" => CrlStatus.Expired,
            "ERROR" => CrlStatus.Error,
            _ => throw new InvalidOperationException($"alerts.statuses entry '{value}' is not supported. Allowed values: OK, WARNING, EXPIRING, EXPIRED, ERROR.")
        };
    }

    private static string ResolveSmtpPassword(string? passwordFromConfig)
    {
        if (!string.IsNullOrWhiteSpace(passwordFromConfig))
        {
            return passwordFromConfig;
        }

        var envPassword = Environment.GetEnvironmentVariable("SMTP_PASSWORD");
        return string.IsNullOrWhiteSpace(envPassword)
            ? throw new InvalidOperationException("SMTP password must be provided via config or SMTP_PASSWORD environment variable.")
            : envPassword;
    }

    private static List<string> ParseRecipients(List<string>? recipients, string propertyName)
    {
        if (recipients == null || recipients.Count == 0)
        {
            throw new InvalidOperationException($"{propertyName} must contain at least one recipient.");
        }

        var list = new List<string>(recipients.Count);
        foreach (var recipient in recipients)
        {
            if (string.IsNullOrWhiteSpace(recipient))
            {
                continue;
            }

            list.Add(recipient.Trim());
        }

        return list.Count == 0 ? throw new InvalidOperationException($"{propertyName} must contain at least one recipient.") : list;
    }

    private static SmtpOptions ParseSmtp(SmtpDocument? document, string propertyName)
    {
        if (document == null)
        {
            throw new InvalidOperationException($"{propertyName} is required.");
        }

        if (string.IsNullOrWhiteSpace(document.Host))
        {
            throw new InvalidOperationException($"{propertyName}.host is required.");
        }

        if (document.Port is null or <= 0 or > 65535)
        {
            throw new InvalidOperationException($"{propertyName}.port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(document.Username))
        {
            throw new InvalidOperationException($"{propertyName}.username is required.");
        }

        var password = ResolveSmtpPassword(document.Password);

        if (string.IsNullOrWhiteSpace(document.From))
        {
            throw new InvalidOperationException($"{propertyName}.from is required.");
        }

        var enableStartTls = document.EnableStartTls ?? true;

        return new SmtpOptions(
            document.Host.Trim(),
            document.Port.Value,
            document.Username,
            password,
            document.From,
            enableStartTls);
    }

    private sealed record ConfigDocument
    {
        [JsonPropertyName("console_reports")]
        public bool? ConsoleReports { get; init; }

        [JsonPropertyName("console_verbose")]
        public bool? ConsoleVerbose { get; init; }

        [JsonPropertyName("csv_reports")]
        public bool? CsvReports { get; init; }

        [JsonPropertyName("csv_output_path")]
        public string? CsvOutputPath { get; init; }

        [JsonPropertyName("csv_append_timestamp")]
        public bool? CsvAppendTimestamp { get; init; }

        [JsonPropertyName("html_report_enabled")]
        public bool? HtmlReportEnabled { get; init; }

        [JsonPropertyName("html_report_path")]
        public string? HtmlReportPath { get; init; }

        [JsonPropertyName("fetch_timeout_seconds")]
        public int? FetchTimeoutSeconds { get; init; }

        [JsonPropertyName("max_parallel_fetches")]
        public int? MaxParallelFetches { get; init; }

        [JsonPropertyName("state_file_path")]
        public string? StateFilePath { get; init; }

        [JsonPropertyName("smtp")]
        public SmtpDocument? Smtp { get; init; }

        [JsonPropertyName("html_report_url")]
        public string? HtmlReportUrl { get; init; }

        [JsonPropertyName("max_crl_size_bytes")]
        public long? MaxCrlSizeBytes { get; init; }

        [JsonPropertyName("reports")]
        public ReportsDocument? Reports { get; init; }

        [JsonPropertyName("alerts")]
        public AlertsDocument? Alerts { get; init; }

        [JsonPropertyName("uris")]
        public List<CrlDocument>? Uris { get; init; }
    }

    private sealed record CrlDocument
    {
        [JsonPropertyName("uri")]
        public string? Uri { get; init; }

        [JsonPropertyName("signature_validation_mode")]
        public string? SignatureValidationMode { get; init; }

        [JsonPropertyName("ca_certificate_path")]
        public string? CaCertificatePath { get; init; }

        [JsonPropertyName("expiry_threshold")]
        public double? ExpiryThreshold { get; init; }

        [JsonPropertyName("ldap")]
        public LdapDocument? Ldap { get; init; }

        [JsonPropertyName("max_crl_size_bytes")]
        public long? MaxCrlSizeBytes { get; init; }
    }

    private sealed record LdapDocument
    {
        [JsonPropertyName("username")]
        public string? Username { get; init; }

        [JsonPropertyName("password")]
        public string? Password { get; init; }
    }

    private sealed record ReportsDocument
    {
        [JsonPropertyName("enabled")]
        public bool? Enabled { get; init; }

        [JsonPropertyName("recipients")]
        public List<string>? Recipients { get; init; }

        [JsonPropertyName("subject")]
        public string? Subject { get; init; }

        [JsonPropertyName("include_summary")]
        public bool? IncludeSummary { get; init; }

        [JsonPropertyName("include_full_csv")]
        public bool? IncludeFullCsv { get; init; }

        [JsonPropertyName("report_frequency_hours")]
        public double? ReportFrequencyHours { get; init; }
    }

    private sealed record AlertsDocument
    {
        [JsonPropertyName("enabled")]
        public bool? Enabled { get; init; }

        [JsonPropertyName("recipients")]
        public List<string>? Recipients { get; init; }

        [JsonPropertyName("statuses")]
        public List<string>? Statuses { get; init; }

        [JsonPropertyName("cooldown_hours")]
        public double? CooldownHours { get; init; }

        [JsonPropertyName("subject_prefix")]
        public string? SubjectPrefix { get; init; }

        [JsonPropertyName("include_details")]
        public bool? IncludeDetails { get; init; }
    }


    private sealed record SmtpDocument
    {
        [JsonPropertyName("host")]
        public string? Host { get; init; }

        [JsonPropertyName("port")]
        public int? Port { get; init; }

        [JsonPropertyName("username")]
        public string? Username { get; init; }

        [JsonPropertyName("password")]
        public string? Password { get; init; }

        [JsonPropertyName("from")]
        public string? From { get; init; }

        [JsonPropertyName("enable_starttls")]
        public bool? EnableStartTls { get; init; }
    }

    private static long ResolveMaxCrlSize(long? configuredValue, long fallback, string context)
    {
        var resolved = configuredValue ?? fallback;
        return resolved is < MinMaxCrlSizeBytes or > MaxMaxCrlSizeBytes
            ? throw new InvalidOperationException($"{context} must be between {MinMaxCrlSizeBytes} and {MaxMaxCrlSizeBytes} bytes.")
            : resolved;
    }
}
