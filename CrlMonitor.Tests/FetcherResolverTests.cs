using CrlMonitor.Fetching;

namespace CrlMonitor.Tests;

/// <summary>
/// Verifies fetcher resolver logic.
/// </summary>
public static class FetcherResolverTests
{
    private static readonly string[] HttpSchemes = ["http", "https"];
    private static readonly string[] LdapSchemes = ["ldap"];

    /// <summary>
    /// Ensures matching schemes return the registered fetcher.
    /// </summary>
    [Fact]
    public static void ResolveReturnsFetcher()
    {
        var fetcher = new StubFetcher();
        var mappings = new[]
        {
            new FetcherMapping(HttpSchemes, fetcher)
        };
        var resolver = new FetcherResolver(mappings);
        var uri = new Uri("https://example.com/a.crl");

        var resolved = resolver.Resolve(uri);

        Assert.Same(fetcher, resolved);
    }

    /// <summary>
    /// Ensures unsupported schemes trigger an error.
    /// </summary>
    [Fact]
    public static void ResolveThrowsForUnknownScheme()
    {
        var mappings = new[]
        {
            new FetcherMapping(LdapSchemes, new StubFetcher())
        };
        var resolver = new FetcherResolver(mappings);

        _ = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(new Uri("https://example.com/crl")));
    }

    private sealed class StubFetcher : ICrlFetcher
    {
        public Task<FetchedCrl> FetchAsync(CrlConfigEntry entry, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
