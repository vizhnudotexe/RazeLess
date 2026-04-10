using System.Runtime.InteropServices;
using System.Windows.Threading;
using DeathAdderManager.Core.Interfaces;
using DeathAdderManager.Infrastructure.Device;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Application;

public sealed class LightingAutomationService : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly ILogger<LightingAutomationService> _logger;

    // Resolved lazily to break the circular dependency with MouseService
    private IMouseService MouseService => _services.GetRequiredService<IMouseService>();
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _displayCheckTimer;
    private DateTime _lastActivityTime;
    private int _originalBrightness;
    private bool _wasLightingOn;
    private bool _disposed;

    [DllImport("user32.dll")]
    private static extern bool GetSystemMetrics(int nIndex);

    private const int SM_CMONITORS = 80;
    private const int SM_MONITORPOWER = 80;

    public LightingAutomationService(IServiceProvider services, ILogger<LightingAutomationService> logger)
    {
        _services = services;
        _logger = logger;

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idleTimer.Tick += OnIdleTimerTick;

        _displayCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _displayCheckTimer.Tick += OnDisplayCheckTick;

        _lastActivityTime = DateTime.Now;
    }

    public void Start()
    {
        _idleTimer.Start();
        _displayCheckTimer.Start();
        _logger.LogInformation("Lighting automation service started");
    }

    public void Stop()
    {
        _idleTimer.Stop();
        _displayCheckTimer.Stop();
        _logger.LogInformation("Lighting automation service stopped");
    }

    public void RecordActivity()
    {
        _lastActivityTime = DateTime.Now;
        RestoreLightingIfNeeded();
    }

    public void UpdateSettings(int idleMinutes, bool turnOffOnIdle, bool turnOffOnDisplayOff)
    {
        _idleTimer.Stop();
        if (turnOffOnIdle)
        {
            _idleTimer.Interval = TimeSpan.FromSeconds(1);
            _idleTimer.Start();
        }

        _displayCheckTimer.Stop();
        if (turnOffOnDisplayOff)
        {
            _displayCheckTimer.Start();
        }
    }

    private void OnIdleTimerTick(object? sender, EventArgs e)
    {
        var profile = MouseService.ActiveProfile;
        if (profile == null) return;

        if (!profile.Lighting.TurnOffOnIdle) return;

        var idleTime = DateTime.Now - _lastActivityTime;
        if (idleTime.TotalMinutes >= profile.Lighting.IdleMinutes)
        {
            TurnOffLightingForIdle();
        }
    }

    private void OnDisplayCheckTick(object? sender, EventArgs e)
    {
        var profile = MouseService.ActiveProfile;
        if (profile == null) return;

        if (!profile.Lighting.TurnOffOnDisplayOff) return;

        try
        {
            bool monitorOff = !IsMonitorActive();
            if (monitorOff)
            {
                TurnOffLightingForDisplayOff();
            }
        }
        catch
        {
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern bool GetDevicePowerState(IntPtr hDevice, out bool pfOn);

    private bool IsMonitorActive()
    {
        return true;
    }

    private bool _isLightingTemporarilyOff;

    private void TurnOffLightingForIdle()
    {
        if (_isLightingTemporarilyOff) return;

        var profile = MouseService.ActiveProfile;
        if (profile == null) return;

        _originalBrightness = profile.Lighting.Brightness;
        _wasLightingOn = profile.Lighting.Brightness > 0;

        if (_wasLightingOn && MouseService.ActiveDevice != null)
        {
            _isLightingTemporarilyOff = true;
            MouseService.ActiveDevice.SetBrightnessAsync(0).ConfigureAwait(false);
            _logger.LogInformation("Lighting turned off due to idle timeout");
        }
    }

    private void TurnOffLightingForDisplayOff()
    {
        if (_isLightingTemporarilyOff) return;

        var profile = MouseService.ActiveProfile;
        if (profile == null) return;

        _originalBrightness = profile.Lighting.Brightness;
        _wasLightingOn = profile.Lighting.Brightness > 0;

        if (_wasLightingOn && MouseService.ActiveDevice != null)
        {
            _isLightingTemporarilyOff = true;
            MouseService.ActiveDevice.SetBrightnessAsync(0).ConfigureAwait(false);
            _logger.LogInformation("Lighting turned off due to display off");
        }
    }

    private void RestoreLightingIfNeeded()
    {
        if (!_isLightingTemporarilyOff) return;

        _isLightingTemporarilyOff = false;

        if (_wasLightingOn && MouseService.ActiveDevice != null)
        {
            MouseService.ActiveDevice.SetBrightnessAsync(_originalBrightness).ConfigureAwait(false);
            _logger.LogInformation("Lighting restored after activity");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
