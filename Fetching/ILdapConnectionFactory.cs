namespace CrlMonitor.Fetching;

internal interface ILdapConnectionFactory
{
    ILdapConnection Open(Uri uri, LdapCredentials? credentials);
}
