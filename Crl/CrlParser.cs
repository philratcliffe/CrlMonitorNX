using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.X509;

namespace CrlMonitor.Crl;

internal sealed class CrlParser(SignatureValidationMode validationMode) : ICrlParser
{
    private readonly SignatureValidationMode _validationMode = validationMode;

    public ParsedCrl Parse(byte[] crlBytes)
    {
        ArgumentNullException.ThrowIfNull(crlBytes);
        if (crlBytes.Length == 0)
        {
            throw new ArgumentException("CRL data is empty", nameof(crlBytes));
        }

        var parser = new X509CrlParser();
        var crl = parser.ReadCrl(crlBytes) ?? throw new InvalidOperationException("Failed to parse CRL");

        var issuer = crl.IssuerDN.ToString();
        var thisUpdate = ToUtc(crl.ThisUpdate);
        var nextUpdate = crl.NextUpdate.HasValue ? (DateTime?)ToUtc(crl.NextUpdate.Value) : null;
        var revoked = ExtractRevokedSerials(crl);
        var isDelta = crl.GetExtensionValue(X509Extensions.DeltaCrlIndicator) != null;

        var signatureStatus = this._validationMode == SignatureValidationMode.None ? "Skipped" : "Unknown";

        return new ParsedCrl(issuer, thisUpdate, nextUpdate, revoked, isDelta, signatureStatus, null, crl);
    }

    private static List<string> ExtractRevokedSerials(X509Crl crl)
    {
        var revoked = new List<string>();
        var entries = crl.GetRevokedCertificates();
        if (entries == null)
        {
            return revoked;
        }

        foreach (var entry in entries)
        {
            revoked.Add(entry.SerialNumber.ToString(16).ToUpperInvariant());
        }

        return revoked;
    }

    private static DateTime ToUtc(DateTime value)
    {
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
