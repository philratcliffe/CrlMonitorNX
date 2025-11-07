# Certificate Installation for Signature Validation

CRL signature validation requires CA certificates in system stores.

## Platform Status

### Windows
✅ **No installation required for major public CAs**

Windows includes root certificates for all major public CAs by default:
- DigiCert (all roots)
- GlobalSign (all roots)
- Google Trust Services
- Let's Encrypt
- And many others

**Tested and working without manual installation:**
- DigiCert Global Root CA
- GlobalSign Root R1 (chains to intermediate "GlobalSign RSA OV SSL CA 2018")
- Google Trust Services WE1

### macOS
❌ **Signature validation not functional**

.NET on macOS cannot access certificates installed via the `security` command:
- .NET only reads Apple's built-in `SystemRootCertificates.keychain`
- Certificates installed to `System.keychain` or `login.keychain` are not visible
- This is a .NET/macOS integration limitation

**Workaround:** Test signature validation on Windows or Linux.

### Linux
✅ **Works with system ca-certificates**

.NET reads from standard Linux certificate paths:
- `/etc/ssl/certs/`
- `/usr/local/share/ca-certificates/`

Install CA certificates using distribution package manager (e.g., `ca-certificates` package).

## Manual Installation (Private/Custom CAs Only)

Only needed for private/internal CAs not included in OS defaults.

### Security Note

**Trust Anchors (Root CAs)**: Must be obtained securely and verified (hash/fingerprint) before installation as trusted roots.

**Intermediate CAs**: Install in intermediate store, NOT as trusted roots. Chain builds to trust anchor.

### Windows - Trust Anchor (Root CA)

```powershell
# Download certificate
curl -o custom-root-ca.crt https://example.com/custom-root-ca.crt

# Verify fingerprint (compare with official source)
Get-FileHash -Algorithm SHA256 custom-root-ca.crt

# Import to Trusted Root Certification Authorities
Import-Certificate -FilePath custom-root-ca.crt -CertStoreLocation Cert:\LocalMachine\Root
```

### Windows - Intermediate CA

```powershell
# Import to Intermediate Certification Authorities (NOT Root)
Import-Certificate -FilePath custom-intermediate-ca.crt -CertStoreLocation Cert:\LocalMachine\CA
```

### Linux - CA Installation

```bash
# Download and verify certificate
curl -o custom-root-ca.crt https://example.com/custom-root-ca.crt
openssl x509 -noout -fingerprint -sha256 -in custom-root-ca.crt
# Compare fingerprint with official source

# Install to ca-certificates
sudo cp custom-root-ca.crt /usr/local/share/ca-certificates/
sudo update-ca-certificates
```

## Troubleshooting

### Enable Logging
Create `enable-logging.txt` in the same directory as CrlMonitor.exe to see:
- Which certificate stores are searched
- Where issuer certificates are found
- Certificate chain details (intermediates and trust anchors)
- Signature verification results

**Example log output showing successful validation:**

**DigiCert Global Root CA (self-signed root):**
```
[DBG] Searching store: "CurrentUser"/"Root"
[INF] ✓ Found issuer cert by CN in "CurrentUser"/"Root"
[INF]   Subject: CN=DigiCert Global Root CA, OU=www.digicert.com, O=DigiCert Inc, C=US
[INF]   Thumbprint: A8985D3A65E5E5C4B2D7D66D40C6DD2FB19C5436
[INF] ✓ Certificate chain validated successfully
[INF] Certificate chain (1 certificate):
[INF]   [0] TRUST ANCHOR: CN=DigiCert Global Root CA, OU=www.digicert.com, O=DigiCert Inc, C=US
[DBG]       Valid: "2006-11-10" to "2031-11-10"
[INF] ✓ CRL signature verified successfully
```

**GlobalSign RSA OV SSL CA 2018 (intermediate → root):**
```
[DBG] Searching store: "CurrentUser"/"CertificateAuthority"
[INF] ✓ Found issuer cert by CN in "CurrentUser"/"CertificateAuthority"
[INF]   Subject: CN=GlobalSign RSA OV SSL CA 2018, O=GlobalSign nv-sa, C=BE
[INF]   Thumbprint: DFE83023062B997682708B4EAB8E819AFF5D9775
[INF] ✓ Certificate chain validated successfully
[INF] Certificate chain (2 certificates):
[INF]   [0] Intermediate: CN=GlobalSign RSA OV SSL CA 2018, O=GlobalSign nv-sa, C=BE
[INF]   [1] TRUST ANCHOR: CN=GlobalSign, O=GlobalSign, OU=GlobalSign Root CA - R3
[INF] ✓ CRL signature verified successfully
```

**Google WE1 (intermediate → GlobalSign root):**
```
[DBG] Searching store: "CurrentUser"/"CertificateAuthority"
[INF] ✓ Found issuer cert by CN in "CurrentUser"/"CertificateAuthority"
[INF]   Subject: CN=WE1, O=Google Trust Services, C=US
[INF]   Thumbprint: 4180DAE3D4F9B3B0AAEEBF2A9A71332689832884
[INF] ✓ Certificate chain validated successfully
[INF] Certificate chain (2 certificates):
[INF]   [0] Intermediate: CN=WE1, O=Google Trust Services, C=US
[INF]   [1] TRUST ANCHOR: CN=GlobalSign, O=GlobalSign, OU=GlobalSign ECC Root CA - R4
[INF] ✓ CRL signature verified successfully
```

**Key observations from Windows testing:**
- All certificates found in **CurrentUser** stores (Root or CertificateAuthority)
- LocalMachine stores not needed (Windows populates both)
- DigiCert: 1-certificate chain (self-signed root)
- GlobalSign intermediate: 2-certificate chain to GlobalSign Root CA - R3
- Google WE1: 2-certificate chain to GlobalSign ECC Root CA - R4

### Check Certificate Presence (Windows)

```powershell
# List all root certificates
Get-ChildItem Cert:\LocalMachine\Root | Select-Object Subject, Thumbprint

# Search for specific CA
Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -like "*DigiCert*" }
```

### Common Issues

**"Issuer cert not found"**: Certificate not in any searched store (My, CA, Root in CurrentUser/LocalMachine)

**Chain validation failed**: Intermediate certificate missing or trust anchor not installed

**macOS always fails**: Expected - .NET cannot access macOS keychains. Use Windows or Linux for testing.

## Certificate Sources

- DigiCert: https://www.digicert.com/kb/digicert-root-certificates.htm
- GlobalSign: https://support.globalsign.com/ca-certificates/root-certificates
- Let's Encrypt: https://letsencrypt.org/certificates/
- Always verify fingerprints before trusting root CAs
