namespace DeathAdderManager.Core.Domain.Enums;

/// <summary>
/// Physical buttons on the DeathAdder Essential.
/// LeftButton cannot be remapped (safety/usability constraint).
/// </summary>
public enum MouseButton
{
    LeftButton   = 1,
    RightButton  = 2,
    MiddleButton = 3,
    SideButton1  = 4,   // Back (thumb, lower)
    SideButton2  = 5,   // Forward (thumb, upper)
    ScrollUp     = 6,
    ScrollDown   = 7,
}

/// <summary>
/// High-level category of what a button action produces.
/// </summary>
public enum ButtonActionType
{
    Default      = 0,   // Restore hardware default
    Disabled     = 1,   // Swallow the click
    MouseButton  = 2,   // Remap to another mouse button
    KeyboardKey  = 3,   // Single keypress
    MediaKey     = 4,   // Play/pause, volume, etc.
    BrowserKey   = 5,   // Back, forward, etc.
    Macro        = 6,   // Placeholder - future expansion
}

/// <summary>
/// Standard polling rates supported by DeathAdder Essential over USB.
/// </summary>
public enum PollingRate
{
    Hz125  = 125,
    Hz500  = 500,
    Hz1000 = 1000,
}

/// <summary>
/// Lighting effect mode. DeathAdder Essential supports static only (no Chroma).
/// </summary>
public enum LightingEffect
{
    Static = 0,
    Off    = 1,
}
