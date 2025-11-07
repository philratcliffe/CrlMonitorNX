using System;
using CrlMonitor.Crl;
using CrlMonitor.Models;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace CrlMonitor.Tests.TestUtilities;

internal static class CrlTestBuilder
{
    public static (ParsedCrl Parsed, X509Certificate CaCert, X509Certificate SignerCert) BuildParsedCrl(bool signWithDifferentKey)
    {
        var caKey = GenerateKeyPair();
        var caCert = GenerateCertificate("CN=CA", caKey, caKey.Public);

        AsymmetricCipherKeyPair signerKey;
        X509Certificate signerCert;
        if (signWithDifferentKey)
        {
            signerKey = GenerateKeyPair();
            signerCert = GenerateCertificate("CN=Alt", signerKey, signerKey.Public);
        }
        else
        {
            signerKey = caKey;
            signerCert = caCert;
        }

        var crlBytes = GenerateCrl(caCert, signerKey);
        var parser = new CrlParser(SignatureValidationMode.CaCertificate);
        var parsed = parser.Parse(crlBytes);
        return (parsed, caCert, signerCert);
    }

    private static AsymmetricCipherKeyPair GenerateKeyPair()
    {
        var generator = new RsaKeyPairGenerator();
        generator.Init(new Org.BouncyCastle.Crypto.Parameters.RsaKeyGenerationParameters(BigInteger.ValueOf(0x10001), new SecureRandom(), 2048, 12));
        return generator.GenerateKeyPair();
    }

    private static X509Certificate GenerateCertificate(string subject, AsymmetricCipherKeyPair issuerKey, AsymmetricKeyParameter subjectPublic)
    {
        var generator = new X509V3CertificateGenerator();
        generator.SetSerialNumber(BigInteger.ProbablePrime(120, new SecureRandom()));
        generator.SetIssuerDN(new X509Name(subject));
        generator.SetSubjectDN(new X509Name(subject));
        generator.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        generator.SetNotAfter(DateTime.UtcNow.AddYears(5));
        generator.SetPublicKey(subjectPublic);
        generator.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(true));
        var signatureFactory = new Asn1SignatureFactory("SHA256WITHRSA", issuerKey.Private);
        return generator.Generate(signatureFactory);
    }

    private static byte[] GenerateCrl(X509Certificate issuerCert, AsymmetricCipherKeyPair signingKey)
    {
        var generator = new X509V2CrlGenerator();
        generator.SetIssuerDN(issuerCert.SubjectDN);
        generator.SetThisUpdate(DateTime.UtcNow);
        generator.SetNextUpdate(DateTime.UtcNow.AddDays(7));
        var signatureFactory = new Asn1SignatureFactory("SHA256WITHRSA", signingKey.Private);
        var crl = generator.Generate(signatureFactory);
        return crl.GetEncoded();
    }
}
