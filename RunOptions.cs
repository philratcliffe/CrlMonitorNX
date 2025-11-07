using System;
using System.Collections.Generic;

namespace CrlMonitor;

internal sealed record RunOptions(
    Uri UriListPath,
    Uri CsvOutputPath,
    bool CsvAppendTimestamp,
    string SignatureValidationMode,
    IReadOnlyDictionary<string, string> UriSpecificCaCertificates);
