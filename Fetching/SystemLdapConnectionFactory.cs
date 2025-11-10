using System.DirectoryServices.Protocols;
using System.Net;

namespace CrlMonitor.Fetching;

internal sealed class SystemLdapConnectionFactory : ILdapConnectionFactory
{
    public ILdapConnection Open(Uri uri, LdapCredentials? credentials)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return new SystemLdapConnection(uri, credentials);
    }

    private static LdapDirectoryIdentifier CreateIdentifier(Uri uri)
    {
        var port = uri.Port > 0 ? uri.Port : (uri.Scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase) ? 636 : 389);
        return new LdapDirectoryIdentifier(uri.Host, port);
    }

    private sealed class SystemLdapConnection : ILdapConnection
    {
        private readonly LdapConnection _connection;

        public SystemLdapConnection(Uri uri, LdapCredentials? credentials)
        {
            var identifier = CreateIdentifier(uri);
            this._connection = new LdapConnection(identifier) {
                AuthType = credentials != null ? AuthType.Negotiate : AuthType.Anonymous
            };

            if (credentials != null)
            {
                this._connection.Credential = new NetworkCredential(credentials.Username, credentials.Password);
            }

            if (uri.Scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase))
            {
                this._connection.SessionOptions.SecureSocketLayer = true;
            }
        }

        public byte[][] GetAttributeValues(string distinguishedName, string attributeName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(distinguishedName);
            ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);

            var request = new SearchRequest(distinguishedName, "(objectClass=*)", SearchScope.Base, attributeName);
            var response = (SearchResponse)this._connection.SendRequest(request);
            if (response.Entries.Count == 0)
            {
                return [];
            }

            var entry = response.Entries[0];
            var attribute = entry.Attributes[attributeName];
            if (attribute == null || attribute.Count == 0)
            {
                return [];
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
            this._connection.Dispose();
        }
    }
}
