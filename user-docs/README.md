# CrlMonitor User Guide

## Overview
CrlMonitor fetches Certificate Revocation Lists (CRLs) over HTTP(S) or LDAP, validates their signatures, checks freshness, and reports the results to the console and CSV (`crl-report.csv`). The self-contained release bundles the runtime, default configuration, and example CA certificates so you can run it without installing additional .NET components.

## Running the Tool
1. Unzip the release archive; it includes `CrlMonitor`, `config.json`, and the `CA-certs/` folder.
2. From the unzipped directory, run:
   ```bash
   ./CrlMonitor config.json
   ```
   (On Windows: `CrlMonitor.exe config.json`)
3. `config.json` can be edited in place; CA certificate paths are relative (e.g., `CA-certs/DigiCertGlobalRootCA.crt`).

## Default Configuration Highlights
- Fetches three public HTTP CRLs (DigiCert, GlobalSign, Google).
- Includes a sample LDAP URI (`ldap://dc1.example.com/...`) that intentionally fails, useful for testing error handling.
- Signature validation is enabled (`"ca-cert"`) where CA certificate files are available; otherwise `"none"` skips validation.
- `expiry_threshold` governs when a CRL moves from `Healthy` to `Expiring` (default 0.8 → 80% of validity window).

## Interpreting Output
### Console
Each line shows overall `status` (`OK` or `ERROR`), signature state, and health state:
```
OK http://crl.example.com [signature: Valid] [health: Healthy]
ERROR ldap://... [signature: Skipped :: No CA] [health: Unknown] :: Could not connect to LDAP host ...
```
Warnings (state/signature/runtime) print below.

### CSV (`crl-report.csv`)
Columns:
- `uri`: CRL endpoint.
- `status`: `OK` or `ERROR`.
- `signature_status` / `signature_error`: `Valid`, `Skipped`, `Invalid`, or `Error` with details.
- `health_status`: `Healthy`, `Expiring`, `Expired`, or `Unknown`).
- `duration_ms`: Fetch+parse duration.
- `error`: Combined message when status is `ERROR`.

## Troubleshooting
- **Could not connect to LDAP host**: URI points at a non-existent LDAP server; verify hostname/credentials.
- **Signature validation disabled**: Set `signature_validation_mode` to `"ca-cert"` and provide `ca_certificate_path`.
- **Expired/Expiring**: Adjust `expiry_threshold` or investigate whether publisher is updating CRLs on time.

