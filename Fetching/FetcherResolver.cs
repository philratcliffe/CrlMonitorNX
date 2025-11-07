using System;
using System.Collections.Generic;

namespace CrlMonitor.Fetching;

internal sealed class FetcherResolver : IFetcherResolver
{
    private readonly Dictionary<string, ICrlFetcher> _fetchers;

    public FetcherResolver(IEnumerable<IFetcherMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        var map = new Dictionary<string, ICrlFetcher>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            foreach (var scheme in mapping.Schemes)
            {
                map[scheme] = mapping.Fetcher;
            }
        }

        _fetchers = map;
    }

    public ICrlFetcher Resolve(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (_fetchers.TryGetValue(uri.Scheme, out var fetcher))
        {
            return fetcher;
        }

        throw new InvalidOperationException($"No fetcher registered for scheme '{uri.Scheme}'.");
    }
}

internal interface IFetcherMapping
{
    IEnumerable<string> Schemes { get; }
    ICrlFetcher Fetcher { get; }
}
