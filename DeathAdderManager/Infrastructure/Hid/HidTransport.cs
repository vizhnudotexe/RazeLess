using HidSharp;
using DeathAdderManager.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Infrastructure.Hid;

/// <summary>
/// Concrete IHidTransport that writes/reads feature reports via HidSharp.
/// HidSharp calls SetupDiGetClassDevs / HidD_GetFeature under the hood on Windows.
/// </summary>
public sealed class HidTransport : IHidTransport
{
    private readonly HidStream                _stream;
    private readonly ILogger<HidTransport>   _logger;
    private bool                              _disposed;

    private const int FeatureReportSize = 91;

    public bool IsOpen => !_disposed && _stream.CanWrite;

    public HidTransport(HidStream stream, ILogger<HidTransport> logger)
    {
        _stream               = stream;
        _logger               = logger;
        _stream.ReadTimeout   = 500;
        _stream.WriteTimeout  = 500;
    }

    public Task<bool> SendFeatureReportAsync(byte[] packet, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (packet.Length != FeatureReportSize)
        {
            _logger.LogError("Invalid packet size {Size}, expected {Expected}", packet.Length, FeatureReportSize);
            return Task.FromResult(false);
        }

        _logger.LogWarning(">>> HID SEND: Class=0x{PClass:X2} Cmd=0x{PCmd:X2} CRC=0x{CRC:X2} TransID=0x{TransID:X2}",
            packet[7], packet[8], packet[88], packet[2]);
        // Full hex dump — remove once DPI is confirmed working
        _logger.LogWarning("    BYTES: {Hex}", BitConverter.ToString(packet, 0, 20));

        return Task.Run(() =>
        {
            try
            {
                lock (_stream)
                {
                    // SetFeature is the correct call for Razer HID feature reports (USB HID 0x09 SET_REPORT).
                    // Do NOT fall back to Write() — that sends an Output report, not a Feature report.
                    _stream.SetFeature(packet);
                }
                Thread.Sleep(20);  // Give firmware time to process (was 5ms, now 20ms)
                _logger.LogWarning("<<< HID SEND OK");
                return true;
            }
            catch (ObjectDisposedException)
            {
                _logger.LogWarning("HID stream disposed during send");
                return false;
            }
            catch (Exception ex)
            {
                // This is the real error message — log it fully so we can diagnose
                _logger.LogError("HID SetFeature FAILED: {Error}", ex.Message);
                return false;
            }
        }, ct);
    }

    public Task<byte[]?> ReadFeatureReportAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Task.Run(() =>
        {
            try
            {
                var buf = new byte[FeatureReportSize];
                buf[0]  = RazerHidPacket.ReportId;
                lock (_stream)
                {
                    _stream.GetFeature(buf);
                }
                return (byte[]?)buf;
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("HID GetFeature timeout");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError("HID GetFeature FAILED: {Error}", ex.Message);
                return (byte[]?)null;
            }
        }, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Dispose();
    }
}

/// <summary>
/// Discovers and opens Razer DeathAdder Essential HID devices.
/// </summary>
public static class HidDeviceFactory
{
    public const int RazerVendorId           = 0x1532;
    public const int DeathAdderEssentialPid  = 0x0071; // Older model
    public const int DeathAdderEssential2021Pid = 0x0098; // 2021/White model

    public static IEnumerable<HidDevice> EnumerateDeathAdderDevices()
    {
        var allRazer = DeviceList.Local.GetHidDevices(vendorID: RazerVendorId);
        return allRazer
            .Where(d => d.ProductID == DeathAdderEssentialPid || d.ProductID == DeathAdderEssential2021Pid);
    }

    public static IEnumerable<HidDevice> EnumerateConfigInterfaces()
    {
        var devs = EnumerateDeathAdderDevices().ToList();
        
        // Priority 1: Devices with MaxFeatureReportLength == 91 (vendor-defined collection)
        // This is the correct interface for Razer 91-byte protocol packets.
        var preferred = devs.Where(d => d.GetMaxFeatureReportLength() == 91).ToList();
        
        // Priority 2: mi_00 (main config interface) if not already in preferred
        var mi00 = devs.Where(d => d.DevicePath.Contains("mi_00") && !d.DevicePath.Contains("col")).Except(preferred).ToList();
        
        // Priority 3: mi_01 non-keyboard
        var mi01 = devs.Where(d => d.DevicePath.Contains("mi_01") && !d.DevicePath.EndsWith("\\kbd") && !d.DevicePath.Contains("col")).Except(preferred).ToList();
        
        // Priority 4: fall back to any remaining
        var rest = devs.Except(preferred).Except(mi00).Except(mi01);
        
        // Return preferred first, then mi00, then mi01, then rest
        foreach (var d in preferred) yield return d;
        foreach (var d in mi00) yield return d;
        foreach (var d in mi01) yield return d;
        foreach (var d in rest) yield return d;
    }

    public static HidStream? OpenConfigInterface(HidDevice device, ILogger logger)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var config = new OpenConfiguration();
                config.SetOption(OpenOption.Exclusive, true);
                var stream = device.Open(config);
                logger.LogInformation("Opened CONFIG (exclusive): {Path}", device.DevicePath);
                return stream;
            }
            catch (Exception ex)
            {
                try
                {
                    var config2 = new OpenConfiguration();
                    config2.SetOption(OpenOption.Exclusive, false);
                    var stream2 = device.Open(config2);
                    logger.LogInformation("Opened CONFIG (non-exclusive): {Path}", device.DevicePath);
                    return stream2;
                }
                catch { }
                logger.LogWarning("Open failed: {Error}", ex.Message);
                if (attempt < 2) Thread.Sleep(50);
            }
        }
        return null;
    }

    public static HidStream? Open(HidDevice device, ILogger logger)
    {
        return OpenConfigInterface(device, logger);
    }
}

/// <summary>
/// Equality comparer for HidDevice by device path.
/// </summary>
internal sealed class DevicePathComparer : IEqualityComparer<HidDevice>
{
    public static readonly DevicePathComparer Instance = new();
    public bool Equals(HidDevice? x, HidDevice? y)  => x?.DevicePath == y?.DevicePath;
    public int  GetHashCode(HidDevice obj)            => obj.DevicePath.GetHashCode();
}
