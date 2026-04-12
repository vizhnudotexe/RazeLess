using HidSharp;
using DeathAdderManager.Core.Domain.Models;
using DeathAdderManager.Core.Interfaces;
using DeathAdderManager.Infrastructure.Device;
using DeathAdderManager.Infrastructure.Hid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WindowsHidTransport = DeathAdderManager.Infrastructure.Hid.WindowsHidTransport;
using WindowsHidDeviceFactory = DeathAdderManager.Infrastructure.Hid.WindowsHidDeviceFactory;

namespace DeathAdderManager.Application;

/// <summary>
/// Central orchestration service. Manages device connection/reconnection,
/// profile switching, and exposes state to ViewModels via events.
/// Registered as a singleton hosted service.
/// </summary>
public sealed class MouseService : BackgroundService, IMouseService
{
    private readonly IDeviceFingerprintService        _fps;
    private readonly IProfileRepository               _profiles;
    private readonly ILogger<MouseService>            _logger;
    private readonly ILogger<HidTransport>            _transportLogger;
    private readonly ILogger<MouseDevice>             _deviceLogger;
    private readonly ILogger<RazerTransactionService> _transactionLogger;
    private readonly ILogger<WinUsbHidTransport>      _winUsbLogger;
    private readonly HidDeviceWatcher                 _watcher;
    private readonly LightingAutomationService        _lightingAutomation;

    private IMouseDevice?   _activeDevice;
    private MouseProfile?   _activeProfile;
    private ISet<HidDevice> _knownHidDevices = new HashSet<HidDevice>(DevicePathComparer.Instance);
    private IMouseDevice?   _ledDevice;

    public IMouseDevice? ActiveDevice      => _activeDevice;
    public IMouseDevice? LedDevice         => _ledDevice;
    public MouseProfile? ActiveProfile     => _activeProfile;
    public bool          IsDeviceConnected => _activeDevice?.IsConnected ?? false;

    public event EventHandler<IMouseDevice?>? DeviceChanged;
    public event EventHandler<MouseProfile?>? ProfileChanged;

    public MouseService(
        IDeviceFingerprintService          fps,
        IProfileRepository                 profiles,
        HidDeviceWatcher                   watcher,
        ILogger<MouseService>              logger,
        ILogger<HidTransport>              transportLogger,
        ILogger<MouseDevice>               deviceLogger,
        ILogger<RazerTransactionService>   transactionLogger,
        ILogger<WinUsbHidTransport>        winUsbLogger,
        LightingAutomationService          lightingAutomation)
    {
        _fps               = fps;
        _profiles          = profiles;
        _watcher           = watcher;
        _logger            = logger;
        _transportLogger   = transportLogger;
        _deviceLogger      = deviceLogger;
        _transactionLogger = transactionLogger;
        _winUsbLogger      = winUsbLogger;
        _lightingAutomation = lightingAutomation;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Allow skipping device connect for debugging invisible-UI or startup hangs.
        var skipConnect = Environment.GetEnvironmentVariable("DA_SKIP_DEVICE_CONNECT") == "1";
        if (skipConnect)
        {
            _logger.LogWarning("DA_SKIP_DEVICE_CONNECT=1: skipping device watcher and initial connect for debugging");
            // Keep host alive so the UI can be shown while device code is disabled
            await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { });
            return;
        }

        _watcher.DeviceListRefreshed += OnDeviceListRefreshed;
        _watcher.Start();

        // Initial scan
        await ScanAndConnectAsync(stoppingToken);

