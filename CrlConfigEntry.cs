using System;
using CrlMonitor.Crl;

namespace CrlMonitor;

internal sealed record CrlConfigEntry(
    Uri Uri,
    SignatureValidationMode SignatureValidationMode,
    string? CaCertificatePath,
    double ExpiryThreshold,
    LdapCredentials? Ldap);
