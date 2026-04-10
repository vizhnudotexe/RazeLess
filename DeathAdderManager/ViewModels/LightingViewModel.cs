using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeathAdderManager.Core.Domain.Models;
using DeathAdderManager.Core.Interfaces;

namespace DeathAdderManager.ViewModels;

public sealed partial class LightingViewModel : ObservableObject
{
    private readonly IMouseService _mouseService;

    [ObservableProperty] private int  _brightness          = 100;
    [ObservableProperty] private bool _turnOffOnDisplayOff = false;
    [ObservableProperty] private bool _turnOffOnIdle       = false;
    [ObservableProperty] private int  _idleMinutes         = 1;
    [ObservableProperty] private bool _hasUnsavedChanges   = false;
    [ObservableProperty] private bool _isLightingOn        = true;
    private int _lastNonZeroBrightness = 100;

    public LightingViewModel(IMouseService mouseService)
    {
        _mouseService = mouseService;
    }

    public void LoadFromProfile(MouseProfile profile)
    {
        Brightness           = profile.Lighting.Brightness;
        TurnOffOnDisplayOff  = profile.Lighting.TurnOffOnDisplayOff;
        TurnOffOnIdle        = profile.Lighting.TurnOffOnIdle;
        IdleMinutes          = profile.Lighting.IdleMinutes;
        IsLightingOn         = profile.Lighting.Brightness > 0;
        HasUnsavedChanges    = false;
    }

    partial void OnBrightnessChanged(int value)
    {
        HasUnsavedChanges = true;
        if (value > 0)
        {
            _lastNonZeroBrightness = value;
            if (!IsLightingOn) IsLightingOn = true;
        }

        var device = _mouseService.ActiveDevice;
        if (device?.IsConnected == true)
        {
            // Apply brightness immediately (fire-and-forget)
            _ = device.SetBrightnessAsync(value, CancellationToken.None);
        }
    }
    partial void OnIsLightingOnChanged(bool value)
    {
        HasUnsavedChanges = true;

        var device = _mouseService.ActiveDevice;
        if (device?.IsConnected != true) return;

        if (!value)
        {
            // turn off lighting immediately
            _ = device.SetBrightnessAsync(0, CancellationToken.None);
        }
        else
        {
            // restore last non-zero brightness
            var to = Math.Clamp(_lastNonZeroBrightness, 1, 100);
            Brightness = to;
            _ = device.SetBrightnessAsync(to, CancellationToken.None);
        }
    }
    partial void OnTurnOffOnDisplayOffChanged(bool value) => HasUnsavedChanges = true;
    partial void OnTurnOffOnIdleChanged(bool value) => HasUnsavedChanges = true;
    partial void OnIdleMinutesChanged(int value) => HasUnsavedChanges = true;

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var profile = _mouseService.ActiveProfile;
        if (profile == null) return;

        profile.Lighting.Brightness          = Brightness;
        profile.Lighting.TurnOffOnDisplayOff = TurnOffOnDisplayOff;
        profile.Lighting.TurnOffOnIdle       = TurnOffOnIdle;
        profile.Lighting.IdleMinutes         = IdleMinutes;

        await _mouseService.SaveAndApplyProfileAsync(profile);
        HasUnsavedChanges = false;
    }
}
