using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CrlMonitor;
using CrlMonitor.Crl;
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
        Assert.Equal(TimeSpan.FromSeconds(45), options.FetchTimeout);
        Assert.Equal(3, options.MaxParallelFetches);
        Assert.Equal(Path.Combine(temp.Path, "state", "state.json"), options.StateFilePath);
        Assert.Equal(2, options.Crls.Count);

        var httpEntry = options.Crls[0];
        Assert.Equal(new Uri("http://crl.example.com/root.crl"), httpEntry.Uri);
        Assert.Equal(SignatureValidationMode.None, httpEntry.SignatureValidationMode);
        Assert.Null(httpEntry.CaCertificatePath);
        Assert.Equal(0.8, httpEntry.ExpiryThreshold);
        Assert.Null(httpEntry.Ldap);

        var ldapEntry = options.Crls[1];
        Assert.Equal(new Uri("ldap://dc1.example.com/root"), ldapEntry.Uri);
        Assert.Equal(SignatureValidationMode.CaCertificate, ldapEntry.SignatureValidationMode);
        Assert.Equal(caPath, ldapEntry.CaCertificatePath);
        Assert.Equal(0.95, ldapEntry.ExpiryThreshold);
        Assert.NotNull(ldapEntry.Ldap);
        Assert.Equal("CN=svc,DC=example,DC=com", ldapEntry.Ldap!.Username);
        Assert.Equal("secret", ldapEntry.Ldap.Password);
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
