using System;
using CrlMonitor.Crl;

namespace CrlMonitor.Models;

internal sealed record CrlCheckResult(
    Uri Uri,
    bool Succeeded,
    TimeSpan Duration,
    ParsedCrl? ParsedCrl,
    string? SignatureStatus,
    string? SignatureError,
    string? Error);
