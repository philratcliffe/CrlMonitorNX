using CrlMonitor.Diagnostics;

namespace CrlMonitor.Models;

internal sealed record CrlCheckRun(
    IReadOnlyList<CrlCheckResult> Results,
    RunDiagnostics Diagnostics,
    DateTime GeneratedAtUtc);
