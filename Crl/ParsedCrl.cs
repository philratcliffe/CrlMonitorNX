using Org.BouncyCastle.X509;

namespace CrlMonitor.Crl;

internal sealed record ParsedCrl(
    string Issuer,
    DateTime ThisUpdate,
    DateTime? NextUpdate,
    IReadOnlyList<string> RevokedSerialNumbers,
    bool IsDelta,
    string SignatureStatus,
    string? SignatureError,
    X509Crl RawCrl);
