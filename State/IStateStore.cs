using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrlMonitor.State;

internal interface IStateStore
{
    Task<DateTime?> GetLastFetchAsync(Uri uri, CancellationToken cancellationToken);

    Task SaveLastFetchAsync(Uri uri, DateTime fetchedAtUtc, CancellationToken cancellationToken);
}
