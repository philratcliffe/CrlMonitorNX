using System;
using System.Threading;
using System.Threading.Tasks;
using CrlMonitor.Crl;
using CrlMonitor.Fetching;
using Xunit;

namespace CrlMonitor.Tests;

/// <summary>
/// Covers LDAP fetcher behaviour with stubbed connections.
/// </summary>
public static class LdapCrlFetcherTests
{
    /// <summary>
    /// Ensures a successful LDAP fetch produces CRL bytes.
    /// </summary>
    [Fact]
    public static async Task FetchAsyncReturnsBytes()
    {
        var expected = new byte[] { 0, 1, 2 };
        var factory = new StubFactory(expected);
        var fetcher = new LdapCrlFetcher(factory);
        var entry = new CrlConfigEntry(new Uri("ldap://dc1.example.com/CN=Example,O=Corp"), SignatureValidationMode.None, null, 0.8, new LdapCredentials("user", "pw"), 10 * 1024 * 1024);

        var result = await fetcher.FetchAsync(entry, CancellationToken.None);

        Assert.Equal(expected, result.Content);
        Assert.Equal(expected.Length, result.ContentLength);
        Assert.Equal("CN=Example,O=Corp", factory.LastDistinguishedName);
    }

    /// <summary>
    /// Ensures missing attributes yield a validation exception.
    /// </summary>
    [Fact]
    public static async Task FetchAsyncThrowsWhenAttributeMissing()
    {
        var factory = new StubFactory(Array.Empty<byte[]>());
        var fetcher = new LdapCrlFetcher(factory);
        var entry = new CrlConfigEntry(new Uri("ldap://dc1.example.com/CN=Missing,O=Corp"), SignatureValidationMode.None, null, 0.8, null, 10 * 1024 * 1024);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fetcher.FetchAsync(entry, CancellationToken.None));
    }

    /// <summary>
    /// Ensures HTTP URIs are rejected by the LDAP fetcher.
    /// </summary>
    [Fact]
    public static async Task FetchAsyncRejectsNonLdap()
    {
        var factory = new StubFactory(Array.Empty<byte[]>());
        var fetcher = new LdapCrlFetcher(factory);
        var entry = new CrlConfigEntry(new Uri("http://example.com/crl"), SignatureValidationMode.None, null, 0.8, null, 10 * 1024 * 1024);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fetcher.FetchAsync(entry, CancellationToken.None));
    }

    /// <summary>
    /// Ensures oversized LDAP payloads are rejected.
    /// </summary>
    [Fact]
    public static async Task FetchAsyncThrowsWhenLdapPayloadTooLarge()
    {
        var factory = new StubFactory(new byte[] { 0, 1, 2 });
        var fetcher = new LdapCrlFetcher(factory);
        var entry = new CrlConfigEntry(new Uri("ldap://dc1.example.com/CN=Example,O=Corp"), SignatureValidationMode.None, null, 0.8, new LdapCredentials("user", "pw"), 2);

        await Assert.ThrowsAsync<CrlTooLargeException>(() => fetcher.FetchAsync(entry, CancellationToken.None));
    }

    private sealed class StubFactory : ILdapConnectionFactory
    {
        private readonly byte[][] _values;

        public StubFactory(byte[] singleValue)
        {
            _values = new[] { singleValue };
        }

        public StubFactory(byte[][] values)
        {
            _values = values;
        }

        public string? LastDistinguishedName { get; private set; }

        public ILdapConnection Open(Uri uri, LdapCredentials? credentials)
        {
            return new StubConnection(this, _values);
        }

        private sealed class StubConnection : ILdapConnection
        {
            private readonly StubFactory _owner;
            private readonly byte[][] _values;

            public StubConnection(StubFactory owner, byte[][] values)
            {
                _owner = owner;
                _values = values;
            }

            public byte[][] GetAttributeValues(string distinguishedName, string attributeName)
            {
                _owner.LastDistinguishedName = distinguishedName;
                return _values;
            }

            public void Dispose()
            {
            }
        }
    }
}
