using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal sealed class HtmlReporter : IReporter
{
    private readonly string _outputPath;
    private readonly ReportingStatus _status;

    public HtmlReporter(string outputPath, ReportingStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        _outputPath = outputPath;
        _status = status ?? throw new ArgumentNullException(nameof(status));
    }

    public async Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken)
    {
        await HtmlReportWriter.WriteAsync(_outputPath, run, cancellationToken).ConfigureAwait(false);
        _status.RecordHtml(_outputPath);
    }
}
