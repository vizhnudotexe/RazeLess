using System.Text;
using HidSharp;
using DeathAdderManager.Core.Domain.Enums;
using DeathAdderManager.Core.Domain.Models;
using DeathAdderManager.Core.Interfaces;
using DeathAdderManager.Infrastructure.Hid;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Infrastructure.Device;

/// <summary>
/// High-level IMouseDevice for the DeathAdder Essential (PID 0x0071 / 0x0098).
///
/// Command routing:
///   ALL commands (DPI, polling rate, LED) go through RazerTransactionService.SendCommandAsync.
///
/// Transport selection (handled by MouseDeviceFactory):
///   WinUSB (WinUsbHidTransport) — preferred path; requires Zadig on Interface 0.
///     Sends raw USB control transfers: both DPI and LED work through this path.
///   PInvoke HID (PInvokeHidTransport) — fallback when WinUSB not installed.
///     DPI/polling work. LED returns Status=0x05 (firmware limitation on HID path).
///
/// The two transports implement the same IHidTransport interface, so the rest of
/// the code (RazerTransactionService, MouseDevice) doesn't know which is active.
/// </summary>
public sealed class MouseDevice : IMouseDevice
{
    private readonly RazerTransactionService _transaction;
    private readonly ILogger<MouseDevice>    _logger;
    private bool                             _disposed;
    private readonly SemaphoreSlim           _sendLock = new(1, 1);

    public DeviceFingerprint Fingerprint { get; }
    public bool IsConnected => !_disposed;

    public MouseDevice(
        RazerTransactionService   transaction,
        DeviceFingerprint         fingerprint,
        ILogger<MouseDevice>      logger)
    {
        _transaction = transaction;
        Fingerprint  = fingerprint;
        _logger      = logger;
    }

    // ── Performance ───────────────────────────────────────────────────────────

    public async Task SetDpiStageAsync(int stageNumber, int dpi, CancellationToken ct = default)
    {
        dpi = DpiStage.Clamp(dpi);
        _logger.LogInformation("SetDpi stage={Stage} dpi={Dpi}", stageNumber, dpi);

        await SendAck(RazerHidPacket.BuildSetDeviceMode(RazerHidPacket.DeviceModeDriver), ct);
        await Task.Delay(10, ct);
        await SendAck(RazerHidPacket.BuildSetSingleStageDpi((byte)stageNumber, dpi), ct);
        await SendAck(RazerHidPacket.BuildApplySettings(), ct);
        await TryLogDpiReadbackAsync($"SetDpiStage(stage={stageNumber}, dpi={dpi})", ct);
    }

    public async Task SetDpiStagesAsync(IReadOnlyList<int> dpiStages, int activeStageIndex, CancellationToken ct = default)
    {
        if (dpiStages.Count == 0) return;
        var clamped   = dpiStages.Select(DpiStage.Clamp).ToList();
        var safeActive = Math.Clamp(activeStageIndex, 0, clamped.Count - 1);

        _logger.LogInformation(
            "SetDpiStages activeStageIndex={A} hardwareStageNumber={H} stages=[{S}]",
            safeActive, safeActive + 1, string.Join(", ", clamped));

        await SendAck(RazerHidPacket.BuildSetDeviceMode(RazerHidPacket.DeviceModeDriver), ct);
        await Task.Delay(10, ct);
        await SendAck(RazerHidPacket.BuildSetDpiStages((byte)safeActive, clamped), ct);
        await SendAck(RazerHidPacket.BuildApplySettings(), ct);
        await TryLogDpiReadbackAsync($"SetDpiStages(active={safeActive + 1})", ct);
    }

