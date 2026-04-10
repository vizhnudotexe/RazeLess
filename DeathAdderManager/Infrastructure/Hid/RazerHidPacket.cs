namespace DeathAdderManager.Infrastructure.Hid;

/// <summary>
/// Builds and parses 91-byte Razer USB HID feature report packets.
///
/// PACKET STRUCTURE (91 bytes total, offset 0 = Report ID):
/// ┌────────┬───────────────────────────────────────────────────────────────┐
/// │ Offset │ Description                                                   │
/// ├────────┼───────────────────────────────────────────────────────────────┤
/// │  0     │ Report ID (always 0x00 for feature reports)                   │
/// │  1     │ Status   (0x00 = new cmd, 0x02 = busy, 0x01 = ok, 0x03 = fail)│
/// │  2     │ Transaction ID (echo'd back by device)                        │
/// │  3-4   │ Remaining packets (big-endian, 0x0000 for single-packet cmds) │
/// │  5     │ Protocol type (0x00)                                          │
/// │  6     │ Data size  (number of meaningful bytes in arguments field)     │
/// │  7     │ Command class (e.g. 0x03 = LED, 0x04 = mouse)                │
/// │  8     │ Command ID   (specific operation within that class)           │
/// │  9-87  │ Arguments / payload (79 bytes, zero-padded)                   │
/// │  88    │ CRC = XOR of bytes 3..87 (Transaction ID byte 2 is EXCLUDED)  │
/// │  89    │ Reserved (0x00)                                               │
/// └────────┴───────────────────────────────────────────────────────────────┘
///
/// MANDATORY WRITE SEQUENCE (confirmed from USB captures):
///   1. Send setting packet (e.g. SetDpiSingleStage)
///   2. Send commit/apply packet (BuildApplySettings) — without this the
///      device silently ignores the setting write.
///
/// SOURCE: Reverse-engineered from Razer Synapse USB captures (changingdpi200to6400.pcap)
/// and the OpenRazer Linux driver (https://github.com/openrazer/openrazer).
/// </summary>
public static class RazerHidPacket
{
    public const int  PacketSize     = 91;
    public const int  PayloadSize    = 90;
    public const int  ArgsOffset     = 9;
    public const int  CrcOffset      = 88;
    public const int  ReportId       = 0x00;
    public const byte StatusNew      = 0x00;
    public const byte StatusBusy     = 0x01;
    public const byte StatusOk       = 0x02;
    public const byte StatusError    = 0x03;
    public const byte StatusTimeout  = 0x04;
    public const byte StatusNotSupp  = 0x05;
    // Rolling Transaction ID — Synapse uses incrementing values (0x01..0x1E),
    // a fixed 0xFF causes the firmware to reject commands on some models.
    private static byte _nextTransId = 0x01;
    private static byte NextTransId() { var id = _nextTransId; _nextTransId = (byte)((_nextTransId % 0x1F) + 0x01); return id; }

    // ── Command Classes ───────────────────────────────────────────────────
    public const byte ClassLed       = 0x03;
    public const byte ClassMouse     = 0x04;
    public const byte ClassButtonMap = 0x02;
    public const byte ClassDevice    = 0x00;

    // ── Command IDs (ClassDevice) ───────────────────────────────────────────
    public const byte CmdSetDeviceMode = 0x04;
    public const byte CmdGetDeviceMode = 0x84;

    // Device Mode values
    public const byte DeviceModeNormal = 0x00;
    public const byte DeviceModeDriver = 0x03;

    // ── Command IDs (ClassMouse) ──────────────────────────────────────────
    // NOTE: CmdSetDpi (0x05) is NOT used by Synapse for the DeathAdder Essential.
    // Synapse uses CmdSetDpiStages (0x06) for ALL DPI writes, even single-stage.
    public const byte CmdSetDpi       = 0x05; // Legacy / unused on this device
    public const byte CmdSetDpiStages = 0x06; // Used for single and multi-stage DPI
    public const byte CmdGetDpi       = 0x85;
    public const byte CmdApplySettings = 0x86; // MANDATORY commit packet after every DPI write

    // ── Command IDs (ClassDevice - polling rate) ──────────────────────────
    public const byte CmdSetPolling   = 0x05;
    public const byte CmdGetPolling   = 0x85;

    // ── Command IDs (ClassLed) ────────────────────────────────────────────
    public const byte CmdSetBrightness = 0x03;
    public const byte CmdGetBrightness = 0x83;
    public const byte CmdSetLedState   = 0x00;
    public const byte CmdSetLedEffect  = 0x02;

    // ── LED Zone IDs ──────────────────────────────────────────────────────
    public const byte LedZoneScroll = 0x01;
    public const byte LedZoneLogo   = 0x04;

    // ── Variable Storage ───────────────────────────────────────────────────
    public const byte VarStore = 0x01;

