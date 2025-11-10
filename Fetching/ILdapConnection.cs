namespace CrlMonitor.Fetching;

internal interface ILdapConnection : IDisposable
{
    byte[][] GetAttributeValues(string distinguishedName, string attributeName);
}
