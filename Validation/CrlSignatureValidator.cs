using System;
using System.IO;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Security.Certificates;
using Org.BouncyCastle.X509;
using CrlMonitor.Crl;
using CrlMonitor.Models;

namespace CrlMonitor.Validation;

internal sealed class CrlSignatureValidator : ICrlSignatureValidator
{
    public SignatureValidationResult Validate(ParsedCrl parsedCrl, CrlConfigEntry entry)
    {
        ArgumentNullException.ThrowIfNull(parsedCrl);
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.SignatureValidationMode == SignatureValidationMode.None)
        {
            return SignatureValidationResult.Skipped("Signature validation disabled.");
        }

        if (string.IsNullOrWhiteSpace(entry.CaCertificatePath))
        {
            return SignatureValidationResult.Failure("CA certificate path not specified.");
        }

        if (!File.Exists(entry.CaCertificatePath))
        {
            return SignatureValidationResult.Failure("CA certificate file not found.");
        }

        try
        {
            var parser = new X509CertificateParser();
            var cert = parser.ReadCertificate(File.ReadAllBytes(entry.CaCertificatePath));
            parsedCrl.RawCrl.Verify(cert.GetPublicKey());
            return SignatureValidationResult.Valid();
        }
        catch (Org.BouncyCastle.Security.InvalidKeyException ex)
        {
            return SignatureValidationResult.Invalid(ex.Message);
        }
        catch (Org.BouncyCastle.Security.Certificates.CrlException ex)
        {
            return SignatureValidationResult.Invalid(ex.Message);
        }
        catch (IOException ex)
        {
            return SignatureValidationResult.Failure(ex.Message);
        }
    }
}
