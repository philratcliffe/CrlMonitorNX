namespace CrlMonitor.Fetching;

internal sealed class LdapCrlFetcher(ILdapConnectionFactory connectionFactory) : ICrlFetcher
{
    private const string AttributeName = "certificateRevocationList;binary";
    private readonly ILdapConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    public Task<FetchedCrl> FetchAsync(CrlConfigEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!IsLdap(entry.Uri))
        {
            throw new InvalidOperationException("LDAP fetcher only supports ldap/ldaps URIs.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var connection = this._connectionFactory.Open(entry.Uri, entry.Ldap);
        var distinguishedName = BuildDistinguishedName(entry.Uri);
        var start = DateTime.UtcNow;
        var values = connection.GetAttributeValues(distinguishedName, AttributeName);
        if (values.Length == 0)
        {
            throw new InvalidOperationException($"LDAP entry '{distinguishedName}' does not contain {AttributeName}.");
        }

        var crlBytes = values[0];
        if (crlBytes.LongLength > entry.MaxCrlSizeBytes)
        {
            throw new CrlTooLargeException(entry.Uri, entry.MaxCrlSizeBytes, crlBytes.LongLength);
        }
        var elapsed = DateTime.UtcNow - start;
        return Task.FromResult(new FetchedCrl(crlBytes, elapsed, crlBytes.Length));
    }

    private static bool IsLdap(Uri uri)
    {
        return uri.Scheme.Equals("ldap", StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildDistinguishedName(Uri uri)
    {
        var path = uri.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("LDAP URI must include a distinguished name.");
        }

        var dn = path.Trim('/');
        return string.IsNullOrWhiteSpace(dn)
            ? throw new InvalidOperationException("LDAP URI must include a distinguished name.")
            : Uri.UnescapeDataString(dn);
    }
}
