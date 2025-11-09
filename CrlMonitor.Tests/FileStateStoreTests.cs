using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.State;
using Xunit;

namespace CrlMonitor.Tests;

/// <summary>
/// Validates the file-backed state store implementation.
/// </summary>
public static class FileStateStoreTests
{
    /// <summary>
    /// Missing file returns null state.
    /// </summary>
    [Fact]
    public static async Task GetLastFetchAsyncReturnsNullWhenFileMissing()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "state", "last-fetch.json");
        using var store = new FileStateStore(path);

        var result = await store.GetLastFetchAsync(new Uri("http://example.com"), CancellationToken.None);

        Assert.Null(result);
    }

    /// <summary>
    /// Values persist across reads.
    /// </summary>
    [Fact]
    public static async Task SaveLastFetchAsyncPersistsValue()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "state.json");
        using var store = new FileStateStore(path);
        var uri = new Uri("http://example.com");
        var timestamp = DateTime.UtcNow;

        await store.SaveLastFetchAsync(uri, timestamp, CancellationToken.None);
        var roundTrip = await store.GetLastFetchAsync(uri, CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.Equal(timestamp, roundTrip);
    }

    /// <summary>
    /// Re-saving updates the stored value.
    /// </summary>
    [Fact]
    public static async Task SaveLastFetchAsyncOverwritesExistingValue()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "state.json");
        using var store = new FileStateStore(path);
        var uri = new Uri("http://example.com");
        var first = DateTime.UtcNow.AddHours(-1);
        var second = DateTime.UtcNow;

        await store.SaveLastFetchAsync(uri, first, CancellationToken.None);
        await store.SaveLastFetchAsync(uri, second, CancellationToken.None);
        var result = await store.GetLastFetchAsync(uri, CancellationToken.None);

        Assert.Equal(second, result);
    }

    /// <summary>
    /// Ensures legacy state layout is still readable.
    /// </summary>
    [Fact]
    public static async Task GetLastFetchAsyncReadsLegacyFormat()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "state.json");
        var legacy = """
        {
          "http://example.com/": "2024-01-01T00:00:00Z"
        }
        """;
        await File.WriteAllTextAsync(path, legacy);
        using var store = new FileStateStore(path);

        var result = await store.GetLastFetchAsync(new Uri("http://example.com"), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(DateTimeKind.Utc, result!.Value.Kind);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), result.Value);
    }

    /// <summary>
    /// Ensures report timestamps persist.
    /// </summary>
    [Fact]
    public static async Task SaveLastReportSentAsyncPersistsValue()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "state.json");
        using var store = new FileStateStore(path);
        var sentAt = DateTime.UtcNow;

        await store.SaveLastReportSentAsync(sentAt, CancellationToken.None);
        var roundtrip = await store.GetLastReportSentAsync(CancellationToken.None);

        Assert.Equal(sentAt, roundtrip);
    }

    /// <summary>
    /// Ensures alert cooldown entries persist.
    /// </summary>
    [Fact]
    public static async Task SaveAlertCooldownAsyncPersistsValue()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "state.json");
        using var store = new FileStateStore(path);
        var key = "http://example.com|expired";

        var timestamp = DateTime.UtcNow;
        await store.SaveAlertCooldownAsync(key, timestamp, CancellationToken.None);
        var result = await store.GetAlertCooldownAsync(key, CancellationToken.None);
        var missing = await store.GetAlertCooldownAsync("http://example.com|other", CancellationToken.None);

        Assert.Equal(timestamp, result);
        Assert.Null(missing);
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString())).FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
