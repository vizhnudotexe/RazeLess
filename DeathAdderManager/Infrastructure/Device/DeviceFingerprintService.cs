using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Text.Json;
using HidSharp;
using DeathAdderManager.Core.Domain.Models;
using DeathAdderManager.Core.Interfaces;
using DeathAdderManager.Infrastructure.Hid;
using DeathAdderManager.Shared;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Infrastructure.Device;

/// <summary>
/// Computes a stable fingerprint for each physical DeathAdder Essential.
/// Strategy (in priority order):
///   1. Serial number from HID descriptor (often empty)
///   2. USB device instance path (stable per port per device on Windows)
///   3. VID+PID as fallback
/// </summary>
public sealed class DeviceFingerprintService : IDeviceFingerprintService
{
    private readonly ILogger<DeviceFingerprintService> _logger;
    private readonly string _fingerprintStorePath;
    private List<DeviceFingerprint>? _cachedKnownDevices;

    public DeviceFingerprintService(ILogger<DeviceFingerprintService> logger)
    {
        _logger = logger;
        _fingerprintStorePath = Path.Combine(AppDataFolder.Path, "known_devices.json");
    }

    public async Task<List<DeviceFingerprint>> DetectDevicesAsync()
    {
        var results = new List<DeviceFingerprint>();
        var devices = HidDeviceFactory.EnumerateDeathAdderDevices().ToList();

        _logger.LogInformation("Detected {Count} DeathAdder Essential device(s)", devices.Count);

        foreach (var dev in devices)
        {
            var fp = BuildFingerprint(dev);
            results.Add(fp);
            _logger.LogInformation("Device fingerprint: {Hash} path={Path}",
                fp.FingerprintHash[..Math.Min(12, fp.FingerprintHash.Length)], fp.DevicePath);
        }

        return results;
    }

    private DeviceFingerprint BuildFingerprint(HidDevice device)
    {
        string serial = string.Empty;
        try { serial = device.GetSerialNumber() ?? string.Empty; }
        catch { /* not all devices expose serial */ }

        string path = device.DevicePath;
        var raw  = $"{HidDeviceFactory.RazerVendorId:X4}:{HidDeviceFactory.DeathAdderEssentialPid:X4}:{serial}:{path}";
        var hash = ComputeHash(raw);

        return new DeviceFingerprint
        {
            VendorId        = HidDeviceFactory.RazerVendorId,
            ProductId       = HidDeviceFactory.DeathAdderEssentialPid,
            DevicePath      = path,
            SerialNumber    = serial,
            FingerprintHash = hash,
            DisplayName     = string.IsNullOrEmpty(serial)
                ? $"DeathAdder Essential ({hash[..6]})"
                : $"DeathAdder Essential (SN:{serial})",
            LastSeen        = DateTime.UtcNow,
        };
    }

    public async Task<DeviceFingerprint?> LookupKnownDeviceAsync(string fingerprintHash)
    {
        var known = await GetAllKnownDevicesAsync();
        return known.FirstOrDefault(d => d.FingerprintHash == fingerprintHash);
    }

    public async Task SaveFingerprintAsync(DeviceFingerprint fingerprint)
    {
        var all      = await GetAllKnownDevicesAsync();
        var existing = all.FirstOrDefault(d => d.FingerprintHash == fingerprint.FingerprintHash);

        if (existing != null)
        {
            existing.LastSeen      = fingerprint.LastSeen;
            existing.LastProfileId = fingerprint.LastProfileId;
        }
        else
        {
            all.Add(fingerprint);
        }

        await AtomicJsonWriteAsync(_fingerprintStorePath, all);
        _cachedKnownDevices = all;
    }

    public async Task<List<DeviceFingerprint>> GetAllKnownDevicesAsync()
    {
        if (_cachedKnownDevices != null)
            return _cachedKnownDevices;

        if (!File.Exists(_fingerprintStorePath))
        {
            _cachedKnownDevices = new List<DeviceFingerprint>();
            return _cachedKnownDevices;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_fingerprintStorePath);
            _cachedKnownDevices = JsonSerializer.Deserialize<List<DeviceFingerprint>>(json) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load known devices file — starting fresh");
            _cachedKnownDevices = new();
        }

        return _cachedKnownDevices;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static async Task AtomicJsonWriteAsync<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp  = path + ".tmp";
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}
