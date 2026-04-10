using System.Text;
using HidSharp;
using DeathAdderManager.Core.Domain.Enums;
using DeathAdderManager.Core.Domain.Models;
using DeathAdderManager.Core.Interfaces;
using DeathAdderManager.Infrastructure.Hid;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Infrastructure.Device;

/// <summary>
/// High-level implementation of IMouseDevice.
/// Translates profile operations into Razer HID packets and sends them via IHidTransport.
/// </summary>
public sealed class MouseDevice : IMouseDevice
{
    private readonly RazerTransactionService _transaction;
    private readonly ILogger<MouseDevice> _logger;
    private bool _disposed;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public DeviceFingerprint Fingerprint { get; }
    public bool IsConnected => !_disposed;

    public MouseDevice(RazerTransactionService transaction, DeviceFingerprint fingerprint, ILogger<MouseDevice> logger)
    {
        _transaction = transaction;
        Fingerprint = fingerprint;
        _logger = logger;
    }

    public async Task SetDpiStageAsync(int stageNumber, int dpi, CancellationToken ct = default)
    {
        dpi = DpiStage.Clamp(dpi);
        _logger.LogInformation("SetDpi stage={Stage} dpi={Dpi}", stageNumber, dpi);

        await Send(RazerHidPacket.BuildSetDeviceMode(RazerHidPacket.DeviceModeDriver), ct);
        await Task.Delay(10, ct);

        var writePacket = RazerHidPacket.BuildSetSingleStageDpi((byte)stageNumber, dpi);
        await Send(writePacket, ct);
        await Send(RazerHidPacket.BuildApplySettings(), ct);

        await TryLogDpiReadbackAsync($"SetDpiStage(stage={stageNumber}, dpi={dpi})", ct);
    }

    public async Task SetDpiStagesAsync(IReadOnlyList<int> dpiStages, int activeStageIndex, CancellationToken ct = default)
    {
        if (dpiStages.Count == 0) return;

        var clamped = dpiStages.Select(DpiStage.Clamp).ToList();
        var safeActive = Math.Clamp(activeStageIndex, 0, clamped.Count - 1);

        _logger.LogInformation(
            "SetDpiStages activeStageIndex={ActiveStageIndex} hardwareStageNumber={HardwareStage} stages=[{Stages}]",
            safeActive,
            safeActive + 1,
            string.Join(", ", clamped));

        await Send(RazerHidPacket.BuildSetDeviceMode(RazerHidPacket.DeviceModeDriver), ct);
        await Task.Delay(10, ct);

        var packet = RazerHidPacket.BuildSetDpiStages((byte)safeActive, clamped);
        await Send(packet, ct);
        await Send(RazerHidPacket.BuildApplySettings(), ct);

        await TryLogDpiReadbackAsync(
            $"SetDpiStages(active={safeActive + 1}, values=[{string.Join(", ", clamped)}])",
            ct);
    }

    public async Task SetActiveDpiStageAsync(int stageIndex, CancellationToken ct = default)
    {
        _logger.LogInformation("SetActiveDpiStage uiStageIndex={StageIndex} hardwareStageNumber={HardwareStage}",
            stageIndex, stageIndex + 1);

        await Send(RazerHidPacket.BuildSetDeviceMode(RazerHidPacket.DeviceModeDriver), ct);
        await Task.Delay(10, ct);

        var packet = RazerHidPacket.BuildSetActiveDpiStage(stageIndex);
        await Send(packet, ct);
        await Send(RazerHidPacket.BuildApplySettings(), ct);

        await TryLogDpiReadbackAsync($"SetActiveDpiStage(stage={stageIndex + 1})", ct);
    }

    public async Task SetPollingRateAsync(PollingRate rate, CancellationToken ct = default)
    {
        byte pollingByte = rate switch
        {
            PollingRate.Hz1000 => RazerHidPacket.PollingByte1000,
            PollingRate.Hz500 => RazerHidPacket.PollingByte500,
            PollingRate.Hz125 => RazerHidPacket.PollingByte125,
            _ => RazerHidPacket.PollingByte1000,
        };
        _logger.LogInformation("SetPollingRate {Rate}Hz", (int)rate);
        var packet = RazerHidPacket.BuildSetPollingRate(pollingByte);
        await Send(packet, ct);
    }

    public async Task SetBrightnessAsync(int percent, CancellationToken ct = default)
    {
        percent = LightingSettings.ClampBrightness(percent);
        byte hwByte = (byte)(percent * 255 / 100);
        _logger.LogInformation("SetBrightness {Percent}% ({HwByte})", percent, hwByte);

        await Send(RazerHidPacket.BuildSetBrightness(RazerHidPacket.LedZoneScroll, hwByte), ct);
        await Send(RazerHidPacket.BuildSetBrightness(RazerHidPacket.LedZoneLogo, hwByte), ct);
    }

