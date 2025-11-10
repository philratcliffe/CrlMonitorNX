namespace CrlMonitor.Fetching;

/// <summary>
/// Reads stream content whilst enforcing a maximum size.
/// </summary>
internal static class CrlContentLimiter
{
    private const int BufferSize = 81920;

    public static async Task<byte[]> ReadAllBytesAsync(Stream source, Uri uri, long limitBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limitBytes);

        using var memory = new MemoryStream();
        var buffer = new byte[BufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (memory.Length + read > limitBytes)
            {
                var observed = memory.Length + read;
                throw new CrlTooLargeException(uri, limitBytes, observed);
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return memory.ToArray();
    }
}
