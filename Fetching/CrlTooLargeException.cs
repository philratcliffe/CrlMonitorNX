using System;

namespace CrlMonitor.Fetching;

/// <summary>
/// Exception thrown when a CRL exceeds the configured maximum size.
/// </summary>
public sealed class CrlTooLargeException : Exception
{
    /// <summary>
    /// Initialises a new instance of the <see cref="CrlTooLargeException"/> class.
    /// </summary>
    public CrlTooLargeException()
    {
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="CrlTooLargeException"/> class.
    /// </summary>
    /// <param name="message">Exception message.</param>
    public CrlTooLargeException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="CrlTooLargeException"/> class.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="innerException">Inner exception.</param>
    public CrlTooLargeException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance of the <see cref="CrlTooLargeException"/> class for the supplied URI.
    /// </summary>
    /// <param name="uri">CRL URI.</param>
    /// <param name="limitBytes">Configured max size in bytes.</param>
    /// <param name="observedBytes">Observed payload size in bytes.</param>
    public CrlTooLargeException(Uri uri, long limitBytes, long? observedBytes = null)
        : base(BuildMessage(uri, limitBytes, observedBytes))
    {
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        LimitBytes = limitBytes;
        ObservedBytes = observedBytes;
    }

    /// <summary>
    /// Gets the CRL URI when provided.
    /// </summary>
    public Uri? Uri { get; }

    /// <summary>
    /// Gets the configured maximum size in bytes.
    /// </summary>
    public long LimitBytes { get; }

    /// <summary>
    /// Gets the observed payload size, if known.
    /// </summary>
    public long? ObservedBytes { get; }

    private static string BuildMessage(Uri uri, long limitBytes, long? observedBytes)
    {
        var message = $"CRL '{uri}' exceeded the configured {limitBytes} byte limit.";
        if (observedBytes.HasValue)
        {
            message += $" Observed {observedBytes.Value} bytes.";
        }

        return message;
    }
}
