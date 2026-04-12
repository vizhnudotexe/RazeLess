using LibUsbDotNet;
using LibUsbDotNet.Main;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Infrastructure.Device;

/// <summary>
/// Sends Razer LED packets via raw USB control transfer, bypassing the Windows HID stack.
///
/// Why needed:
///   The DeathAdder Essential 2021 (PID 0x0098) firmware rejects every Class=0x03
///   LED packet sent through the standard HID SetFeature path (Status=0x05 on all
///   interfaces). Razer Synapse uses RZUDD.sys (a kernel driver) to send raw USB
///   control transfers instead. OpenRazer on Linux does the same via usb_control_msg().
///
/// This service replicates that exact control transfer using LibUsbDotNet over WinUSB.
///
/// PREREQUISITE (one-time manual step):
///   The user must run Zadig (https://zadig.akeo.ie/) to replace the HID driver on
///   the mi_00 interface with the WinUSB driver. Only then will LibUsbDotNet be able
///   to open the device. If WinUSB is not installed, IsAvailable returns false and
///   all calls are silently skipped — the app falls back to HID fire-and-forget.
///
/// Control transfer parameters (matching OpenRazer usb_control_msg call):
///   bmRequestType = 0x21  (Class | Interface | Host-to-Device)
///   bRequest      = 0x09  (HID SET_REPORT)
///   wValue        = 0x0300 (Report Type=Feature, Report ID=0)
///   wIndex        = 0      (Interface 0, i.e. mi_00)
///   data          = 91-byte Razer HID packet
/// </summary>
public sealed class RawUsbLightingService : IDisposable
{
    private const int RazerVid = 0x1532;
    private const int RazerPid = 0x0098;

    // Control transfer values matching OpenRazer's usb_control_msg
    private const byte  BmRequestType = 0x21;
    private const byte  BRequest      = 0x09;
    private const short WValue        = 0x0300;

    // OpenRazer sends 90 bytes (struct razer_report), NOT 91.
    // Our packet array is 91 bytes: byte[0] = Windows HID Report ID (0x00),
    // bytes[1..90] = the 90-byte Razer protocol payload.
    // The Report ID is already encoded in wValue high-byte (0x03 = Feature, 0x00 = ID 0),
    // so the data field must NOT include the Report ID byte.
    // We skip byte[0] and send exactly 90 bytes — matching OpenRazer exactly.
    private const int HidPacketLength  = 91; // full array from our builder
    private const int UsbPayloadLength = 90; // what the firmware expects

    // Try wIndex 0, 1, 2 in order. wIndex = USB interface number.
    private static readonly short[] InterfaceIndexesToTry = [0, 1, 2];

    private readonly ILogger<RawUsbLightingService> _logger;
    private UsbDevice?  _usbDevice;
    private short       _workingInterfaceIndex = 0;
    private bool        _disposed;

    public bool IsAvailable => _usbDevice != null && !_disposed;

    public RawUsbLightingService(ILogger<RawUsbLightingService> logger)
    {
        _logger = logger;
        TryOpen();
    }

    private void TryOpen()
    {
        try
        {
            var finder = new UsbDeviceFinder(RazerVid, RazerPid);
            _usbDevice = UsbDevice.OpenUsbDevice(finder);

            if (_usbDevice == null)
            {
                _logger.LogInformation(
                    "[RawUsbLighting] WinUSB device not found for VID=0x{V:X4} PID=0x{P:X4}. " +
                    "Run Zadig to install WinUSB on the mi_00 interface, then restart the app.",
                    RazerVid, RazerPid);
            }
            else
            {
                _logger.LogInformation(
                    "[RawUsbLighting] WinUSB device opened: {Name}",
                    _usbDevice.Info?.ProductString ?? "Razer DeathAdder Essential");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RawUsbLighting] Failed to open WinUSB device (expected if Zadig not run yet).");
            _usbDevice = null;
        }
    }

    /// <summary>
    /// Send a 91-byte Razer packet via raw USB control transfer.
    /// Tries each wIndex in <see cref="InterfaceIndexesToTry"/> until one succeeds.
    /// Returns true if the transfer was accepted (ErrorCode.Success).
    /// Returns false silently if WinUSB is not available.
    /// </summary>
    public Task<bool> SendLightingPacketAsync(byte[] packet, CancellationToken ct = default)
    {
        if (!IsAvailable) return Task.FromResult(false);
        if (packet.Length != HidPacketLength)
        {
            _logger.LogWarning("[RawUsbLighting] Packet length {L} != {E}, skipping.", packet.Length, HidPacketLength);
            return Task.FromResult(false);
        }

        // LibUsbDotNet control transfers are synchronous — offload to thread pool
        return Task.Run(() => SendControlTransfer(packet), ct);
    }

    private bool SendControlTransfer(byte[] packet)
    {
        if (_usbDevice == null) return false;

        // Strip the Windows HID Report ID byte (packet[0] = 0x00).
        // USB control transfer with wValue=0x0300 already encodes (Feature, ID=0).
        // OpenRazer sends sizeof(struct razer_report) = 90 bytes, no Report ID prefix.
        // Sending 91 bytes with the Report ID prefix shifts every field by 1 byte,
        // causing the firmware to silently accept but ignore the payload.
        byte[] payload = new byte[UsbPayloadLength];
        Array.Copy(packet, 1, payload, 0, UsbPayloadLength); // skip byte[0] (Report ID)

        // Try wIndex 0 first (the one that worked last time), then others
        var ordered = new short[] { _workingInterfaceIndex }
            .Concat(InterfaceIndexesToTry.Where(i => i != _workingInterfaceIndex))
            .ToArray();

        foreach (var wIndex in ordered)
        {
            try
            {
                var setup = new UsbSetupPacket(
                    BmRequestType,
                    BRequest,
                    WValue,
                    wIndex,
                    (short)UsbPayloadLength);

                int transferred = 0;
                bool ok = _usbDevice.ControlTransfer(ref setup, payload, UsbPayloadLength, out transferred);

                _logger.LogDebug(
                    "[RawUsbLighting] ControlTransfer wIndex={I} ok={Ok} transferred={T} Class=0x{C:X2} Cmd=0x{Cmd:X2}",
                    wIndex, ok, transferred, packet[7], packet[8]);

                if (ok && transferred == UsbPayloadLength)
                {
                    _workingInterfaceIndex = wIndex;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RawUsbLighting] ControlTransfer exception on wIndex={I}", wIndex);
            }
        }

        _logger.LogWarning("[RawUsbLighting] All wIndex attempts failed for Class=0x{C:X2} Cmd=0x{Cmd:X2}",
            packet[7], packet[8]);
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _usbDevice?.Close();
            UsbDevice.Exit();
        }
        catch { /* best effort */ }
    }
}
