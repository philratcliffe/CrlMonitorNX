using System;
using System.Collections.Generic;
using System.Linq;
using CrlMonitor.Crl;
using Xunit;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;

namespace CrlMonitor.Tests;

/// <summary>
/// Verifies the basic behaviour of <see cref="CrlParser"/>.
/// </summary>
public static class CrlParserTests
{
    private static readonly string[] SampleRevokedSerials = { "01", "0A" };

    /// <summary>
    /// Ensures null inputs are rejected.
    /// </summary>
    [Fact]
    public static void ParseThrowsWhenBytesNull()
    {
        var parser = new CrlParser(SignatureValidationMode.None);
        Assert.Throws<ArgumentNullException>(() => parser.Parse(null!));
    }

    /// <summary>
    /// Ensures empty payloads surface a useful exception.
    /// </summary>
    [Fact]
    public static void ParseThrowsWhenBytesEmpty()
    {
        var parser = new CrlParser(SignatureValidationMode.None);
        Assert.Throws<ArgumentException>(() => parser.Parse(Array.Empty<byte>()));
    }

    /// <summary>
    /// Validates that metadata, serials, and delta status are populated.
    /// </summary>
    [Fact]
    public static void ParseEmitsMetadataAndSerials()
    {
        var thisUpdate = new DateTime(2024, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var nextUpdate = thisUpdate.AddDays(3);
        var crlBytes = CreateTestCrl(thisUpdate, nextUpdate, includeDeltaIndicator: true, SampleRevokedSerials);

        var parser = new CrlParser(SignatureValidationMode.None);
        var parsed = parser.Parse(crlBytes);

        Assert.Equal("CN=Test CA", parsed.Issuer);
        Assert.Equal(thisUpdate, parsed.ThisUpdate);
        Assert.Equal(nextUpdate, parsed.NextUpdate);
        var expectedSerials = NormaliseSerials(SampleRevokedSerials);
        var actualSerials = parsed.RevokedSerialNumbers.OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.Equal(expectedSerials, actualSerials);
        Assert.True(parsed.IsDelta);
        Assert.Equal("Skipped", parsed.SignatureStatus);
        Assert.Null(parsed.SignatureError);
    }

    /// <summary>
    /// Confirms signature status defaults when validation is enabled.
    /// </summary>
    [Fact]
    public static void ParseDefaultsSignatureStatusWhenValidationEnabled()
    {
        var thisUpdate = new DateTime(2024, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var crlBytes = CreateTestCrl(thisUpdate, thisUpdate.AddDays(1), includeDeltaIndicator: false);

        var parser = new CrlParser(SignatureValidationMode.CaCertificate);
        var parsed = parser.Parse(crlBytes);

        Assert.Equal("Unknown", parsed.SignatureStatus);
    }

    private static byte[] CreateTestCrl(
        DateTime thisUpdateUtc,
        DateTime nextUpdateUtc,
        bool includeDeltaIndicator,
        IReadOnlyList<string>? revokedSerialsHex = null)
    {
        var random = new SecureRandom();
        var keyPairGenerator = GeneratorUtilities.GetKeyPairGenerator("RSA");
        keyPairGenerator.Init(new KeyGenerationParameters(random, 2048));
        var keyPair = keyPairGenerator.GenerateKeyPair();

        var issuer = new X509Name("CN=Test CA");
        var certGenerator = new X509V3CertificateGenerator();
        certGenerator.SetSerialNumber(BigInteger.One);
        certGenerator.SetIssuerDN(issuer);
        certGenerator.SetSubjectDN(issuer);
        certGenerator.SetNotBefore(thisUpdateUtc.AddDays(-1));
        certGenerator.SetNotAfter(nextUpdateUtc.AddYears(1));
        certGenerator.SetPublicKey(keyPair.Public);
        certGenerator.AddExtension(
            X509Extensions.BasicConstraints,
            true,
            new BasicConstraints(true));
        var signatureFactory = new Asn1SignatureFactory("SHA256WITHRSA", keyPair.Private);
        var issuerCert = certGenerator.Generate(signatureFactory);

        var crlGenerator = new X509V2CrlGenerator();
        crlGenerator.SetIssuerDN(issuerCert.SubjectDN);
        crlGenerator.SetThisUpdate(thisUpdateUtc);
        crlGenerator.SetNextUpdate(nextUpdateUtc);

        if (revokedSerialsHex != null)
        {
            foreach (var serialHex in revokedSerialsHex)
            {
                var serial = new BigInteger(serialHex, 16);
                crlGenerator.AddCrlEntry(serial, thisUpdateUtc, CrlReason.PrivilegeWithdrawn);
            }
        }

        if (includeDeltaIndicator)
        {
            crlGenerator.AddExtension(
                X509Extensions.DeltaCrlIndicator,
                false,
                new DerInteger(BigInteger.One));
        }

        var crl = crlGenerator.Generate(signatureFactory);
        return crl.GetEncoded();
    }

    private static List<string> NormaliseSerials(IEnumerable<string> serials)
    {
        var formatted = new List<string>();
        foreach (var serial in serials)
        {
            var normalised = new BigInteger(serial, 16).ToString(16).ToUpperInvariant();
            formatted.Add(normalised);
        }

        formatted.Sort(StringComparer.Ordinal);
        return formatted;
    }
}
