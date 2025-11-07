using System;
using System.Net;
using System.DirectoryServices.Protocols;

namespace CrlMonitor.Fetching;

internal sealed class SystemLdapConnectionFactory : ILdapConnectionFactory
{
    public ILdapConnection Open(Uri uri, LdapCredentials? credentials)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var identifier = CreateIdentifier(uri);
        var connection = new LdapConnection(identifier)
        {
            AuthType = credentials != null ? AuthType.Negotiate : AuthType.Anonymous
        };

        if (credentials != null)
        {
            connection.Credential = new NetworkCredential(credentials.Username, credentials.Password);
        }

        if (uri.Scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase))
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }

        return new SystemLdapConnection(connection);
    }

    private static LdapDirectoryIdentifier CreateIdentifier(Uri uri)
    {
        var port = uri.Port > 0 ? uri.Port : (uri.Scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase) ? 636 : 389);
        return new LdapDirectoryIdentifier(uri.Host, port);
    }

    private sealed class SystemLdapConnection : ILdapConnection
    {
        private readonly LdapConnection _connection;

        public SystemLdapConnection(LdapConnection connection)
        {
            _connection = connection;
        }

        public byte[][] GetAttributeValues(string distinguishedName, string attributeName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(distinguishedName);
            ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);

            var request = new SearchRequest(distinguishedName, "(objectClass=*)", SearchScope.Base, attributeName);
            var response = (SearchResponse)_connection.SendRequest(request);
            if (response.Entries.Count == 0)
            {
                return Array.Empty<byte[]>();
            }

            var entry = response.Entries[0];
            var attribute = entry.Attributes[attributeName];
            if (attribute == null || attribute.Count == 0)
            {
                return Array.Empty<byte[]>();
            }

            var result = new byte[attribute.Count][];
            for (var i = 0; i < attribute.Count; i++)
            {
                result[i] = (byte[])attribute[i];
            }

            return result;
        }

        public void Dispose()
        {
            _connection.Dispose();
        }
    }
}
