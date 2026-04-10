using System.Text.Json.Serialization;

namespace DeathAdderManager.Core.Domain.Models;

/// <summary>
/// Uniquely identifies one physical DeathAdder Essential unit.
/// Two DeathAdder Essentials attached at the same time produce two different fingerprints.
/// </summary>
public sealed class DeviceFingerprint
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>USB Vendor ID - always 0x1532 for Razer</summary>
    [JsonPropertyName("vid")]
    public int VendorId { get; set; }

    /// <summary>USB Product ID - 0x0071 for DeathAdder Essential</summary>
    [JsonPropertyName("pid")]
    public int ProductId { get; set; }

    /// <summary>
    /// Windows device instance path (e.g. HID\VID_1532&amp;PID_0071\7&amp;...).
    /// Stable for a given USB port + device.
    /// </summary>
    [JsonPropertyName("devicePath")]
    public string DevicePath { get; set; } = string.Empty;

    /// <summary>
    /// Serial number string if the device exposes one via the HID descriptor.
    /// Many DeathAdder Essentials return empty — do not rely on this alone.
    /// </summary>
    [JsonPropertyName("serialNumber")]
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>
    /// Composite fingerprint hash: SHA256(VID+PID+SerialNumber+DevicePath).
    /// Used as dictionary key for per-device profile association.
    /// </summary>
    [JsonPropertyName("fingerprintHash")]
    public string FingerprintHash { get; set; } = string.Empty;

    /// <summary>Friendly display name shown in UI</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "DeathAdder Essential";

    [JsonPropertyName("lastSeen")]
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>ID of the last profile applied to this physical device.</summary>
    [JsonPropertyName("lastProfileId")]
    public Guid? LastProfileId { get; set; }
}
