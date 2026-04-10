using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeathAdderManager.Core.Domain.Enums;
using DeathAdderManager.Core.Domain.Models;
using DeathAdderManager.Core.Interfaces;

namespace DeathAdderManager.ViewModels;

public sealed class ButtonRowViewModel : ObservableObject
{
    public string      ButtonName  { get; }
    public MouseButton Button      { get; }

    private string _currentAction = "Default";
    public string CurrentAction
    {
        get => _currentAction;
        set { SetProperty(ref _currentAction, value); }
    }

    public ButtonRowViewModel(string name, MouseButton btn)
    {
        ButtonName = name;
        Button     = btn;
    }
}

public sealed partial class ButtonsViewModel : ObservableObject
{
    private readonly IMouseService _mouseService;

    [ObservableProperty] private bool _hasUnsavedChanges = false;
    [ObservableProperty] private ButtonRowViewModel? _selectedRow;

    [RelayCommand]
    private void SelectButton(string name)
    {
        SelectedRow = ButtonRows.FirstOrDefault(r => r.ButtonName == name);
        if (SelectedRow == null && name == "Scroll Click") // Handling middle click fallback if named differently
            SelectedRow = ButtonRows.FirstOrDefault(r => r.ButtonName == "Middle Click");
    }

    [RelayCommand]
    private void CloseSelection() => SelectedRow = null;

    [RelayCommand]
    private void ApplyAction(string actionName)
    {
        if (SelectedRow != null)
        {
            SelectedRow.CurrentAction = actionName;
        }
    }

    // All remappable buttons
    public ObservableCollection<ButtonRowViewModel> ButtonRows { get; } = new()
    {
        new("Right Click",   MouseButton.RightButton),
        new("Scroll Click",  MouseButton.MiddleButton),
        new("Side Button 1 (Back)",    MouseButton.SideButton1),
        new("Side Button 2 (Forward)", MouseButton.SideButton2),
        new("Scroll Up",     MouseButton.ScrollUp),
        new("Scroll Down",   MouseButton.ScrollDown),
    };

    // Available action strings for combo boxes
    public List<string> AvailableActions { get; } = new()
    {
        "Default",
        "Disabled",
        "Left Click",
        "Right Click",
        "Middle Click",
        "Side Button 1",
        "Side Button 2",
        "Scroll Up",
        "Scroll Down",
        "Volume Up",
        "Volume Down",
        "Mute",
        "Play/Pause",
        "Next Track",
        "Prev Track",
        "Browser Back",
        "Browser Forward",
    };

    public ButtonsViewModel(IMouseService mouseService)
    {
        _mouseService = mouseService;

        foreach (var row in ButtonRows)
            row.PropertyChanged += (_, _) => HasUnsavedChanges = true;
    }

    public void LoadFromProfile(MouseProfile profile)
    {
        void SetRow(MouseButton btn, ButtonAction action)
        {
            var row = ButtonRows.FirstOrDefault(r => r.Button == btn);
            if (row != null) row.CurrentAction = action.DisplayName;
        }

        SetRow(MouseButton.RightButton,  profile.Buttons.RightButton);
        SetRow(MouseButton.MiddleButton, profile.Buttons.MiddleButton);
        SetRow(MouseButton.SideButton1,  profile.Buttons.SideButton1);
        SetRow(MouseButton.SideButton2,  profile.Buttons.SideButton2);
        SetRow(MouseButton.ScrollUp,     profile.Buttons.ScrollUp);
        SetRow(MouseButton.ScrollDown,   profile.Buttons.ScrollDown);
        HasUnsavedChanges = false;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var profile = _mouseService.ActiveProfile;
        if (profile == null) return;

        foreach (var row in ButtonRows)
        {
            var action = ActionFromDisplayName(row.CurrentAction);
            profile.Buttons.SetAction(row.Button, action);
        }

        await _mouseService.SaveAndApplyProfileAsync(profile);
        HasUnsavedChanges = false;
    }

    private static ButtonAction ActionFromDisplayName(string name) => name switch
    {
        "Disabled"        => ButtonAction.Disabled(),
        "Left Click"      => ButtonAction.FromMouseButton(MouseButton.LeftButton),
        "Right Click"     => ButtonAction.FromMouseButton(MouseButton.RightButton),
        "Middle Click"    => ButtonAction.FromMouseButton(MouseButton.MiddleButton),
        "Side Button 1"   => ButtonAction.FromMouseButton(MouseButton.SideButton1),
        "Side Button 2"   => ButtonAction.FromMouseButton(MouseButton.SideButton2),
        "Volume Up"       => ButtonAction.FromVirtualKey(0xAF, "Volume Up"),
        "Volume Down"     => ButtonAction.FromVirtualKey(0xAE, "Volume Down"),
        "Mute"            => ButtonAction.FromVirtualKey(0xAD, "Mute"),
        "Play/Pause"      => ButtonAction.FromVirtualKey(0xB3, "Play/Pause"),
        "Next Track"      => ButtonAction.FromVirtualKey(0xB0, "Next Track"),
        "Prev Track"      => ButtonAction.FromVirtualKey(0xB1, "Prev Track"),
        "Browser Back"    => ButtonAction.FromVirtualKey(0xA6, "Browser Back"),
        "Browser Forward" => ButtonAction.FromVirtualKey(0xA7, "Browser Forward"),
        _                 => ButtonAction.Default(),
    };
}
