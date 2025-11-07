using System;
using CrlMonitor.Crl;

namespace CrlMonitor.Models;

internal sealed record CrlCheckResult(
    Uri Uri,
    string Status,
    TimeSpan Duration,
    ParsedCrl? ParsedCrl,
    string? ErrorInfo);