    public async Task SetActiveDpiStageAsync(int stageIndex, CancellationToken ct = default)
    {
        _logger.LogInformation("SetActiveDpiStage uiStageIndex={I} hardwareStageNumber={H}",
            stageIndex, stageIndex + 1);

        await SendAck(RazerHidPacket.BuildSetDeviceMode(RazerHidPacket.DeviceModeDriver), ct);
        await Task.Delay(10, ct);
        await SendAck(RazerHidPacket.BuildSetActiveDpiStage(stageIndex), ct);
        await SendAck(RazerHidPacket.BuildApplySettings(), ct);
        await TryLogDpiReadbackAsync($"SetActiveDpiStage(stage={stageIndex + 1})", ct);
    }

    public async Task SetPollingRateAsync(PollingRate rate, CancellationToken ct = default)
    {
        byte pollingByte = rate switch
        {
            PollingRate.Hz1000 => RazerHidPacket.PollingByte1000,
            PollingRate.Hz500  => RazerHidPacket.PollingByte500,
            PollingRate.Hz125  => RazerHidPacket.PollingByte125,
            _                  => RazerHidPacket.PollingByte1000,
        };
        _logger.LogInformation("SetPollingRate {Rate}Hz", (int)rate);
        await SendAck(RazerHidPacket.BuildSetPollingRate(pollingByte), ct);
    }

    // ── Lighting ─────────────────────────────────────────────────────────────
    //
    // LED commands use the same SendAck path as DPI.
    // When the active transport is WinUsbHidTransport (Zadig installed on Interface 0),
    // the firmware processes LED commands correctly and returns Status=0x02 OK.
    // When the active transport is PInvokeHidTransport (fallback), the firmware returns
    // Status=0x05 Not-Supported, which is logged as a warning but does not crash the app.

    public async Task SetBrightnessAsync(int percent, CancellationToken ct = default)
    {
        percent = LightingSettings.ClampBrightness(percent);
        byte hwByte = (byte)(percent * 255 / 100);
        _logger.LogInformation("SetBrightness {Percent}% ({HwByte})", percent, hwByte);
        await SendAck(RazerHidPacket.BuildSetBrightness(RazerHidPacket.LedZoneLogo, hwByte), ct);
    }

