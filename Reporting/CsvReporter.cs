using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
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
        using var writer = new StreamWriter(stream);
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            NewLine = Environment.NewLine,
            HasHeaderRecord = true
        };

        using var csv = new CsvWriter(writer, csvConfig);
        csv.WriteField("uri");
        csv.WriteField("status");
        csv.WriteField("signature_status");
        csv.WriteField("signature_error");
        csv.WriteField("health_status");
        csv.WriteField("duration_ms");
        csv.WriteField("error");
        await csv.NextRecordAsync().ConfigureAwait(false);

        foreach (var result in run.Results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = result.Succeeded ? "OK" : "ERROR";
            csv.WriteField(result.Uri.ToString());
            csv.WriteField(status);
            csv.WriteField(result.SignatureStatus ?? "Unknown");
            csv.WriteField(result.SignatureError ?? string.Empty);
            csv.WriteField(result.HealthStatus ?? "Unknown");
            csv.WriteField(result.Duration.TotalMilliseconds);
            csv.WriteField(result.Error ?? string.Empty);
            await csv.NextRecordAsync().ConfigureAwait(false);
        }
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
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
