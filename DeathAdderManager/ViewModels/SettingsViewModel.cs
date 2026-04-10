using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeathAdderManager.Core.Interfaces;

namespace DeathAdderManager.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IStartupRestoreService _startupService;

    [ObservableProperty] private bool   _runOnStartup;
    [ObservableProperty] private string _appDataPath = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public SettingsViewModel(IStartupRestoreService startupService)
    {
        _startupService = startupService;
        RunOnStartup    = _startupService.IsRunOnStartupEnabled();
        AppDataPath     = Shared.AppDataFolder.Path;
    }

    partial void OnRunOnStartupChanged(bool value)
    {
        _startupService.SetRunOnStartup(value);
        StatusMessage = value ? "Will run on Windows startup" : "Startup disabled";
    }

    [RelayCommand]
    private void OpenAppDataFolder()
    {
        System.Diagnostics.Process.Start("explorer.exe", AppDataPath);
    }
}