    // ── Polling rate byte values ──────────────────────────────────────────
    public const byte PollingByte1000 = 0x01;
    public const byte PollingByte500  = 0x02;
    public const byte PollingByte125  = 0x08;

    /// <summary>
    /// Create a zeroed 91-byte buffer with header fields pre-filled.
    /// Caller fills args[0..n] then calls <see cref="Finalise"/>.
    /// </summary>
    public static byte[] Create(byte commandClass, byte commandId, byte dataSize)
    {
        var buf = new byte[PacketSize];
        buf[0] = ReportId;
        buf[1] = StatusNew;
        buf[2] = NextTransId();  // Rolling counter matching Synapse behaviour
        buf[6] = dataSize;
        buf[7] = commandClass;
        buf[8] = commandId;
        return buf;
    }

    /// <summary>
    /// Compute CRC (XOR of bytes 3..87) and write to buf[88].
    /// IMPORTANT: Byte 2 (Transaction ID) is EXCLUDED from the CRC on the
    /// DeathAdder Essential 2021 — confirmed via Synapse USB captures.
    /// Including it causes the firmware to silently reject the packet.
    /// </summary>
    public static void Finalise(byte[] buf)
    {
        byte crc = 0;
        for (int i = 3; i < CrcOffset; i++)  // Start at 3, skip Transaction ID
            crc ^= buf[i];
        buf[CrcOffset] = crc;
        buf[89] = 0x00;
    }

    // ── Packet factory methods ─────────────────────────────────────────────

    /// <summary>
    /// Build a single-stage DPI write packet — the format used by Synapse for
    /// live DPI updates on the DeathAdder Essential.
    ///
    /// Confirmed from USB captures (changingdpi200to6400.pcap):
    ///   CMD  = Class 0x04, Cmd 0x06, Size 0x0A
    ///   args[0] = VARSTORE (0x01)
    ///   args[1] = Active stage (1-indexed, e.g. 0x01 for stage 1)
    ///   args[2] = Stage count  (0x01 = single stage write)
    ///   args[3] = 0x00 (padding)
    ///   args[4] = DPI_X high byte
    ///   args[5] = DPI_X low byte
    ///   args[6] = DPI_Y high byte
    ///   args[7] = DPI_Y low byte
    ///   args[8] = 0x00
    ///   args[9] = 0x00
    ///
    /// Example: DPI 200 → payload: 01 01 01 00 00 C8 00 C8 00 00
    /// Must be followed immediately by BuildApplySettings().
    /// </summary>
    public static byte[] BuildSetSingleStageDpi(byte stageNumber, int dpi)
    {
        var buf = Create(ClassMouse, CmdSetDpiStages, 0x0A);
        buf[ArgsOffset + 0] = VarStore;        // 0x01
        buf[ArgsOffset + 1] = stageNumber;     // active stage (1-indexed)
        buf[ArgsOffset + 2] = 0x01;            // count = 1 stage
        buf[ArgsOffset + 3] = 0x00;            // padding
        buf[ArgsOffset + 4] = (byte)((dpi >> 8) & 0xFF); // DPI_X hi
        buf[ArgsOffset + 5] = (byte)(dpi & 0xFF);         // DPI_X lo
        buf[ArgsOffset + 6] = (byte)((dpi >> 8) & 0xFF); // DPI_Y hi
        buf[ArgsOffset + 7] = (byte)(dpi & 0xFF);         // DPI_Y lo
        buf[ArgsOffset + 8] = 0x00;
        buf[ArgsOffset + 9] = 0x00;
        Finalise(buf);
        return buf;
    }

    /// <summary>
    /// Build the Apply/Commit packet that MUST follow every DPI write.
    /// Without this packet the mouse controller ignores the setting change.
    ///
    /// Confirmed from USB captures: CMD = Class 0x04, Cmd 0x86, Size 0x50.
    /// The payload is 80 zero-padded bytes with a single 0x01 at args[0].
    /// </summary>
    public static byte[] BuildApplySettings()
    {
        var buf = Create(ClassMouse, CmdApplySettings, 0x50);
        buf[ArgsOffset + 0] = 0x01; // Apply flag
        // Remaining 79 bytes stay zero
        Finalise(buf);
        return buf;
    }