    public async Task SetLightingEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        _logger.LogInformation("SetLightingEnabled {Enabled}", enabled);
        await SendAck(RazerHidPacket.BuildSetLedState(RazerHidPacket.LedZoneLogo, enabled), ct);
    }

    public async Task SetLightingEffectAsync(LightingEffectType effect, CancellationToken ct = default)
    {
        _logger.LogInformation("SetLightingEffect {Effect}", effect);

        // Extended matrix API (Cmd=0x0D) — OpenRazer uses this for DeathAdder Essential 2021.
        switch (effect)
        {
            case LightingEffectType.Static:
                await SendAck(RazerHidPacket.BuildSetLogoStatic2021(), ct);
                break;

            case LightingEffectType.Breathing:
                await SendAck(RazerHidPacket.BuildSetLogoEffect2021(RazerHidPacket.EffectBreathing2021, 0x02), ct);
                await SendAck(RazerHidPacket.BuildSetBrightness(RazerHidPacket.LedZoneLogo, 0xFF), ct);
                break;
        }

        await SendAck(RazerHidPacket.BuildApplySettings(), ct);
    }

    public Task SetButtonMappingAsync(MouseButton button, ButtonAction action, CancellationToken ct = default)
    {
        _logger.LogInformation("Button mapping stored (software intercept): {Button} => {Action}",
            button, action.DisplayName);
        return Task.CompletedTask;
    }

    // ── Bulk apply ────────────────────────────────────────────────────────────

    public async Task ApplyProfileAsync(MouseProfile profile, CancellationToken ct = default)
    {
        _logger.LogInformation("Applying profile '{Name}' to device {Device}",
            profile.Name, Fingerprint.FingerprintHash[..8]);

        // 1. Enter driver mode
        _logger.LogInformation("→ Driver mode");
        await SendAck(RazerHidPacket.BuildSetDeviceMode(RazerHidPacket.DeviceModeDriver), ct);
        await Task.Delay(20, ct);

        // 2. DPI stages
        var dpiValues = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            var stage = profile.DpiStages.FirstOrDefault(s => s.Index == i);
            dpiValues.Add(stage?.Enabled == true ? stage.Dpi : 800);
        }
        _logger.LogInformation("→ DPI stages: [{Stages}] active={Active}",
            string.Join(", ", dpiValues), profile.ActiveDpiStage + 1);

        await SendAck(RazerHidPacket.BuildSetDpiStages((byte)profile.ActiveDpiStage, dpiValues), ct);
        await SendAck(RazerHidPacket.BuildApplySettings(), ct);
        await TryLogDpiReadbackAsync($"ApplyProfile(active={profile.ActiveDpiStage + 1})", ct);
        await Task.Delay(15, ct);

        // 3. Polling rate
        _logger.LogInformation("→ Polling rate: {Rate}Hz", (int)profile.PollingRate);
        await SetPollingRateAsync(profile.PollingRate, ct);
        await Task.Delay(15, ct);

        // 4. Lighting — fire-and-forget, no blocking
        _logger.LogInformation("→ Brightness: {Pct}%", profile.Lighting.Brightness);
        await SetBrightnessAsync(profile.Lighting.Brightness, ct);
        await SetLightingEnabledAsync(profile.Lighting.Brightness > 0, ct);
        await SetLightingEffectAsync(profile.Lighting.Effect, ct);

        _logger.LogInformation("Profile '{Name}' applied successfully", profile.Name);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Send with full ACK (DPI, polling, device mode, LED via WinUSB)</summary>
    private async Task SendAck(byte[] packet, CancellationToken ct)
    {
        if (!IsConnected) { _logger.LogWarning("Send skipped — device not connected"); return; }
        try
        {
            await _sendLock.WaitAsync(ct);
            var ok = await _transaction.SendCommandAsync(packet, ct);
            if (!ok)
                _logger.LogWarning("Command class=0x{C:X2} id=0x{Id:X2} gave no ACK",
                    packet[7], packet[8]);
        }
        catch (OperationCanceledException) { }
        finally { _sendLock.Release(); }
    }

    /// <summary>Send fire-and-forget via HID (LED fallback when WinUSB not available)</summary>
    private async Task SendFaf(byte[] packet, CancellationToken ct)
    {
        if (!IsConnected) return;
        try
        {
            await _sendLock.WaitAsync(ct);
            await _transaction.SendFireAndForgetAsync(packet, ct);
        }
        catch (OperationCanceledException) { }
        finally { _sendLock.Release(); }
    }

    private async Task TryLogDpiReadbackAsync(string context, CancellationToken ct)
    {
        try
        {
            var response = await _transaction.SendCommandWithResponseAsync(
                RazerHidPacket.BuildGetDpi(), ct);
            if (response == null)
            {
                _logger.LogWarning("DPI readback: no response after {Context}", context);
                return;
            }

            var args    = response.Skip(RazerHidPacket.ArgsOffset).Take(32).ToArray();
            var hex     = BitConverter.ToString(args).Replace("-", " ");
            byte active = args.Length > 1 ? args[1] : (byte)0;
            byte count  = args.Length > 2 ? args[2] : (byte)0;

            var decoded = new StringBuilder();
            int offset  = 3;
            for (int i = 0; i < Math.Min((int)count, 5) && offset + 4 < args.Length; i++)
            {
                var stg = args[offset++];
                var x   = (args[offset++] << 8) | args[offset++];
                var y   = (args[offset++] << 8) | args[offset++];
                offset += 2;
                decoded.Append($"[{stg}:{x}/{y}] ");
            }

            _logger.LogInformation(
                "DPI readback after {Context}: status=0x{S:X2}, activeStage={A}, stageCount={C}, parsed={P}, rawArgs={R}",
                context, RazerHidPacket.GetStatus(response), active, count,
                decoded.ToString().Trim(), hex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DPI readback failed after {Context}", context);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await Task.CompletedTask;
    }
}

/// <summary>
/// Factory that opens a MouseDevice on the correct HID interface.
///
/// Transport priority:
///   1. WinUsbHidTransport  — if Zadig has installed WinUSB on Interface 0 (mi_00).
///      Enables LED + DPI via raw USB control transfers. PREFERRED.
///   2. PInvokeHidTransport — fallback zero-access HID handle on mi_00.
///      DPI and polling work; LED returns 0x05 from firmware (hardware limitation).
/// </summary>
public static class MouseDeviceFactory
{
    private static ILogger? _transportLogger;
    private static ILogger? _deviceLogger;
    private static ILogger? _transactionLogger;
    private static ILogger<WinUsbHidTransport>? _winUsbLogger;

    private static MouseDevice? _ledDevice;

    public static void SetLoggers(
        ILogger<HidTransport>             tl,
        ILogger<MouseDevice>              dl,
        ILogger<RazerTransactionService>  xl,
        ILogger<WinUsbHidTransport>       wl)
    {
        _transportLogger   = tl;
        _deviceLogger      = dl;
        _transactionLogger = xl;
        _winUsbLogger      = wl;
    }

    /// <summary>
    /// Try to open the mouse via WinUSB first (preferred — enables LED control).
    /// Call this once at startup before iterating HID interfaces.
    /// Returns null if WinUSB is not installed on Interface 0 yet.
    /// </summary>
    public static MouseDevice? TryOpenViaWinUsb(
        DeviceFingerprint                fingerprint,
        ILogger<HidTransport>            transportLogger,
        ILogger<MouseDevice>             deviceLogger,
        ILogger<RazerTransactionService> transactionLogger,
        ILogger<WinUsbHidTransport>      winUsbLogger)
    {
        SetLoggers(transportLogger, deviceLogger, transactionLogger, winUsbLogger);

        var transport = WinUsbHidTransport.TryOpen(0x1532, 0x0098, winUsbLogger);
        if (transport == null) return null;

        var transaction = new RazerTransactionService(transport, transactionLogger);
        var device      = new MouseDevice(transaction, fingerprint, deviceLogger);
        _ledDevice      = device;

        deviceLogger.LogInformation(
            "[Factory] WinUSB transport active — LED + DPI routed through raw USB (Interface 0).");
        return device;
    }

    /// <summary>
    /// Try to open the mouse via a specific HID interface (PInvoke or HidSharp fallback).
    /// Used when WinUSB is not installed.
    /// </summary>
    public static MouseDevice? TryOpen(
        HidDevice                        hidDevice,
        DeviceFingerprint                fingerprint,
        ILogger<HidTransport>            transportLogger,
        ILogger<MouseDevice>             deviceLogger,
        ILogger<RazerTransactionService> transactionLogger,
        ILogger<WinUsbHidTransport>      winUsbLogger)
    {
        SetLoggers(transportLogger, deviceLogger, transactionLogger, winUsbLogger);

        IHidTransport? transport;

        bool isMi00 = hidDevice.DevicePath.Contains("mi_00") &&
                      !hidDevice.DevicePath.Contains("col");

        if (isMi00)
        {
            // Zero-access CreateFile bypasses Windows HID exclusive lock on mi_00.
            // NOTE: this will silently fail to open if mi_00 is now WinUSB (Zadig installed).
            // In that case TryOpenViaWinUsb() should have succeeded already.
            transport = PInvokeHidTransport.TryOpen(hidDevice, transportLogger);
        }
        else
        {
            var stream = HidDeviceFactory.Open(hidDevice, transportLogger);
            if (stream == null) return null;
            transport = new HidTransport(stream, transportLogger);
        }

        if (transport == null) return null;

        deviceLogger.LogInformation(
            "[Factory] PInvoke HID transport active — DPI/polling only. LED needs Zadig on Interface 0.");

        var transaction = new RazerTransactionService(transport, transactionLogger);
        var device      = new MouseDevice(transaction, fingerprint, deviceLogger);
        _ledDevice = device;
        return device;
    }

    public static MouseDevice? GetLedDevice() => _ledDevice;
}
