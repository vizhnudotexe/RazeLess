using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeathAdderManager.Core.Domain.Models;
using DeathAdderManager.Core.Interfaces;

namespace DeathAdderManager.ViewModels;

/// <summary>
/// Root ViewModel. Owns navigation state and device connection status.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IMouseService _mouseService;

    [ObservableProperty] private string _deviceStatus    = "No device detected";
    [ObservableProperty] private string _profileName     = "—";
    [ObservableProperty] private bool   _isConnected     = false;
    [ObservableProperty] private int    _selectedTabIndex = 0;

    public PerformanceViewModel PerformanceVm { get; }
    public LightingViewModel    LightingVm    { get; }
    public ButtonsViewModel     ButtonsVm     { get; }
    public ProfilesViewModel    ProfilesVm    { get; }
    public SettingsViewModel    SettingsVm    { get; }

    public MainViewModel(
        IMouseService       mouseService,
        PerformanceViewModel performanceVm,
        LightingViewModel    lightingVm,
        ButtonsViewModel     buttonsVm,
        ProfilesViewModel    profilesVm,
        SettingsViewModel    settingsVm)
    {
        _mouseService = mouseService;
        PerformanceVm = performanceVm;
        LightingVm    = lightingVm;
        ButtonsVm     = buttonsVm;
        ProfilesVm    = profilesVm;
        SettingsVm    = settingsVm;

        _mouseService.DeviceChanged  += OnDeviceChanged;
        _mouseService.ProfileChanged += OnProfileChanged;

        // Sync initial state
        IsConnected  = _mouseService.IsDeviceConnected;
        DeviceStatus = IsConnected
            ? _mouseService.ActiveDevice!.Fingerprint.DisplayName
            : "No device detected";
    }

    private void OnDeviceChanged(object? sender, IMouseDevice? device)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            IsConnected  = device?.IsConnected ?? false;
            DeviceStatus = IsConnected ? device!.Fingerprint.DisplayName : "No device detected";
        });
    }

    private void OnProfileChanged(object? sender, MouseProfile? profile)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            ProfileName = profile?.Name ?? "—";
            if (profile != null)
            {
                PerformanceVm.LoadFromProfile(profile);
                LightingVm.LoadFromProfile(profile);
                ButtonsVm.LoadFromProfile(profile);
            }
        });
    }
}
