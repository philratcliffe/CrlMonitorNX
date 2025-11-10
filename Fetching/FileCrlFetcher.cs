namespace CrlMonitor.Fetching;

internal sealed class FileCrlFetcher : ICrlFetcher
{
    public async Task<FetchedCrl> FetchAsync(CrlConfigEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        var path = entry.Uri.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"File URI '{entry.Uri}' is invalid.");
        }

        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"CRL file not found: {path}", path);
        }

        if (fileInfo.Length > entry.MaxCrlSizeBytes)
        {
            throw new CrlTooLargeException(entry.Uri, entry.MaxCrlSizeBytes, fileInfo.Length);
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = await CrlContentLimiter.ReadAllBytesAsync(stream, entry.Uri, entry.MaxCrlSizeBytes, cancellationToken).ConfigureAwait(false);
        return new FetchedCrl(bytes, TimeSpan.Zero, bytes.LongLength);
    }
}
