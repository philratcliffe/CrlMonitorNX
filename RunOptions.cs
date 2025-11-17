using CrlMonitor.Notifications;

namespace CrlMonitor;

internal sealed record RunOptions(
    bool ConsoleReports,
    bool ConsoleVerbose,
    bool CsvReports,
    string CsvOutputPath,
    bool CsvAppendTimestamp,
    bool HtmlReportEnabled,
    string? HtmlReportPath,
    string? HtmlReportUrl,
    long DefaultMaxCrlSizeBytes,
    TimeSpan FetchTimeout,
    int MaxParallelFetches,
    string StateFilePath,
    bool UseSystemProxy,
    IReadOnlyList<CrlConfigEntry> Crls,
    ReportOptions? Reports,
    AlertOptions? Alerts);
