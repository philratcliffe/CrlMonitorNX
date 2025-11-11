using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CrlMonitor.Eula;

internal static class EulaMetadataProvider
{
    private const string ResourceName = "CrlMonitor.EULA.txt";

    private static readonly char[] LineSeparators = ['\r', '\n'];

    public static EulaMetadata GetMetadata()
    {
        var text = ReadEmbeddedEula();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var hash = Convert.ToHexString(hashBytes);
        var version = ExtractField(text, "Version:") ?? "Unknown";
        var effectiveDate = ExtractField(text, "Effective Date:") ?? "Unknown";
        return new EulaMetadata(text, hash, version, effectiveDate);
    }

    private static string ReadEmbeddedEula()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: false);
        return reader.ReadToEnd();
    }

    private static string? ExtractField(string text, string fieldName)
    {
        var lines = text.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith(fieldName, StringComparison.OrdinalIgnoreCase))
            {
                var value = line[fieldName.Length..].Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }
}
