using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
            var entries = await ReadEntriesAsync(cancellationToken).ConfigureAwait(false);
            return entries.TryGetValue(uri.ToString(), out var value)
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : null;
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
            var entries = await ReadEntriesAsync(cancellationToken).ConfigureAwait(false);
            entries[uri.ToString()] = normalized;
            await WriteEntriesAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, DateTime>> ReadEntriesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var data = await JsonSerializer.DeserializeAsync<Dictionary<string, DateTime>>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        return data != null
            ? new Dictionary<string, DateTime>(data, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task WriteEntriesAsync(Dictionary<string, DateTime> entries, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, entries, SerializerOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
