using System;
using System.Collections.Generic;

namespace CrlMonitor;

internal sealed record RunOptions(
    bool ConsoleReports,
    bool CsvReports,
    string CsvOutputPath,
    bool CsvAppendTimestamp,
    TimeSpan FetchTimeout,
    int MaxParallelFetches,
    string StateFilePath,
    IReadOnlyList<CrlConfigEntry> Crls);
