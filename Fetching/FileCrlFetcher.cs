using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Models;

namespace CrlMonitor.Fetching;

internal sealed class FileCrlFetcher : ICrlFetcher
{
    public Task<FetchedCrl> FetchAsync(CrlConfigEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        var path = entry.Uri.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"File URI '{entry.Uri}' is invalid.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"CRL file not found: {path}", path);
        }

        return FetchInternalAsync(path, cancellationToken);
    }

    private static async Task<FetchedCrl> FetchInternalAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return new FetchedCrl(bytes, TimeSpan.Zero, bytes.LongLength);
    }
}