    public async Task SetLightingEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        _logger.LogInformation("SetLightingEnabled {Enabled}", enabled);
        await Send(RazerHidPacket.BuildSetLedState(RazerHidPacket.LedZoneScroll, enabled), ct);
        await Send(RazerHidPacket.BuildSetLedState(RazerHidPacket.LedZoneLogo, enabled), ct);
    }

    public Task SetButtonMappingAsync(MouseButton button, ButtonAction action, CancellationToken ct = default)
    {
        _logger.LogInformation("Button mapping stored (software intercept): {Button} => {Action}", button, action.DisplayName);
        return Task.CompletedTask;
    }

    public async Task ApplyProfileAsync(MouseProfile profile, CancellationToken ct = default)
    {
        _logger.LogInformation("Applying profile '{Name}' to device {Device}",
            profile.Name, Fingerprint.FingerprintHash[..8]);

        _logger.LogInformation("Setting Device Mode = Driver (0x03)");
        await Send(RazerHidPacket.BuildSetDeviceMode(RazerHidPacket.DeviceModeDriver), ct);
        await Task.Delay(20, ct);

        var dpiValues = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            var stage = profile.DpiStages.FirstOrDefault(s => s.Index == i);
            dpiValues.Add(stage?.Enabled == true ? stage.Dpi : 0);
        }

        var dpiPacket = RazerHidPacket.BuildSetDpiStages((byte)profile.ActiveDpiStage, dpiValues);
        await Send(dpiPacket, ct);
        _logger.LogInformation(
            "Sent DPI stages: {Stages}; uiActiveStageIndex={StageIndex}; hardwareActiveStageNumber={HardwareStage}",
            string.Join(", ", dpiValues),
            profile.ActiveDpiStage,
            profile.ActiveDpiStage + 1);

        await Send(RazerHidPacket.BuildApplySettings(), ct);
        await TryLogDpiReadbackAsync(
            $"ApplyProfile(active={profile.ActiveDpiStage + 1}, values=[{string.Join(", ", dpiValues)}])",
            ct);

        await Task.Delay(15, ct);
        await SetPollingRateAsync(profile.PollingRate, ct);

        await Task.Delay(15, ct);
        await SetBrightnessAsync(profile.Lighting.Brightness, ct);

        if (profile.Lighting.Brightness == 0)
            await SetLightingEnabledAsync(false, ct);
        else
            await SetLightingEnabledAsync(true, ct);

        _logger.LogInformation("Profile '{Name}' applied successfully", profile.Name);
    }

    private async Task Send(byte[] packet, CancellationToken ct)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Send skipped - device not connected");
            return;
        }

        try
        {
            await _sendLock.WaitAsync(ct);
            var ok = await _transaction.SendCommandAsync(packet, ct);
            if (!ok)
            {
                _logger.LogWarning("Command class=0x{Class:X2} id=0x{Id:X2} was rejected or timed out. (Normal if unsupported)",
                    packet[7], packet[8]);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Send cancelled");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task TryLogDpiReadbackAsync(string context, CancellationToken ct)
    {
        try
        {
            var response = await _transaction.SendCommandWithResponseAsync(RazerHidPacket.BuildGetDpi(), ct);
            if (response == null)
            {
                _logger.LogWarning("DPI readback: no response after {Context}", context);
                return;
            }

            var args = response.Skip(RazerHidPacket.ArgsOffset).Take(32).ToArray();
            var hex = BitConverter.ToString(args).Replace("-", " ");
            byte activeStage = args.Length > 1 ? args[1] : (byte)0;
            byte stageCount = args.Length > 2 ? args[2] : (byte)0;

            var decoded = new StringBuilder();
            int offset = 3;
            int maxStagesToParse = Math.Min(stageCount, (byte)5);
            for (int i = 0; i < maxStagesToParse; i++)
            {
                if (offset + 4 >= args.Length) break;
                var stage = args[offset++];
                var x = (args[offset++] << 8) | args[offset++];
                var y = (args[offset++] << 8) | args[offset++];
                offset += 2;
                decoded.Append($"[{stage}:{x}/{y}] ");
            }

            _logger.LogInformation(
                "DPI readback after {Context}: status=0x{Status:X2}, activeStage={ActiveStage}, stageCount={StageCount}, parsed={Parsed}, rawArgs={Raw}",
                context,
                RazerHidPacket.GetStatus(response),
                activeStage,
                stageCount,
                decoded.ToString().Trim(),
                hex);
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
/// Factory that opens a MouseDevice for a detected HID device.
/// </summary>
public static class MouseDeviceFactory
{
    public static MouseDevice? TryOpen(
        HidDevice hidDevice,
        DeviceFingerprint fingerprint,
        ILogger<HidTransport> transportLogger,
        ILogger<MouseDevice> deviceLogger,
        ILogger<RazerTransactionService> transactionLogger)
    {
        IHidTransport? transport;

        if (hidDevice.DevicePath.Contains("mi_00") && !hidDevice.DevicePath.Contains("col"))
        {
            transport = PInvokeHidTransport.TryOpen(hidDevice, transportLogger);
        }
        else
        {
            var stream = HidDeviceFactory.Open(hidDevice, transportLogger);
            if (stream == null) return null;
            transport = new HidTransport(stream, transportLogger);
        }

        if (transport == null) return null;

        var transaction = new RazerTransactionService(transport, transactionLogger);
        return new MouseDevice(transaction, fingerprint, deviceLogger);
    }
}
