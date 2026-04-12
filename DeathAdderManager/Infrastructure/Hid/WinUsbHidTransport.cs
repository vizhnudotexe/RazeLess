using LibUsbDotNet;
using LibUsbDotNet.Main;
using DeathAdderManager.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Infrastructure.Hid;

/// <summary>
/// IHidTransport implementation using LibUsbDotNet (WinUSB) instead of the
/// Windows HID stack. Routes ALL commands (DPI, polling rate, AND LED) through
/// raw USB control transfers — exactly as Razer Synapse's RZUDD.sys kernel driver does.
///
/// PREREQUISITE (one-time Zadig step):
///   Run Zadig → select "Razer DeathAdder Essential (Interface 0)" → WinUSB → Install.
///   This replaces the HID driver on mi_00. The PInvoke path then falls back gracefully.
///   Mouse movement still works via Interface 1 (unchanged).
///
/// Why this works for LED when HID doesn't:
///   The Windows HID stack processes SET_REPORT for class 0x03 (LED) and returns
///   Status=0x05 (Not Supported). Raw USB control transfers bypass this HID layer
///   entirely — the request goes straight to the device firmware, which processes it
///   and (assuming correct packet format) executes the LED change.
///
/// Control transfer wire format:
///   SET_REPORT: bmRequestType=0x21, bRequest=0x09, wValue=0x0300, wIndex=0, 90 bytes
///   GET_REPORT: bmRequestType=0xA1, bRequest=0x01, wValue=0x0300, wIndex=0, 90 bytes
///
/// Our packet builders produce 91 bytes (byte[0] = Windows HID Report ID = 0x00).
/// The Report ID is already encoded in wValue high byte (0x03=Feature, 0x00=ID).
/// We strip byte[0] before sending and prepend a dummy 0x00 after receiving.
/// </summary>
public sealed class WinUsbHidTransport : IHidTransport
{
    private const byte  HostToDevice_Class_Interface = 0x21; // SET_REPORT direction
    private const byte  DevToHost_Class_Interface    = 0xA1; // GET_REPORT direction
    private const byte  SetReport      = 0x09;
    private const byte  GetReport      = 0x01;
    private const short FeatureReport  = 0x0300; // Report Type=Feature(0x03), ID=0(0x00)
    private const short Interface0     = 0x0000;
    private const int   HidPacketLen   = 91;     // with Report ID prefix (from builders)
    private const int   UsbPayloadLen  = 90;     // without Report ID prefix (what firmware wants)

    private readonly UsbDevice                   _device;
    private readonly ILogger<WinUsbHidTransport> _logger;
    private          bool                        _disposed;

    public bool IsOpen => !_disposed;

    private WinUsbHidTransport(UsbDevice device, ILogger<WinUsbHidTransport> logger)
    {
        _device = device;
        _logger = logger;
    }

    /// <summary>
    /// Try to open the Razer device via WinUSB/LibUsbDotNet.
    /// Returns null if WinUSB driver is not installed on Interface 0 (Zadig not yet run).
    /// Returns a valid transport if found — use this to replace PInvokeHidTransport.
    /// </summary>
    public static WinUsbHidTransport? TryOpen(
        int                           vid,
        int                           pid,
        ILogger<WinUsbHidTransport>   logger)
    {
        try
        {
            var device = UsbDevice.OpenUsbDevice(new UsbDeviceFinder(vid, pid));
            if (device == null)
            {
                logger.LogDebug(
                    "[WinUsbHid] No WinUSB device found (VID=0x{V:X4} PID=0x{P:X4}). " +
                    "Run Zadig → Interface 0 → WinUSB to enable LED control.",
                    vid, pid);
                return null;
            }

            logger.LogInformation(
                "[WinUsbHid] WinUSB device opened: {Name}. DPI + LED will use raw USB control transfers.",
                device.Info?.ProductString ?? "Razer Device");

            return new WinUsbHidTransport(device, logger);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "[WinUsbHid] Open failed (WinUSB not set up yet — this is expected).");
            return null;
        }
    }

    /// <summary>
    /// Sends a 91-byte Razer packet via USB SET_REPORT control transfer.
    /// Byte[0] (Report ID) is stripped before sending.
    /// </summary>
    public Task<bool> SendFeatureReportAsync(byte[] packet, CancellationToken ct = default)
    {
        if (_disposed || packet.Length != HidPacketLen) return Task.FromResult(false);

        // Offload to thread pool — LibUsbDotNet ControlTransfer is synchronous
        return Task.Run(() =>
        {
            var payload = new byte[UsbPayloadLen];
            Array.Copy(packet, 1, payload, 0, UsbPayloadLen); // strip Report ID byte

            var setup = new UsbSetupPacket(
                HostToDevice_Class_Interface,
                SetReport,
                FeatureReport,
                Interface0,
                (short)UsbPayloadLen);

            try
            {
                bool ok = _device.ControlTransfer(ref setup, payload, UsbPayloadLen, out int n);
                _logger.LogDebug(
                    "[WinUsbHid] SET ok={Ok} bytes={N} Class=0x{C:X2} Cmd=0x{Cmd:X2}",
                    ok, n, packet[7], packet[8]);
                return ok && n == UsbPayloadLen;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[WinUsbHid] SET_REPORT exception Class=0x{C:X2} Cmd=0x{Cmd:X2}",
                    packet[7], packet[8]);
                return false;
            }
        }, ct);
    }

    /// <summary>
    /// Reads a 91-byte Razer response via USB GET_REPORT control transfer.
    /// Prepends a dummy Report ID byte (0x00) to match what callers expect.
    /// </summary>
    public Task<byte[]?> ReadFeatureReportAsync(CancellationToken ct = default)
    {
        if (_disposed) return Task.FromResult<byte[]?>(null);

        return Task.Run<byte[]?>(() =>
        {
            var buffer = new byte[UsbPayloadLen];
            var setup  = new UsbSetupPacket(
                DevToHost_Class_Interface,
                GetReport,
                FeatureReport,
                Interface0,
                (short)UsbPayloadLen);

            try
            {
                bool ok = _device.ControlTransfer(ref setup, buffer, UsbPayloadLen, out int n);
                _logger.LogDebug(
                    "[WinUsbHid] GET ok={Ok} bytes={N} Status=0x{S:X2}",
                    ok, n, buffer.Length > 0 ? buffer[0] : 0xFF);

                if (!ok || n != UsbPayloadLen) return null;

                // Prepend dummy Report ID to produce 91-byte array for RazerTransactionService
                var result = new byte[HidPacketLen];
                result[0] = 0x00;
                Array.Copy(buffer, 0, result, 1, UsbPayloadLen);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WinUsbHid] GET_REPORT exception");
                return null;
            }
        }, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _device.Close(); UsbDevice.Exit(); }
        catch { /* best effort */ }
    }
}
