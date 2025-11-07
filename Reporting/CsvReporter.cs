using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal sealed class CsvReporter : IReporter
{
    private readonly string _outputPath;
    private readonly bool _appendTimestamp;

    public CsvReporter(string outputPath, bool appendTimestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        _outputPath = outputPath;
        _appendTimestamp = appendTimestamp;
    }

    public async Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();

        var path = _appendTimestamp ? AppendTimestamp(_outputPath) : _outputPath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteLineAsync("uri,status,duration_ms,error").ConfigureAwait(false);
        foreach (var result in run.Results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = result.Succeeded ? "OK" : "ERR";
            var duration = result.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
            var line = $"{result.Uri},{status},{duration},\"{result.Error ?? string.Empty}\"";
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }
    }

    private static string AppendTimestamp(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(directory, $"{baseName}_{timestamp}{extension}");
    }
}