        // Keep alive until host shuts down
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { });

        _watcher.Stop();

        if (_activeDevice != null)
            await _activeDevice.DisposeAsync();
    }

    // ── Profile management ────────────────────────────────────────────────────

    public Task<List<MouseProfile>> GetProfilesAsync() => _profiles.GetAllAsync();

    public async Task SwitchProfileAsync(Guid profileId)
    {
        var profile = await _profiles.GetByIdAsync(profileId);
        if (profile == null)
        {
            _logger.LogWarning("SwitchProfile: profile {Id} not found", profileId);
            return;
        }
        await ApplyProfileInternalAsync(profile);
    }

    public async Task SaveAndApplyProfileAsync(MouseProfile profile)
    {
        await _profiles.SaveAsync(profile);
        await ApplyProfileInternalAsync(profile);
    }

    public async Task DeleteProfileAsync(Guid profileId)
    {
        await _profiles.DeleteAsync(profileId);
    }

    public async Task CreateProfileAsync(string name)
    {
        var profile = MouseProfile.CreateDefault();
        profile.Name = name;
        await _profiles.SaveAsync(profile);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private async Task ApplyProfileInternalAsync(MouseProfile profile)
    {
        _activeProfile = profile;
        ProfileChanged?.Invoke(this, profile);

        if (_activeDevice?.IsConnected == true)
        {
            await _activeDevice.ApplyProfileAsync(profile);

            // Persist last-used profile for this device
            if (_activeDevice != null)
                await _profiles.SetLastActiveProfileIdAsync(
                    _activeDevice.Fingerprint.FingerprintHash, profile.Id);
            
            // Update lighting automation settings (idle/display-off)
            try
            {
                _lightingAutomation?.UpdateSettings(profile.Lighting.IdleMinutes, profile.Lighting.TurnOffOnIdle, profile.Lighting.TurnOffOnDisplayOff);
            }
            catch { }
        }
    }

    private async Task ScanAndConnectAsync(CancellationToken ct)
    {
        var fingerprints = await _fps.DetectDevicesAsync();

        if (fingerprints.Count == 0)
        {
            _logger.LogInformation("No DeathAdder Essential detected");
            return;
        }

        var fp      = fingerprints[0]; // Use first detected physical device
        var hidDevs = HidDeviceFactory.EnumerateConfigInterfaces().ToList();

        if (hidDevs.Count == 0)
        {
            _logger.LogWarning("No config interface found, trying all interfaces");
            hidDevs = HidDeviceFactory.EnumerateDeathAdderDevices().ToList();
        }

        if (hidDevs.Count == 0) return;

        await ConnectDeviceAsync(hidDevs, fp, ct);
    }

    private async Task ConnectDeviceAsync(List<HidDevice> hidDevs, DeviceFingerprint fp, CancellationToken ct)
    {
        if (_activeDevice != null)
            await _activeDevice.DisposeAsync();

        MouseDevice? device = null;

        // ── Strategy 1: WinUSB (Interface 0 via Zadig) ────────────────────────
        // If WinUSB is installed on mi_00, this handles BOTH DPI and LED commands.
        device = MouseDeviceFactory.TryOpenViaWinUsb(
            fp, _transportLogger, _deviceLogger, _transactionLogger, _winUsbLogger);

        if (device != null)
        {
            _logger.LogInformation("Opened via WinUSB (Interface 0) — LED + DPI enabled.");
        }
        else
        {
            // ── Strategy 2: PInvoke HID fallback ──────────────────────────────
            // LED will not work; DPI and polling rate still function.
            foreach (var hidDev in hidDevs)
            {
                _logger.LogInformation("Trying HID interface: {Path} (MaxLen={MaxLen})",
                    hidDev.DevicePath, hidDev.MaxInputReportLength);
                device = MouseDeviceFactory.TryOpen(
                    hidDev, fp, _transportLogger, _deviceLogger, _transactionLogger, _winUsbLogger);
                if (device != null)
                {
                    _logger.LogInformation("Opened via HID (DPI only, no LED). Run Zadig on Interface 0 to enable LED.");
                    break;
                }
            }
        }

        if (device == null)
        {
            _logger.LogWarning("Could not open any HID interfaces for the mouse. Windows might be locking them.");
            return;
        }

        _activeDevice = device;
        _ledDevice = MouseDeviceFactory.GetLedDevice();
        _logger.LogInformation("Connected to {DisplayName}", fp.DisplayName);
        DeviceChanged?.Invoke(this, device);

        // Save fingerprint and restore last profile
        fp.LastSeen = DateTime.UtcNow;
        await _fps.SaveFingerprintAsync(fp);

        var lastProfileId = await _profiles.GetLastActiveProfileIdAsync(fp.FingerprintHash);
        MouseProfile profile;

        if (lastProfileId.HasValue)
        {
            profile = await _profiles.GetByIdAsync(lastProfileId.Value)
                      ?? MouseProfile.CreateDefault();
        }
        else
        {
            var all = await _profiles.GetAllAsync();
            profile = all.FirstOrDefault() ?? MouseProfile.CreateDefault();
        }

        _logger.LogInformation("Restoring profile '{Name}' (ID: {Id}). UI Active Stage Index: {StageIndex}, Hardware Stage Number: {HardwareStage}, DPI: {Dpi}", 
            profile.Name, profile.Id, profile.ActiveDpiStage, profile.ActiveDpiStage + 1,
            profile.DpiStages.FirstOrDefault(s => s.Index == profile.ActiveDpiStage)?.Dpi ?? 0);
        
        await ApplyProfileInternalAsync(profile);
    }

    private void OnDeviceListRefreshed(object? sender, ISet<HidDevice> current)
    {
        var connected    = current.Except(_knownHidDevices, DevicePathComparer.Instance).ToList();
        var disconnected = _knownHidDevices.Except(current, DevicePathComparer.Instance).ToList();
        _knownHidDevices = current;

        if (disconnected.Any())
        {
            _logger.LogInformation("Mouse disconnected");
            _activeDevice = null;
            DeviceChanged?.Invoke(this, null);
        }

        if (connected.Any())
        {
            _logger.LogInformation("Mouse reconnected — restoring profile");
            _ = Task.Run(async () => await ScanAndConnectAsync(CancellationToken.None));
        }
    }
}
