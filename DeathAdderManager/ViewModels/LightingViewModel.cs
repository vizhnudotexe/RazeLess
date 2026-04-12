using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeathAdderManager.Core.Domain.Enums;
using DeathAdderManager.Core.Domain.Models;
using DeathAdderManager.Core.Interfaces;

namespace DeathAdderManager.ViewModels;

public sealed partial class LightingViewModel : ObservableObject
{
    private readonly IMouseService _mouseService;

    [ObservableProperty] private int              _brightness          = 100;
    [ObservableProperty] private bool             _turnOffOnDisplayOff = false;
    [ObservableProperty] private bool             _turnOffOnIdle       = false;
    [ObservableProperty] private int              _idleMinutes         = 1;
    [ObservableProperty] private bool             _hasUnsavedChanges   = false;
    [ObservableProperty] private bool             _isLightingOn        = true;
    [ObservableProperty] private LightingEffectType _selectedEffect    = LightingEffectType.Static;

    // Remembers the last non-zero brightness for restore when lighting is toggled back on
    private int _lastNonZeroBrightness = 100;

    public LightingViewModel(IMouseService mouseService)
    {
        _mouseService = mouseService;
    }

    // ── Profile sync ──────────────────────────────────────────────────────────

    public void LoadFromProfile(MouseProfile profile)
    {
        // Suppress change-handlers while loading so they don't fire hardware calls
        _brightness          = profile.Lighting.Brightness;
        _isLightingOn        = profile.Lighting.Brightness > 0;
        _turnOffOnDisplayOff = profile.Lighting.TurnOffOnDisplayOff;
        _turnOffOnIdle       = profile.Lighting.TurnOffOnIdle;
        _idleMinutes         = profile.Lighting.IdleMinutes;
        _selectedEffect      = profile.Lighting.Effect;
        _lastNonZeroBrightness = profile.Lighting.Brightness > 0
            ? profile.Lighting.Brightness
            : 100;

        OnPropertyChanged(nameof(Brightness));
        OnPropertyChanged(nameof(IsLightingOn));
        OnPropertyChanged(nameof(TurnOffOnDisplayOff));
        OnPropertyChanged(nameof(TurnOffOnIdle));
        OnPropertyChanged(nameof(IdleMinutes));
        OnPropertyChanged(nameof(SelectedEffect));

        HasUnsavedChanges = false;
    }

    // ── Property change handlers → live hardware updates ─────────────────────

    partial void OnBrightnessChanged(int value)
    {
        HasUnsavedChanges = true;

        if (value > 0)
        {
            _lastNonZeroBrightness = value;
            // Keep toggle in sync (don't trigger OnIsLightingOnChanged recursion)
            if (!_isLightingOn)
            {
                _isLightingOn = true;
                OnPropertyChanged(nameof(IsLightingOn));
            }
        }

        // Use ActiveDevice — LED device on PID 0x0098 is the same underlying device.
        // The SendFaf (fire-and-forget) path in MouseDevice means this is non-blocking.
        var device = _mouseService.ActiveDevice;
        if (device?.IsConnected == true)
        {
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
            // Turn off: set brightness to 0 and send LED-off effect
            _ = device.SetBrightnessAsync(0, CancellationToken.None);
            _ = device.SetLightingEnabledAsync(false, CancellationToken.None);
        }
        else
        {
            // Restore previous brightness and re-apply the selected effect
            var restoreTo = Math.Clamp(_lastNonZeroBrightness, 1, 100);
            _brightness = restoreTo;
            OnPropertyChanged(nameof(Brightness));

            _ = device.SetBrightnessAsync(restoreTo, CancellationToken.None);
            _ = device.SetLightingEnabledAsync(true, CancellationToken.None);
            _ = device.SetLightingEffectAsync(_selectedEffect, CancellationToken.None);
        }
    }

    partial void OnSelectedEffectChanged(LightingEffectType value)
    {
        HasUnsavedChanges = true;

        var device = _mouseService.ActiveDevice;
        if (device?.IsConnected == true && _isLightingOn)
        {
            _ = device.SetLightingEffectAsync(value, CancellationToken.None);
        }
    }

    partial void OnTurnOffOnDisplayOffChanged(bool value) => HasUnsavedChanges = true;
    partial void OnTurnOffOnIdleChanged(bool value)       => HasUnsavedChanges = true;
    partial void OnIdleMinutesChanged(int value)          => HasUnsavedChanges = true;

    // ── Save command ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var profile = _mouseService.ActiveProfile;
        if (profile == null) return;

        profile.Lighting.Brightness          = Brightness;
        profile.Lighting.Effect              = SelectedEffect;
        profile.Lighting.TurnOffOnDisplayOff = TurnOffOnDisplayOff;
        profile.Lighting.TurnOffOnIdle       = TurnOffOnIdle;
        profile.Lighting.IdleMinutes         = IdleMinutes;

        await _mouseService.SaveAndApplyProfileAsync(profile);
        HasUnsavedChanges = false;
    }
}
