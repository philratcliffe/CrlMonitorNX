using System.Text.Json.Serialization;

namespace CrlMonitor.Eula;

internal sealed class EulaAcceptanceRecord
{
    [JsonPropertyName("LicenseAccepted")]
    public bool LicenseAccepted { get; set; }

    [JsonPropertyName("AcceptedDate")]
    public DateTime AcceptedDate { get; set; }

    [JsonPropertyName("AcceptedLicenseHash")]
    public string? AcceptedLicenseHash { get; set; }

    [JsonPropertyName("AcceptedLicenseVersion")]
    public string? AcceptedLicenseVersion { get; set; }

    [JsonPropertyName("AcceptedLicenseEffectiveDate")]
    public string? AcceptedLicenseEffectiveDate { get; set; }

    [JsonPropertyName("AcceptanceMethod")]
    public string? AcceptanceMethod { get; set; }
}
