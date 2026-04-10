using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeathAdderManager.Core.Domain.Models;
using DeathAdderManager.Core.Interfaces;

namespace DeathAdderManager.ViewModels;

public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly IMouseService _mouseService;

    [ObservableProperty] private MouseProfile? _selectedProfile;
    [ObservableProperty] private string        _newProfileName   = string.Empty;
    [ObservableProperty] private string        _statusMessage    = string.Empty;

    public ObservableCollection<MouseProfile> Profiles { get; } = new();

    public ProfilesViewModel(IMouseService mouseService)
    {
        _mouseService = mouseService;
        _mouseService.ProfileChanged += (_, p) =>
            App.Current.Dispatcher.Invoke(() =>
            {
                if (p != null) SelectedProfile = Profiles.FirstOrDefault(x => x.Id == p.Id);
            });
    }

    public async Task LoadProfilesAsync()
    {
        var all = await _mouseService.GetProfilesAsync();
        Profiles.Clear();
        foreach (var p in all) Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private async Task SwitchProfileAsync()
    {
        if (SelectedProfile == null) return;
        await _mouseService.SwitchProfileAsync(SelectedProfile.Id);
        StatusMessage = $"Switched to '{SelectedProfile.Name}'";
    }

    [RelayCommand]
    private async Task CreateProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfileName)) return;
        await _mouseService.CreateProfileAsync(NewProfileName);
        NewProfileName = string.Empty;
        await LoadProfilesAsync();
        StatusMessage = "Profile created";
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile == null || Profiles.Count <= 1) return;
        await _mouseService.DeleteProfileAsync(SelectedProfile.Id);
        await LoadProfilesAsync();
        StatusMessage = "Profile deleted";
    }
}
