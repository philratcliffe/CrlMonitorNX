using CrlMonitor.Crl;

namespace CrlMonitor.Validation;

internal interface ICrlSignatureValidator
{
    SignatureValidationResult Validate(ParsedCrl parsedCrl, CrlConfigEntry entry);
}
