using System.Text.Json.Serialization;
using DeathAdderManager.Core.Domain.Enums;

namespace DeathAdderManager.Core.Domain.Models;

/// <summary>
/// Describes what action a physical button produces.
/// </summary>
public sealed class ButtonAction
{
    [JsonPropertyName("type")]
    public ButtonActionType Type { get; set; } = ButtonActionType.Default;

    /// <summary>
    /// For MouseButton type: target MouseButton enum value.
    /// For KeyboardKey type: Windows Virtual-Key code (VK_*).
    /// For MediaKey / BrowserKey: platform-specific code.
    /// For Macro: macro ID string.
    /// Unused for Default/Disabled.
    /// </summary>
    [JsonPropertyName("parameter")]
    public string Parameter { get; set; } = string.Empty;

    /// <summary>Human-readable description shown in the UI combo box.</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "Default";

    public static ButtonAction Default()  => new() { Type = ButtonActionType.Default,  DisplayName = "Default" };
    public static ButtonAction Disabled() => new() { Type = ButtonActionType.Disabled, DisplayName = "Disabled" };

    public static ButtonAction FromMouseButton(MouseButton btn) => new()
    {
        Type        = ButtonActionType.MouseButton,
        Parameter   = ((int)btn).ToString(),
        DisplayName = btn.ToString()
    };

    public static ButtonAction FromVirtualKey(int vk, string keyName) => new()
    {
        Type        = ButtonActionType.KeyboardKey,
        Parameter   = vk.ToString(),
        DisplayName = keyName
    };
}

/// <summary>
/// Maps each remappable physical button to its current ButtonAction.
/// LeftButton is always hardware default and never stored here.
/// </summary>
public sealed class ButtonMappings
{
    [JsonPropertyName("rightButton")]
    public ButtonAction RightButton { get; set; } = ButtonAction.Default();

    [JsonPropertyName("middleButton")]
    public ButtonAction MiddleButton { get; set; } = ButtonAction.Default();

    [JsonPropertyName("sideButton1")]
    public ButtonAction SideButton1 { get; set; } = ButtonAction.Default();

    [JsonPropertyName("sideButton2")]
    public ButtonAction SideButton2 { get; set; } = ButtonAction.Default();

    [JsonPropertyName("scrollUp")]
    public ButtonAction ScrollUp { get; set; } = ButtonAction.Default();

    [JsonPropertyName("scrollDown")]
    public ButtonAction ScrollDown { get; set; } = ButtonAction.Default();

    public static ButtonMappings CreateDefault() => new();

    /// <summary>Returns the action for a given button (Left not supported).</summary>
    public ButtonAction GetAction(MouseButton btn) => btn switch
    {
        MouseButton.RightButton  => RightButton,
        MouseButton.MiddleButton => MiddleButton,
        MouseButton.SideButton1  => SideButton1,
        MouseButton.SideButton2  => SideButton2,
        MouseButton.ScrollUp     => ScrollUp,
        MouseButton.ScrollDown   => ScrollDown,
        _                        => ButtonAction.Default()
    };

    public void SetAction(MouseButton btn, ButtonAction action)
    {
        switch (btn)
        {
            case MouseButton.RightButton:  RightButton  = action; break;
            case MouseButton.MiddleButton: MiddleButton = action; break;
            case MouseButton.SideButton1:  SideButton1  = action; break;
            case MouseButton.SideButton2:  SideButton2  = action; break;
            case MouseButton.ScrollUp:     ScrollUp     = action; break;
            case MouseButton.ScrollDown:   ScrollDown   = action; break;
        }
    }
}
