using CrlMonitor.Models;

namespace CrlMonitor.Reporting;

internal sealed class CompositeReporter(IReadOnlyList<IReporter> reporters) : IReporter
{
    private readonly IReadOnlyList<IReporter> _reporters = reporters ?? throw new ArgumentNullException(nameof(reporters));

    public async Task ReportAsync(CrlCheckRun run, CancellationToken cancellationToken)
    {
        foreach (var reporter in this._reporters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await reporter.ReportAsync(run, cancellationToken).ConfigureAwait(false);
        }
    }
}
