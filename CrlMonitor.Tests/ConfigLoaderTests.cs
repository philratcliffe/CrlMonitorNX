using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CrlMonitor;
using CrlMonitor.Crl;
using CrlMonitor.Notifications;
using CrlMonitor.Models;
using Xunit;

namespace CrlMonitor.Tests;

/// <summary>
/// Tests the behaviour of <see cref="ConfigLoader" />.
/// </summary>
public static class ConfigLoaderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Verifies that a valid configuration is materialised into run options.
    /// </summary>
    [Fact]
    public static void LoadParsesValidConfig()
    {
        using var temp = new TempFolder();
        var caPath = temp.WriteFile("example-ca.pem", "dummy");
        var configPath = temp.WriteJson("config.json", $$"""
        {
          "console_reports": true,
          "csv_append_timestamp": true,
          "csv_output_path": "out/report.csv",
          "csv_reports": true,
          "html_report_enabled": true,
          "html_report_path": "reports/latest.html",
          "fetch_timeout_seconds": 45,
          "max_parallel_fetches": 3,
          "state_file_path": "state/state.json",
          "uris": [
            {
              "uri": "http://crl.example.com/root.crl",
              "signature_validation_mode": "none"
            },
            {
              "uri": "ldap://dc1.example.com/root",
              "signature_validation_mode": "ca-cert",
              "ca_certificate_path": "example-ca.pem",
              "expiry_threshold": 0.95,
              "max_crl_size_bytes": 5242880,
              "ldap": {
                "username": "CN=svc,DC=example,DC=com",
                "password": "secret"
              }
            }
          ]
        }
        """);

        var options = ConfigLoader.Load(configPath);

        Assert.True(options.ConsoleReports);
        Assert.True(options.CsvReports);
        Assert.True(options.CsvAppendTimestamp);
        Assert.Equal(Path.Combine(temp.Path, "out", "report.csv"), options.CsvOutputPath);
        Assert.True(options.HtmlReportEnabled);
        Assert.Equal(Path.Combine(temp.Path, "reports", "latest.html"), options.HtmlReportPath);
        Assert.Null(options.HtmlReportUrl);
        Assert.Equal(TimeSpan.FromSeconds(45), options.FetchTimeout);
        Assert.Equal(3, options.MaxParallelFetches);
        Assert.Equal(Path.Combine(temp.Path, "state", "state.json"), options.StateFilePath);
        Assert.Equal(2, options.Crls.Count);
        Assert.Equal(10 * 1024 * 1024, options.DefaultMaxCrlSizeBytes);

        var httpEntry = options.Crls[0];
        Assert.Equal(new Uri("http://crl.example.com/root.crl"), httpEntry.Uri);
        Assert.Equal(SignatureValidationMode.None, httpEntry.SignatureValidationMode);
        Assert.Null(httpEntry.CaCertificatePath);
        Assert.Equal(0.8, httpEntry.ExpiryThreshold);
        Assert.Null(httpEntry.Ldap);
        Assert.Equal(10 * 1024 * 1024, httpEntry.MaxCrlSizeBytes);

        var ldapEntry = options.Crls[1];
        Assert.Equal(new Uri("ldap://dc1.example.com/root"), ldapEntry.Uri);
        Assert.Equal(SignatureValidationMode.CaCertificate, ldapEntry.SignatureValidationMode);
        Assert.Equal(caPath, ldapEntry.CaCertificatePath);
        Assert.Equal(0.95, ldapEntry.ExpiryThreshold);
        Assert.NotNull(ldapEntry.Ldap);
        Assert.Equal("CN=svc,DC=example,DC=com", ldapEntry.Ldap!.Username);
        Assert.Equal("secret", ldapEntry.Ldap.Password);
        Assert.Equal(5_242_880, ldapEntry.MaxCrlSizeBytes);
    }

    /// <summary>
    /// Ensures that the global max CRL size value is honoured.
    /// </summary>
    [Fact]
    public static void LoadHonoursGlobalMaxCrlSize()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "max_crl_size_bytes": 2097152,
          "state_file_path": "state.json",
          "uris": [
            { "uri": "http://example.com/root.crl" }
          ]
        }
        """);

        var options = ConfigLoader.Load(configPath);

        Assert.Equal(2_097_152, options.DefaultMaxCrlSizeBytes);
        var entry = Assert.Single(options.Crls);
        Assert.Equal(2_097_152, entry.MaxCrlSizeBytes);
    }

    /// <summary>
    /// Ensures invalid max size values are rejected.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(200_000_000)]
    public static void LoadThrowsWhenMaxCrlSizeInvalid(long candidate)
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", $$"""
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "max_crl_size_bytes": {{candidate}},
          "state_file_path": "state.json",
          "uris": [
            { "uri": "http://example.com/root.crl" }
          ]
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("max_crl_size_bytes", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures per-entry overrides must also satisfy the allowed range.
    /// </summary>
    [Fact]
    public static void LoadThrowsWhenEntryMaxCrlSizeInvalid()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "uris": [
            {
              "uri": "http://example.com/root.crl",
              "max_crl_size_bytes": 0
            }
          ]
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("max_crl_size_bytes", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures unsupported URI schemes are rejected.
    /// </summary>
    [Fact]
    public static void LoadThrowsWhenSchemeUnsupported()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "uris": [
            { "uri": "ftp://example.com/root.crl" }
          ]
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("unsupported scheme", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures unsupported signature modes are rejected.
    /// </summary>
    [Fact]
    public static void LoadThrowsForUnknownSignatureMode()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "uris": [
            {
              "uri": "http://example.com/root.crl",
              "signature_validation_mode": "full-chain"
            }
          ]
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("signature_validation_mode", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures fetch timeout must stay within the allowed range.
    /// </summary>
    [Fact]
    public static void LoadThrowsWhenTimeoutOutOfRange()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 1000,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "uris": [
            { "uri": "http://example.com/root.crl" }
          ]
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("fetch_timeout_seconds", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures relative file URIs resolve against config directory.
    /// </summary>
    [Fact]
    public static void LoadSupportsRelativeFileUris()
    {
        using var temp = new TempFolder();
        var crlPath = temp.WriteBinary("examples/rel.crl", new byte[] { 0 });
        var fileName = Path.GetFileName(crlPath);
        var configPath = temp.WriteJson("config.json", $$"""
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "uris": [
            {
              "uri": "file://./{{fileName}}"
            }
          ]
        }
        """);

        var options = ConfigLoader.Load(configPath);

        var crl = Assert.Single(options.Crls);
        Assert.Equal("file", crl.Uri.Scheme);
    }

    /// <summary>
    /// Ensures CA certificate paths are required for ca-cert validation.
    /// </summary>
    [Fact]
    public static void LoadThrowsWhenCaCertMissing()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "uris": [
            {
              "uri": "http://example.com/root.crl",
              "signature_validation_mode": "ca-cert"
            }
          ]
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("ca_certificate_path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures report and alert sections are parsed.
    /// </summary>
    [Fact]
    public static void LoadParsesReportsAndAlerts()
    {
        using var temp = new TempFolder();
        var caPath = temp.WriteFile("ca.pem", "dummy");
        var configPath = temp.WriteJson("config.json", $$"""
        {
          "console_reports": false,
          "csv_reports": false,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "smtp": {
            "host": "smtp.example.com",
            "port": 2525,
            "username": "svc",
            "password": "pw",
            "from": "CRL Monitor <svc@example.com>"
          },
          "reports": {
            "enabled": true,
            "frequency": "weekly",
            "recipients": ["ops@example.com"],
            "subject": "Weekly CRL",
            "include_summary": true,
            "include_full_csv": false
          },
          "alerts": {
            "enabled": true,
            "recipients": ["oncall@example.com"],
            "statuses": ["ERROR", "EXPIRED", "EXPIRING"],
            "cooldown_hours": 12,
            "subject_prefix": "[ALERT]",
            "include_details": true
          },
          "uris": [
            {
              "uri": "http://example.com/root.crl",
              "signature_validation_mode": "ca-cert",
              "ca_certificate_path": "ca.pem"
            }
          ]
        }
        """);

        var options = ConfigLoader.Load(configPath);

        Assert.NotNull(options.Reports);
        var reports = options.Reports!;
        Assert.True(reports.Enabled);
        Assert.Equal(ReportFrequency.Weekly, reports.Frequency);
        Assert.Single(reports.Recipients);
        Assert.Equal("Weekly CRL", reports.Subject);
        Assert.True(reports.IncludeSummary);
        Assert.False(reports.IncludeFullCsv);
        Assert.Equal("smtp.example.com", reports.Smtp.Host);
        Assert.Equal(2525, reports.Smtp.Port);
        Assert.Equal("svc", reports.Smtp.Username);
        Assert.Equal("pw", reports.Smtp.Password);
        Assert.Equal("CRL Monitor <svc@example.com>", reports.Smtp.From);
        Assert.True(reports.Smtp.EnableStartTls);

        Assert.NotNull(options.Alerts);
        var alerts = options.Alerts!;
        Assert.True(alerts.Enabled);
        Assert.Single(alerts.Recipients);
        Assert.Equal(TimeSpan.FromHours(12), alerts.Cooldown);
        Assert.Equal("[ALERT]", alerts.SubjectPrefix);
        Assert.True(alerts.IncludeDetails);
        Assert.Equal(3, alerts.Statuses.Count);
        Assert.Contains(CrlStatus.Error, alerts.Statuses);
        Assert.Contains(CrlStatus.Expired, alerts.Statuses);
        Assert.Contains(CrlStatus.Expiring, alerts.Statuses);
        Assert.Equal(reports.Smtp, alerts.Smtp);
    }

    /// <summary>
    /// Ensures expiry thresholds stay within the accepted range.
    /// </summary>
    [Fact]
    public static void LoadThrowsWhenExpiryThresholdOutOfRange()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "uris": [
            {
              "uri": "http://example.com/root.crl",
              "expiry_threshold": 0.05
            }
          ]
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("expiry_threshold", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures alerts require at least one recipient when enabled.
    /// </summary>
    [Fact]
    public static void LoadThrowsWhenAlertsMissingRecipients()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "alerts": {
            "enabled": true,
            "recipients": [],
            "statuses": ["ERROR"]
          },
          "uris": [
            { "uri": "http://example.com/root.crl" }
          ]
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("alerts.recipients", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures alerts require at least one status when enabled.
    /// </summary>
    [Fact]
    public static void LoadThrowsWhenAlertsMissingStatuses()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "alerts": {
            "enabled": true,
            "recipients": ["ops@example.com"]
          },
          "uris": [
            { "uri": "http://example.com/root.crl" }
          ]
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("alerts.statuses", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures html report URL is parsed.
    /// </summary>
    [Fact]
    public static void LoadParsesHtmlReportUrl()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "html_report_enabled": true,
          "html_report_path": "reports/report.html",
          "html_report_url": "https://example.com/report.html",
          "smtp": {
            "host": "smtp.example.com",
            "port": 25,
            "username": "svc",
            "password": "pw",
            "from": "svc@example.com"
          },
          "reports": {
            "enabled": true,
            "frequency": "daily",
            "recipients": ["ops@example.com"]
          },
          "uris": [
            { "uri": "http://example.com/root.crl" }
          ]
        }
        """);

        var options = ConfigLoader.Load(configPath);

        Assert.True(options.HtmlReportEnabled);
        Assert.Equal(Path.Combine(temp.Path, "reports", "report.html"), options.HtmlReportPath);
        Assert.Equal("https://example.com/report.html", options.HtmlReportUrl);
    }

    /// <summary>
    /// Ensures SMTP password can be sourced from environment variable.
    /// </summary>
    [Fact]
    public static void LoadReadsSmtpPasswordFromEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable("SMTP_PASSWORD");
        Environment.SetEnvironmentVariable("SMTP_PASSWORD", "env-secret");
        try
        {
            using var temp = new TempFolder();
            var configPath = temp.WriteJson("config.json", """
            {
              "console_reports": true,
              "csv_reports": true,
              "csv_output_path": "report.csv",
              "csv_append_timestamp": false,
              "fetch_timeout_seconds": 30,
              "max_parallel_fetches": 1,
              "state_file_path": "state.json",
              "smtp": {
                "host": "smtp.example.com",
                "port": 587,
                "username": "svc",
                "from": "svc@example.com"
              },
              "reports": {
                "enabled": true,
                "frequency": "daily",
                "recipients": ["ops@example.com"]
              },
              "uris": [
                { "uri": "http://example.com/root.crl" }
              ]
            }
            """);

            var options = ConfigLoader.Load(configPath);

            Assert.Equal("env-secret", options.Reports!.Smtp.Password);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMTP_PASSWORD", previous);
        }
    }

    /// <summary>
    /// Ensures invalid report frequency is rejected.
    /// </summary>
    [Fact]
    public static void LoadThrowsWhenReportFrequencyInvalid()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "smtp": {
            "host": "smtp.example.com",
            "port": 25,
            "username": "svc",
            "password": "pw",
            "from": "svc@example.com"
          },
          "reports": {
            "enabled": true,
            "frequency": "hourly",
            "recipients": ["ops@example.com"]
          },
          "uris": [
            { "uri": "http://example.com/root.crl" }
          ]
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("reports.frequency", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures duplicate URIs are detected.
    /// </summary>
    [Fact]
    public static void LoadThrowsWhenDuplicateUris()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "uris": [
            { "uri": "http://example.com/root.crl" },
            { "uri": "HTTP://example.com/root.crl" }
          ]
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures LDAP credentials are disallowed for non-LDAP URIs.
    /// </summary>
    [Fact]
    public static void LoadThrowsWhenLdapSpecifiedForHttp()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "uris": [
            {
              "uri": "http://example.com/root.crl",
              "ldap": {
                "username": "user",
                "password": "pw"
              }
            }
          ]
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("LDAP", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures LDAP credentials require both username and password.
    /// </summary>
    [Fact]
    public static void LoadThrowsWhenLdapIncomplete()
    {
        using var temp = new TempFolder();
        var configPath = temp.WriteJson("config.json", """
        {
          "console_reports": true,
          "csv_reports": true,
          "csv_output_path": "report.csv",
          "csv_append_timestamp": false,
          "fetch_timeout_seconds": 30,
          "max_parallel_fetches": 1,
          "state_file_path": "state.json",
          "uris": [
            {
              "uri": "ldap://example.com/root",
              "ldap": {
                "username": ""
              }
            }
          ]
        }
        """);

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(configPath));
        Assert.Contains("ldap", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory().FullName;

        public string WriteJson(string fileName, string json)
        {
            var targetPath = System.IO.Path.Combine(Path, fileName);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
            var element = JsonDocument.Parse(json).RootElement;
            File.WriteAllText(targetPath, JsonSerializer.Serialize(element, JsonOptions));
            return targetPath;
        }

        public string WriteFile(string fileName, string content)
        {
            var targetPath = System.IO.Path.Combine(Path, fileName);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, content);
            return targetPath;
        }

        public string WriteBinary(string fileName, byte[] content)
        {
            var targetPath = System.IO.Path.Combine(Path, fileName);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, content);
            return targetPath;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // Ignore cleanup errors in tests.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore cleanup errors in tests.
            }
        }
    }
}
