using Org.BouncyCastle.X509;
using CrlMonitor.Crl;
using Serilog;

namespace CrlMonitor.Validation;

internal sealed class CrlSignatureValidator : ICrlSignatureValidator
{
    private const long MaxCaCertificateBytes = 200 * 1024;

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

        var fileInfo = new FileInfo(entry.CaCertificatePath);
        if (fileInfo.Length > MaxCaCertificateBytes)
        {
            return SignatureValidationResult.Skipped("CA certificate exceeds 200 KB limit.");
        }

        try
        {
            var parser = new X509CertificateParser();
            var cert = parser.ReadCertificate(File.ReadAllBytes(entry.CaCertificatePath));
            if (cert == null)
            {
                Log.Error("CA certificate parsing failed for {CertPath}. File may be malformed or not in PEM/DER format.", entry.CaCertificatePath);
                return SignatureValidationResult.Failure("CA certificate could not be parsed. Ensure file is valid PEM or DER format.");
            }

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
