namespace CrlMonitor.State;

internal interface IStateStore
{
    Task<DateTime?> GetLastFetchAsync(Uri uri, CancellationToken cancellationToken);

    Task SaveLastFetchAsync(Uri uri, DateTime fetchedAtUtc, CancellationToken cancellationToken);

    Task<DateTime?> GetLastReportSentAsync(CancellationToken cancellationToken);

    Task SaveLastReportSentAsync(DateTime sentAtUtc, CancellationToken cancellationToken);

    Task<DateTime?> GetAlertCooldownAsync(string key, CancellationToken cancellationToken);

    Task SaveAlertCooldownAsync(string key, DateTime triggeredAtUtc, CancellationToken cancellationToken);
}
