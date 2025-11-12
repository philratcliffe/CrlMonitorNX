using CrlMonitor.State;

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

        var result = await store.GetLastFetchAsync(new Uri("http://example.com"), CancellationToken.None).ConfigureAwait(true);

        Assert.Null(result);
    }

    /// <summary>
    /// Values persist across reads.
    /// </summary>
    [Fact]
    public static async Task SaveLastFetchAsyncPersistsValue()
    {
        // CA2007 is suppressed here because the test intentionally awaits many asynchronous calls
        // and the default SynchronizationContext is irrelevant inside the test runner.
#pragma warning disable CA2007
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

    /// <summary>
    /// Stress test concurrently reading and writing from multiple threads.
    /// </summary>
    [Fact]
    public static async Task ConcurrentAccessIsSerialized()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "state.json");
        using var store = new FileStateStore(path);
        var cancellationToken = CancellationToken.None;
        var uris = new[]
        {
            new Uri("http://a.example.com"),
            new Uri("http://b.example.com"),
            new Uri("http://c.example.com"),
            new Uri("http://d.example.com")
        };

        var tasks = new Task[32];
        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () => {
                var index = i % uris.Length;
                var uri = uris[index];
                var timestamp = DateTime.UtcNow.AddMinutes(i);
                await store.SaveLastFetchAsync(uri, timestamp, cancellationToken);
                var readBack = await store.GetLastFetchAsync(uri, cancellationToken);
                _ = Assert.NotNull(readBack);
                Assert.Equal(DateTimeKind.Utc, readBack!.Value.Kind);
                Assert.True(Math.Abs((readBack.Value - timestamp).TotalMilliseconds) < 5_000);
                var alertKey = FormattableString.Invariant($"{uri}|{i}");
                await store.SaveAlertCooldownAsync(alertKey, timestamp, cancellationToken);
                var alertBack = await store.GetAlertCooldownAsync(alertKey, cancellationToken);
                _ = Assert.NotNull(alertBack);
                Assert.True(Math.Abs((alertBack!.Value - timestamp).TotalMilliseconds) < 5_000);
            }, cancellationToken);
        }

        await Task.WhenAll(tasks);

#pragma warning restore CA2007
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            this.Path = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString())).FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(this.Path, true);
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
