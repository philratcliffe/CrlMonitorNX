using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Crl;
using CrlMonitor.Fetching;
using CrlMonitor.Models;
using Xunit;

namespace CrlMonitor.Tests;

/// <summary>
/// Verifies file fetcher behaviour.
/// </summary>
public static class FileCrlFetcherTests
{
    /// <summary>
    /// Ensures file fetcher returns file contents.
    /// </summary>
    [Fact]
    public static async Task FetchAsyncReadsFile()
    {
        var fetcher = new FileCrlFetcher();
        var bytes = new byte[] { 1, 2, 3 };
        var tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        await File.WriteAllBytesAsync(tempPath, bytes);

        try
        {
            var entry = new CrlConfigEntry(new Uri(tempPath), SignatureValidationMode.None, null, 0.8, null);
            var result = await fetcher.FetchAsync(entry, CancellationToken.None);

            Assert.Equal(bytes, result.Content);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
