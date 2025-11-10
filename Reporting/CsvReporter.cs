using System.Globalization;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal sealed class CsvReporter : IReporter
{
    private readonly string _outputPath;
    private readonly ReportingStatus _status;

    public CsvReporter(string outputPath, ReportingStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        this._outputPath = outputPath;
        this._status = status ?? throw new ArgumentNullException(nameof(status));
    }

    public async Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(this._outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(this._outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        await CsvReportFormatter.WriteAsync(writer, run, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        this._status.RecordCsv(this._outputPath);
    }

    internal static string ResolveOutputPath(string path, bool appendTimestamp)
    {
        if (!appendTimestamp)
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(directory, $"{baseName}_{timestamp}{extension}");
    }
}
