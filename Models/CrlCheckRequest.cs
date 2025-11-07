using System;
using CrlMonitor.Crl;

namespace CrlMonitor.Models;

internal sealed record CrlCheckRequest(
    Uri Uri,
    SignatureValidationMode SignatureValidationMode,
    string? CaCertificatePath,
    double ExpiryThreshold,
    LdapCredentials? Ldap);
