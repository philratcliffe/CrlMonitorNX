using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CrlMonitor.State;

internal sealed class FileStateStore : IStateStore, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FileStateStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public async Task<DateTime?> GetLastFetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            return state.LastFetch.TryGetValue(uri.ToString(), out var value)
                ? value
                : (DateTime?)null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveLastFetchAsync(Uri uri, DateTime fetchedAtUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var normalized = DateTime.SpecifyKind(fetchedAtUtc, DateTimeKind.Utc);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            state.LastFetch[uri.ToString()] = normalized;
            await WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DateTime?> GetLastReportSentAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            return state.LastReportSentUtc;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveLastReportSentAsync(DateTime sentAtUtc, CancellationToken cancellationToken)
    {
        var normalized = DateTime.SpecifyKind(sentAtUtc, DateTimeKind.Utc);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            state.LastReportSentUtc = normalized;
            await WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DateTime?> GetAlertCooldownAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            return state.AlertCooldowns.TryGetValue(key, out var timestamp)
                ? timestamp
                : (DateTime?)null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAlertCooldownAsync(string key, DateTime triggeredAtUtc, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var normalized = DateTime.SpecifyKind(triggeredAtUtc, DateTimeKind.Utc);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await ReadStateAsync(cancellationToken).ConfigureAwait(false);
            state.AlertCooldowns[key] = normalized;
            await WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StateDocument> ReadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new StateDocument();
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new StateDocument();
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("last_fetch", out _))
        {
            var state = JsonSerializer.Deserialize<StateDocument>(json, SerializerOptions) ?? new StateDocument();
            state.Normalize();
            return state;
        }

        var legacy = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json, SerializerOptions);
        if (legacy != null)
        {
            var state = new StateDocument();
            foreach (var pair in legacy)
            {
                state.LastFetch[pair.Key] = DateTime.SpecifyKind(pair.Value, DateTimeKind.Utc);
            }

            return state;
        }

        return new StateDocument();
    }

    private async Task WriteStateAsync(StateDocument state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _gate.Dispose();
    }

    private sealed class StateDocument
    {
        public StateDocument()
        {
            LastFetch = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            AlertCooldowns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        [JsonPropertyName("last_fetch")]
        public Dictionary<string, DateTime> LastFetch { get; set; }

        [JsonPropertyName("alert_cooldowns")]
        public Dictionary<string, DateTime> AlertCooldowns { get; set; }

        [JsonPropertyName("last_report_sent_utc")]
        public DateTime? LastReportSentUtc { get; set; }

        public void Normalize()
        {
            LastFetch = LastFetch != null
                ? new Dictionary<string, DateTime>(LastFetch, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            AlertCooldowns = AlertCooldowns != null
                ? new Dictionary<string, DateTime>(AlertCooldowns, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            NormalizeDictionary(LastFetch);
            NormalizeDictionary(AlertCooldowns);
            if (LastReportSentUtc.HasValue)
            {
                LastReportSentUtc = DateTime.SpecifyKind(LastReportSentUtc.Value, DateTimeKind.Utc);
            }
        }

        private static void NormalizeDictionary(Dictionary<string, DateTime> dictionary)
        {
            var keys = new List<string>(dictionary.Keys);
            foreach (var key in keys)
            {
                dictionary[key] = DateTime.SpecifyKind(dictionary[key], DateTimeKind.Utc);
            }
        }
    }
}
