using System;
using System.IO;
using CrlMonitor.Crl;
using CrlMonitor.Models;
using CrlMonitor.Validation;
using CrlMonitor.Tests.TestUtilities;
using Xunit;

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
        var (parsed, _, _) = CrlTestBuilder.BuildParsedCrl(false);
        var validator = new CrlSignatureValidator();
        var entry = new CrlConfigEntry(new Uri("http://example.com"), SignatureValidationMode.None, null, 0.8, null);

        var result = validator.Validate(parsed, entry);

        Assert.Equal("Skipped", result.Status);
    }

    /// <summary>
    /// Ensures valid signatures are accepted.
    /// </summary>
    [Fact]
    public static void ValidateAcceptsValidSignature()
    {
        var (parsed, caCert, _) = CrlTestBuilder.BuildParsedCrl(false);
        using var temp = new TempFile(caCert.GetEncoded());
        var validator = new CrlSignatureValidator();
        var entry = new CrlConfigEntry(new Uri("http://example.com"), SignatureValidationMode.CaCertificate, temp.Path, 0.8, null);

        var result = validator.Validate(parsed, entry);

        Assert.Equal("Valid", result.Status);
    }

    /// <summary>
    /// Ensures invalid signatures are flagged.
    /// </summary>
    [Fact]
    public static void ValidateRejectsInvalidSignature()
    {
        var (parsed, caCert, _) = CrlTestBuilder.BuildParsedCrl(true);
        using var temp = new TempFile(caCert.GetEncoded());
        var validator = new CrlSignatureValidator();
        var entry = new CrlConfigEntry(new Uri("http://example.com"), SignatureValidationMode.CaCertificate, temp.Path, 0.8, null);

        var result = validator.Validate(parsed, entry);

        Assert.Equal("Invalid", result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; }

        public TempFile(byte[] content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            File.WriteAllBytes(Path, content);
        }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
            }
        }
    }
}