    /// <summary>
    /// Build DPI stages packet for multiple stages at once.
    /// activeStageIndex is the app's 0-based stage index and is translated to
    /// the device's expected 1-based active-stage value in the packet payload.
    /// </summary>
    public static byte[] BuildSetDpiStages(byte activeStageIndex, List<int> dpiStages)
    {
        var buf = Create(ClassMouse, CmdSetDpiStages, 0x26);
        buf[ArgsOffset + 0] = VarStore;
        buf[ArgsOffset + 1] = (byte)(activeStageIndex + 1);
        buf[ArgsOffset + 2] = (byte)dpiStages.Count;
        
        int offset = 3;
        for (int i = 0; i < dpiStages.Count && offset < 0x26; i++)
        {
            int dpi = dpiStages[i];
            buf[ArgsOffset + offset++] = (byte)i;
            buf[ArgsOffset + offset++] = (byte)((dpi >> 8) & 0xFF);
            buf[ArgsOffset + offset++] = (byte)(dpi & 0xFF);
            buf[ArgsOffset + offset++] = (byte)((dpi >> 8) & 0xFF);
            buf[ArgsOffset + offset++] = (byte)(dpi & 0xFF);
            buf[ArgsOffset + offset++] = 0x00;
            buf[ArgsOffset + offset++] = 0x00;
        }
        Finalise(buf);
        return buf;
    }

    /// <summary>
    /// Build a Get DPI packet (Class 0x04, Cmd 0x85).
    /// The device may return current/active stage or full stage payload depending on firmware.
    /// </summary>
    public static byte[] BuildGetDpi()
    {
        var buf = Create(ClassMouse, CmdGetDpi, 0x01);
        buf[ArgsOffset + 0] = VarStore;
        Finalise(buf);
        return buf;
    }

    /// <summary>
    /// Build an active-stage-only packet.
    /// stageIndex is 0-based in the app model and converted to 1-based for HID.
    /// </summary>
    public static byte[] BuildSetActiveDpiStage(int stageIndex)
    {
        var buf = Create(ClassMouse, CmdSetDpiStages, 0x03);
        buf[ArgsOffset + 0] = VarStore;
        buf[ArgsOffset + 1] = (byte)(stageIndex + 1);
        buf[ArgsOffset + 2] = 0x01;
        Finalise(buf);
        return buf;
    }

    /// <summary>
    /// Build a Set Polling Rate packet (Class 0x00, Cmd 0x05).
    /// </summary>
    public static byte[] BuildSetPollingRate(byte pollingByte)
    {
        var buf = Create(ClassDevice, CmdSetPolling, 0x01);
        buf[ArgsOffset + 0] = pollingByte;
        Finalise(buf);
        return buf;
    }

    /// <summary>
    /// Build a device mode packet - must set driver mode before certain settings.
    /// Mode 0x00 = Normal, Mode 0x03 = Driver Mode
    /// </summary>
    public static byte[] BuildSetDeviceMode(byte mode)
    {
        var buf = Create(ClassDevice, CmdSetDeviceMode, 0x02);
        buf[ArgsOffset + 0] = mode;
        buf[ArgsOffset + 1] = 0x00;
        Finalise(buf);
        return buf;
    }

    /// <summary>
    /// Set LED brightness for a zone.
    /// args[0] = storage (0x01 = on-board)
    /// args[1] = zone LED ID
    /// args[2] = brightness byte (0-255)
    /// </summary>
    public static byte[] BuildSetBrightness(byte ledZone, byte brightnessByte)
    {
        var buf = Create(ClassLed, CmdSetBrightness, 0x03);
        buf[ArgsOffset + 0] = VarStore;
        buf[ArgsOffset + 1] = ledZone;
        buf[ArgsOffset + 2] = brightnessByte;
        Finalise(buf);
        return buf;
    }

    public static byte[] BuildSetLedState(byte ledZone, bool enabled)
    {
        var buf = Create(ClassLed, CmdSetLedState, 0x03);
        buf[ArgsOffset + 0] = 0x01;
        buf[ArgsOffset + 1] = ledZone;
        buf[ArgsOffset + 2] = (byte)(enabled ? 0x01 : 0x00);
        Finalise(buf);
        return buf;
    }

    public static byte[] BuildSetLedEffect(byte ledZone, byte effect)
    {
        var buf = Create(ClassLed, CmdSetLedEffect, 0x06);
        buf[ArgsOffset + 0] = 0x01;
        buf[ArgsOffset + 1] = ledZone;
        buf[ArgsOffset + 2] = effect;
        Finalise(buf);
        return buf;
    }

    public static bool ValidateCrc(byte[] response)
    {
        if (response.Length < PayloadSize) return false;
        byte crc = 0;
        for (int i = 3; i < CrcOffset; i++)  // Match Finalise — skip Transaction ID
            crc ^= response[i];
        return crc == response[CrcOffset];
    }

    public static byte GetStatus(byte[] response)
    {
        if (response.Length < 2) return 0xFF;
        return response[1];
    }

    public static byte GetTransactionId(byte[] response)
    {
        if (response.Length < 3) return 0xFF;
        return response[2];
    }

    public static bool IsResponseValid(byte[] response, byte expectedTransId)
    {
        if (response.Length < PacketSize) return false;
        if (GetStatus(response) != StatusOk) return false;
        if (GetTransactionId(response) != expectedTransId) return false;
        return ValidateCrc(response);
    }
}
