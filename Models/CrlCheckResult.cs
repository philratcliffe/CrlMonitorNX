using CrlMonitor.Crl;

namespace CrlMonitor.Models;

internal sealed record CrlCheckResult(
    Uri Uri,
    CrlStatus Status,
    TimeSpan Duration,
    ParsedCrl? ParsedCrl,
    string? ErrorInfo,
    DateTime? PreviousFetchUtc,
    TimeSpan? DownloadDuration,
    long? ContentLength,
    DateTime CheckedAtUtc,
    string? SignatureStatus);
