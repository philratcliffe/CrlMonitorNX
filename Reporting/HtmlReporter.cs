using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal sealed class HtmlReporter : IReporter
{
    private readonly string _outputPath;
    private readonly ReportingStatus _status;

    public HtmlReporter(string outputPath, ReportingStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        this._outputPath = outputPath;
        this._status = status ?? throw new ArgumentNullException(nameof(status));
    }

    public async Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken)
    {
        await HtmlReportWriter.WriteAsync(this._outputPath, run, cancellationToken).ConfigureAwait(false);
        this._status.RecordHtml(this._outputPath);
    }
}
