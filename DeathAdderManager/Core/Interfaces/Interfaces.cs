using DeathAdderManager.Core.Domain.Enums;
using DeathAdderManager.Core.Domain.Models;

namespace DeathAdderManager.Core.Interfaces;

// ──────────────────────────────────────────────
// HID transport abstraction
// ──────────────────────────────────────────────

/// <summary>
/// Low-level pipe to the mouse. Sends and receives raw 90-byte Razer HID packets.
/// Abstracted so the protocol layer can be unit-tested with a fake transport.
/// </summary>
public interface IHidTransport : IDisposable
{
    bool IsOpen { get; }
    Task<bool>    SendFeatureReportAsync(byte[] packet, CancellationToken ct = default);
    Task<byte[]?> ReadFeatureReportAsync(CancellationToken ct = default);
}

// ──────────────────────────────────────────────
// Mouse device abstraction
// ──────────────────────────────────────────────

/// <summary>
/// High-level API for all mouse operations.
/// Wraps the HID protocol layer; callers should not know about raw bytes.
/// </summary>
public interface IMouseDevice : IAsyncDisposable
{
    DeviceFingerprint Fingerprint  { get; }
    bool              IsConnected  { get; }

    // ── Performance ──────────────────────────
    Task SetDpiStageAsync(int stageIndex, int dpi, CancellationToken ct = default);
    Task SetDpiStagesAsync(IReadOnlyList<int> dpiStages, int activeStageIndex, CancellationToken ct = default);
    Task SetActiveDpiStageAsync(int stageIndex, CancellationToken ct = default);
    Task SetPollingRateAsync(PollingRate rate, CancellationToken ct = default);

    // ── Lighting ─────────────────────────────
    Task SetBrightnessAsync(int percent, CancellationToken ct = default);
    Task SetLightingEnabledAsync(bool enabled, CancellationToken ct = default);
    Task SetLightingEffectAsync(LightingEffectType effect, CancellationToken ct = default);

    // ── Buttons ──────────────────────────────
    Task SetButtonMappingAsync(MouseButton button, ButtonAction action, CancellationToken ct = default);

    // ── Bulk apply ───────────────────────────
    Task ApplyProfileAsync(MouseProfile profile, CancellationToken ct = default);
}

// ──────────────────────────────────────────────
// Profile repository
// ──────────────────────────────────────────────

public interface IProfileRepository
{
    Task<List<MouseProfile>> GetAllAsync();
    Task<MouseProfile?>      GetByIdAsync(Guid id);
    Task                     SaveAsync(MouseProfile profile);
    Task                     DeleteAsync(Guid id);
    Task<Guid?>              GetLastActiveProfileIdAsync(string deviceFingerprintHash);
    Task                     SetLastActiveProfileIdAsync(string deviceFingerprintHash, Guid profileId);
}

// ──────────────────────────────────────────────
// Device fingerprint service
// ──────────────────────────────────────────────

public interface IDeviceFingerprintService
{
    Task<List<DeviceFingerprint>> DetectDevicesAsync();
    Task<DeviceFingerprint?>      LookupKnownDeviceAsync(string fingerprintHash);
    Task                          SaveFingerprintAsync(DeviceFingerprint fingerprint);
    Task<List<DeviceFingerprint>> GetAllKnownDevicesAsync();
}

// ──────────────────────────────────────────────
// Startup restore service
// ──────────────────────────────────────────────

public interface IStartupRestoreService
{
    Task<bool> RestoreLastProfileAsync(CancellationToken ct = default);
    void SetRunOnStartup(bool enable);
    bool IsRunOnStartupEnabled();
}

// ──────────────────────────────────────────────
// Mouse service (orchestration)
// ──────────────────────────────────────────────

public interface IMouseService
{
    IMouseDevice? ActiveDevice         { get; }
    IMouseDevice? LedDevice            { get; }
    MouseProfile? ActiveProfile        { get; }
    bool          IsDeviceConnected    { get; }

    event EventHandler<IMouseDevice?>  DeviceChanged;
    event EventHandler<MouseProfile?>  ProfileChanged;

    Task<List<MouseProfile>> GetProfilesAsync();
    Task                     SwitchProfileAsync(Guid profileId);
    Task                     SaveAndApplyProfileAsync(MouseProfile profile);
    Task                     DeleteProfileAsync(Guid profileId);
    Task                     CreateProfileAsync(string name);
}
