using System;
using System.Collections.Generic;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Utilities.Date;
using Org.BouncyCastle.X509;

namespace CrlMonitor.Crl;

internal sealed class CrlParser
{
    private readonly SignatureValidationMode _validationMode;

    public CrlParser(SignatureValidationMode validationMode)
    {
        _validationMode = validationMode;
    }

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
        DateTime? nextUpdate = crl.NextUpdate != null ? ToUtc(crl.NextUpdate) : null;
        var revoked = ExtractRevokedSerials(crl);
        var isDelta = crl.GetExtensionValue(X509Extensions.DeltaCrlIndicator) != null;

        var signatureStatus = _validationMode == SignatureValidationMode.None ? "Skipped" : "Unknown";

        return new ParsedCrl(issuer, thisUpdate, nextUpdate, revoked, isDelta, signatureStatus, null);
    }

    private static List<string> ExtractRevokedSerials(X509Crl crl)
    {
        var revoked = new List<string>();
        var entries = crl.GetRevokedCertificates();
        if (entries == null)
        {
            return revoked;
        }

        foreach (X509CrlEntry entry in entries)
        {
            revoked.Add(entry.SerialNumber.ToString(16).ToUpperInvariant());
        }

        return revoked;
    }

    private static DateTime ToUtc(object source)
    {
        return source switch
        {
            DateTimeObject dto => DateTime.SpecifyKind(dto.Value, DateTimeKind.Utc),
            DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
            _ => throw new InvalidOperationException("Unknown CRL timestamp representation encountered.")
        };
    }
}
