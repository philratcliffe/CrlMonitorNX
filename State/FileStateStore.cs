using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrlMonitor.State;

internal sealed class FileStateStore : IStateStore, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FileStateStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this._filePath = Path.GetFullPath(filePath);
    }

    public async Task<DateTime?> GetLastFetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        await this._gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await this.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            return state.LastFetch.TryGetValue(uri.ToString(), out var value) ? value : null;
        }
        finally
        {
            _ = this._gate.Release();
        }
    }

    public async Task SaveLastFetchAsync(Uri uri, DateTime fetchedAtUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var normalized = DateTime.SpecifyKind(fetchedAtUtc, DateTimeKind.Utc);
        await this._gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await this.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            state.LastFetch[uri.ToString()] = normalized;
            await this.WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = this._gate.Release();
        }
    }

    public async Task<DateTime?> GetLastReportSentAsync(CancellationToken cancellationToken)
    {
        await this._gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await this.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            return state.LastReportSentUtc;
        }
        finally
        {
            _ = this._gate.Release();
        }
    }

    public async Task SaveLastReportSentAsync(DateTime sentAtUtc, CancellationToken cancellationToken)
    {
        var normalized = DateTime.SpecifyKind(sentAtUtc, DateTimeKind.Utc);
        await this._gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await this.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            state.LastReportSentUtc = normalized;
            await this.WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = this._gate.Release();
        }
    }

    public async Task<DateTime?> GetAlertCooldownAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await this._gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await this.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            return state.AlertCooldowns.TryGetValue(key, out var timestamp) ? timestamp : null;
        }
        finally
        {
            _ = this._gate.Release();
        }
    }

    public async Task SaveAlertCooldownAsync(string key, DateTime triggeredAtUtc, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var normalized = DateTime.SpecifyKind(triggeredAtUtc, DateTimeKind.Utc);
        await this._gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await this.ReadStateAsync(cancellationToken).ConfigureAwait(false);
            state.AlertCooldowns[key] = normalized;
            await this.WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = this._gate.Release();
        }
    }

    private async Task<StateDocument> ReadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(this._filePath))
        {
            return new StateDocument();
        }

        var json = await File.ReadAllTextAsync(this._filePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new StateDocument();
        }

        try
        {
            var state = JsonSerializer.Deserialize<StateDocument>(json, SerializerOptions) ?? new StateDocument();
            state.Normalize();
            return state;
        }
        catch (JsonException)
        {
            // Corrupt state file - return empty state (defensive fallback)
            return new StateDocument();
        }
    }

    private async Task WriteStateAsync(StateDocument state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(this._filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(this._filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        this._gate.Dispose();
    }

    private sealed class StateDocument
    {
        public StateDocument()
        {
            this.LastFetch = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            this.AlertCooldowns = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        [JsonPropertyName("last_fetch")]
        public Dictionary<string, DateTime> LastFetch { get; set; }

        [JsonPropertyName("alert_cooldowns")]
        public Dictionary<string, DateTime> AlertCooldowns { get; set; }

        [JsonPropertyName("last_report_sent_utc")]
        public DateTime? LastReportSentUtc { get; set; }

        public void Normalize()
        {
            this.LastFetch = this.LastFetch != null
                ? new Dictionary<string, DateTime>(this.LastFetch, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            this.AlertCooldowns = this.AlertCooldowns != null
                ? new Dictionary<string, DateTime>(this.AlertCooldowns, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            NormalizeDictionary(this.LastFetch);
            NormalizeDictionary(this.AlertCooldowns);
            if (this.LastReportSentUtc.HasValue)
            {
                this.LastReportSentUtc = NormalizeDateTime(this.LastReportSentUtc.Value);
            }
        }

        private static void NormalizeDictionary(Dictionary<string, DateTime> dictionary)
        {
            var keys = new List<string>(dictionary.Keys);
            foreach (var key in keys)
            {
                dictionary[key] = NormalizeDateTime(dictionary[key]);
            }
        }

        private static DateTime NormalizeDateTime(DateTime value)
        {
            // System.Text.Json may deserialize timezone-aware DateTimes as Local kind
            // Convert to UTC to ensure consistency across timezones
            return value.Kind switch {
                DateTimeKind.Local => value.ToUniversalTime(),
                DateTimeKind.Utc => value,
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
                _ => throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "Unexpected DateTimeKind value.")
            };
        }
    }
}
