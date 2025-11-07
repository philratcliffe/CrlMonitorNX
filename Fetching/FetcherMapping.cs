using System;
using System.Collections.Generic;

namespace CrlMonitor.Fetching;

internal sealed class FetcherMapping : IFetcherMapping
{
    public FetcherMapping(IEnumerable<string> schemes, ICrlFetcher fetcher)
    {
        Schemes = schemes ?? throw new ArgumentNullException(nameof(schemes));
        Fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
    }

    public IEnumerable<string> Schemes { get; }
    public ICrlFetcher Fetcher { get; }
}
