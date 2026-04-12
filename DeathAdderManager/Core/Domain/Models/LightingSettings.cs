using System.Text.Json.Serialization;
using DeathAdderManager.Core.Domain.Enums;

namespace DeathAdderManager.Core.Domain.Models;

/// <summary>
/// Lighting configuration for the DeathAdder Essential scroll wheel + logo LEDs.
/// </summary>
public sealed class LightingSettings
{
    public const int MinBrightness  = 0;
    public const int MaxBrightness  = 100;
    public const int MinIdleMinutes = 1;
    public const int MaxIdleMinutes = 15;

    /// <summary>Overall brightness 0-100. 0 = off, 100 = full.</summary>
    [JsonPropertyName("brightness")]
    public int Brightness { get; set; } = 100;

    /// <summary>Lighting effect type: Static, Breathing, or None.</summary>
    [JsonPropertyName("effect")]
    public LightingEffectType Effect { get; set; } = LightingEffectType.Static;

    /// <summary>If true, lighting turns off when the Windows display powers down.</summary>
    [JsonPropertyName("offOnDisplayOff")]
    public bool TurnOffOnDisplayOff { get; set; } = false;

    /// <summary>If true, lighting turns off after IdleMinutes of no mouse input.</summary>
    [JsonPropertyName("offOnIdle")]
    public bool TurnOffOnIdle { get; set; } = false;

    /// <summary>How many minutes of idle before the lighting turns off (1-15).</summary>
    [JsonPropertyName("idleMinutes")]
    public int IdleMinutes { get; set; } = 1;

    public static LightingSettings CreateDefault() => new()
    {
        Brightness          = 100,
        Effect              = LightingEffectType.Static,
        TurnOffOnDisplayOff = false,
        TurnOffOnIdle       = false,
        IdleMinutes         = 1,
    };

    public static int ClampBrightness(int v) => Math.Clamp(v, MinBrightness, MaxBrightness);
    public static int ClampIdle(int v)       => Math.Clamp(v, MinIdleMinutes, MaxIdleMinutes);

    /// <summary>Convert 0-100 brightness to a 0-255 hardware byte.</summary>
    public byte ToBrightnessHwByte() => (byte)(Brightness * 255 / 100);
}
