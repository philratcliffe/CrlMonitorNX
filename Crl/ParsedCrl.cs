using System;
using System.Collections.Generic;

namespace CrlMonitor.Crl;

internal sealed record ParsedCrl(
    string Issuer,
    DateTime ThisUpdate,
    DateTime? NextUpdate,
    IReadOnlyList<string> RevokedSerialNumbers,
    bool IsDelta,
    string SignatureStatus,
    string? SignatureError);
