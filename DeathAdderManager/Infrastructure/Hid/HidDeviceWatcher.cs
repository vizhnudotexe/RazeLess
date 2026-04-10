using HidSharp;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Infrastructure.Hid;

/// <summary>
/// Raises events when a DeathAdder Essential is plugged in or removed.
/// Uses HidSharp's DeviceList.Changed event which hooks WM_DEVICECHANGE on Windows.
/// This is purely event-driven — no polling loops.
/// </summary>
public sealed class HidDeviceWatcher : IDisposable
{
    private readonly ILogger<HidDeviceWatcher> _logger;
    private bool _subscribed;

    public event EventHandler<ISet<HidDevice>>? DeviceListRefreshed;

    public HidDeviceWatcher(ILogger<HidDeviceWatcher> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        if (_subscribed) return;
        DeviceList.Local.Changed += OnDeviceListChanged;
        _subscribed = true;
        _logger.LogInformation("HID device watcher started");
    }

    public void Stop()
    {
        if (!_subscribed) return;
        DeviceList.Local.Changed -= OnDeviceListChanged;
        _subscribed = false;
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
    {
        _logger.LogDebug("USB device list changed — rescanning for DeathAdder Essential");
        var current = HidDeviceFactory.EnumerateDeathAdderDevices()
            .ToHashSet(DevicePathComparer.Instance);
        DeviceListRefreshed?.Invoke(this, current);
    }

    public void Dispose() => Stop();
}
