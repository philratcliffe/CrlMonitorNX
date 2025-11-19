using CrlMonitor.Crl;
using CrlMonitor.Validation;
using CrlMonitor.Tests.TestUtilities;
using Org.BouncyCastle.X509;

namespace CrlMonitor.Tests;

/// <summary>
/// Validates the signature validator behaviour.
/// </summary>
public static class CrlSignatureValidatorTests
{
    /// <summary>
    /// Ensures validation can be skipped per config.
    /// </summary>
    [Fact]
    public static void ValidateReturnsSkippedWhenModeNone()
    {
        var (parsed, _, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        var validator = new CrlSignatureValidator();
        var entry = new CrlConfigEntry(new Uri("http://example.com"), SignatureValidationMode.None, null, 0.8, null, 10 * 1024 * 1024);

        var result = validator.Validate(parsed, entry);

        Assert.Equal("Skipped", result.Status);
    }

    /// <summary>
    /// Ensures valid signatures are accepted.
    /// </summary>
    [Fact]
    public static void ValidateAcceptsValidSignature()
    {
        var (parsed, caCert, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        using var temp = new TempFile(caCert.GetEncoded());
        var validator = new CrlSignatureValidator();
        var entry = new CrlConfigEntry(new Uri("http://example.com"), SignatureValidationMode.CaCertificate, temp.Path, 0.8, null, 10 * 1024 * 1024);

        var result = validator.Validate(parsed, entry);

        Assert.Equal("Valid", result.Status);
    }

    /// <summary>
    /// Ensures oversized CA certificates skip validation.
    /// </summary>
    [Fact]
    public static void ValidateSkipsWhenCaCertTooLarge()
    {
        var (parsed, _, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        var oversized = new byte[205_000];
        using var temp = new TempFile(oversized);
        var validator = new CrlSignatureValidator();
        var entry = new CrlConfigEntry(new Uri("http://example.com"), SignatureValidationMode.CaCertificate, temp.Path, 0.8, null, 10 * 1024 * 1024);

        var result = validator.Validate(parsed, entry);

        Assert.Equal("Skipped", result.Status);
        Assert.Contains("200 KB", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures invalid signatures are flagged.
    /// </summary>
    [Fact]
    public static void ValidateRejectsInvalidSignature()
    {
        var (parsed, caCert, _, _) = CrlTestBuilder.BuildParsedCrl(true);
        using var temp = new TempFile(caCert.GetEncoded());
        var validator = new CrlSignatureValidator();
        var entry = new CrlConfigEntry(new Uri("http://example.com"), SignatureValidationMode.CaCertificate, temp.Path, 0.8, null, 10 * 1024 * 1024);

        var result = validator.Validate(parsed, entry);

        Assert.Equal("Invalid", result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    /// <summary>
    /// Ensures real-world GlobalSign CRL validates with intermediate cert.
    /// </summary>
    [Fact]
    public static void ValidateGlobalSignCrlWithIntermediateCert()
    {
        var crlPath = Path.Combine(AppContext.BaseDirectory, "examples", "crls", "GlobalSignRSAOVSSLCA2018.crl");
        var certPath = Path.Combine(AppContext.BaseDirectory, "examples", "CA-certs", "GlobalSignRSAOVSSLCA2018.pem");

        if (!File.Exists(crlPath))
        {
            throw new FileNotFoundException($"Test CRL not found: {crlPath}");
        }

        if (!File.Exists(certPath))
        {
            throw new FileNotFoundException($"Test cert not found: {certPath}");
        }

        var crlBytes = File.ReadAllBytes(crlPath);
        var parser = new X509CrlParser();
        var rawCrl = parser.ReadCrl(crlBytes);

        var revokedCerts = rawCrl.GetRevokedCertificates();
        var serialNumbers = revokedCerts != null
            ? revokedCerts.Cast<Org.BouncyCastle.X509.X509CrlEntry>()
                .Select(entry => entry.SerialNumber.ToString(16).ToUpperInvariant())
                .ToList()
            : [];

        var parsed = new ParsedCrl(
            rawCrl.IssuerDN.ToString(),
            rawCrl.ThisUpdate,
            rawCrl.NextUpdate?.ToUniversalTime(),
            serialNumbers,
            false,
            "Pending",
            null,
            rawCrl);

        var validator = new CrlSignatureValidator();
        var entry = new CrlConfigEntry(
            new Uri("http://crl.globalsign.com/gsrsaovsslca2018.crl"),
            SignatureValidationMode.CaCertificate,
            certPath,
            0.8,
            null,
            10 * 1024 * 1024);

        var result = validator.Validate(parsed, entry);

        Assert.Equal("Valid", result.Status);
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; }

        public TempFile(byte[] content)
        {
            this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            File.WriteAllBytes(this.Path, content);
        }

        public void Dispose()
        {
            try
            {
                File.Delete(this.Path);
            }
            catch (IOException)
            {
            }
        }
    }
}
