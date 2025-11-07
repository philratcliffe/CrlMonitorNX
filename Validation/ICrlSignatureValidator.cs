using CrlMonitor.Crl;
using CrlMonitor.Models;

namespace CrlMonitor.Validation;

internal interface ICrlSignatureValidator
{
    SignatureValidationResult Validate(ParsedCrl parsedCrl, CrlConfigEntry entry);
}
