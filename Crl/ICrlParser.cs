namespace CrlMonitor.Crl;

internal interface ICrlParser
{
    ParsedCrl Parse(byte[] crlBytes);
}
