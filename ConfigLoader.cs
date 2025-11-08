using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CrlMonitor.Crl;

namespace CrlMonitor;

internal static class ConfigLoader
{
    private const double DefaultExpiryThreshold = 0.8;
    private const double MinExpiryThreshold = 0.1;
    private const double MaxExpiryThreshold = 1.0;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
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
        if (timeoutSeconds <= 0 || timeoutSeconds > 600)
        {
            throw new InvalidOperationException("fetch_timeout_seconds must be between 1 and 600 seconds.");
        }

        if (maxParallel < 1 || maxParallel > 64)
        {
            throw new InvalidOperationException("max_parallel_fetches must be between 1 and 64.");
        }

        var entries = BuildEntries(document.Uris, configDirectory);
        return new RunOptions(
            document.ConsoleReports ?? true,
            document.CsvReports ?? true,
            ResolvePath(configDirectory, csvPath),
            document.CsvAppendTimestamp ?? false,
            TimeSpan.FromSeconds(timeoutSeconds),
            maxParallel,
            ResolvePath(configDirectory, stateFilePath),
            entries);
    }

    private static List<CrlConfigEntry> BuildEntries(IReadOnlyList<CrlDocument>? documents, string baseDirectory)
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
                throw new InvalidOperationException($"Invalid uri '{document.Uri}'.");
            }

            if (!seen.Add(uri.ToString()))
            {
                throw new InvalidOperationException($"Found duplicate uri '{uri}'.");
            }

            var signatureMode = ParseSignatureMode(document.SignatureValidationMode);
            var caPath = ResolveCaPath(signatureMode, document.CaCertificatePath, baseDirectory, uri);
            var threshold = ParseExpiryThreshold(document.ExpiryThreshold, uri);
            var ldap = ParseLdap(document.Ldap, uri);

            if (ldap != null && !IsLdapScheme(uri))
            {
                throw new InvalidOperationException($"LDAP credentials can only be specified for ldap/ldaps URIs. Offending URI: {uri}");
            }

            if (ldap == null && IsLdapScheme(uri) && document.Ldap != null)
            {
                throw new InvalidOperationException($"LDAP block for {uri} must specify both username and password.");
            }

            entries.Add(new CrlConfigEntry(uri, signatureMode, caPath, threshold, ldap));
        }

        return entries;
    }

    private static SignatureValidationMode ParseSignatureMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return SignatureValidationMode.None;
        }

        if (value.Equals("ca-cert", StringComparison.OrdinalIgnoreCase))
        {
            return SignatureValidationMode.CaCertificate;
        }

        throw new InvalidOperationException("signature_validation_mode must be 'none' or 'ca-cert'.");
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
        if (!File.Exists(resolved))
        {
            throw new InvalidOperationException($"ca_certificate_path '{caPath}' not found for URI {uri}.");
        }

        return resolved;
    }

    private static double ParseExpiryThreshold(double? value, Uri uri)
    {
        var threshold = value ?? DefaultExpiryThreshold;
        if (threshold < MinExpiryThreshold || threshold > MaxExpiryThreshold)
        {
            throw new InvalidOperationException($"expiry_threshold for {uri} must be between {MinExpiryThreshold} and {MaxExpiryThreshold}.");
        }

        return threshold;
    }

    private static LdapCredentials? ParseLdap(LdapDocument? document, Uri uri)
    {
        if (document == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(document.Username) || string.IsNullOrWhiteSpace(document.Password))
        {
            throw new InvalidOperationException($"ldap.username and ldap.password must be supplied for {uri}.");
        }

        return new LdapCredentials(document.Username, document.Password);
    }

    private static bool IsLdapScheme(Uri uri)
    {
        return uri.Scheme.Equals("ldap", StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePath(string baseDirectory, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static string RequirePath(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value;
    }

    private sealed record ConfigDocument
    {
        [JsonPropertyName("console_reports")]
        public bool? ConsoleReports { get; init; }

        [JsonPropertyName("csv_reports")]
        public bool? CsvReports { get; init; }

        [JsonPropertyName("csv_output_path")]
        public string? CsvOutputPath { get; init; }

        [JsonPropertyName("csv_append_timestamp")]
        public bool? CsvAppendTimestamp { get; init; }

        [JsonPropertyName("fetch_timeout_seconds")]
        public int? FetchTimeoutSeconds { get; init; }

        [JsonPropertyName("max_parallel_fetches")]
        public int? MaxParallelFetches { get; init; }

        [JsonPropertyName("state_file_path")]
        public string? StateFilePath { get; init; }

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
    }

    private sealed record LdapDocument
    {
        [JsonPropertyName("username")]
        public string? Username { get; init; }

        [JsonPropertyName("password")]
        public string? Password { get; init; }
    }
}
