using System.Text.Json.Serialization;

namespace DeathAdderManager.Core.Domain.Models;

/// <summary>
/// Represents one DPI stage on the mouse.
/// The DeathAdder Essential supports up to 5 stages stored in hardware.
/// </summary>
public sealed class DpiStage
{
    public const int MinDpi    = 200;
    public const int MaxDpi    = 6400;
    public const int MaxStages = 5;

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("dpi")]
    public int Dpi { get; set; } = 800;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    public DpiStage() { }

    public DpiStage(int index, int dpi, bool enabled = true)
    {
        Index   = index;
        Dpi     = Clamp(dpi);
        Enabled = enabled;
        Label   = $"{dpi} DPI";
    }

    public static int  Clamp(int dpi)    => Math.Clamp(dpi, MinDpi, MaxDpi);
    public static bool IsValid(int dpi)  => dpi >= MinDpi && dpi <= MaxDpi;

    /// <summary>Default 5-stage profile matching Razer factory defaults.</summary>
    public static List<DpiStage> CreateDefaults() => new()
    {
        new DpiStage(0, 1600) { Label = "Default (1600)" },
        new DpiStage(1, 800),
        new DpiStage(2, 400),
        new DpiStage(3, 3200),
        new DpiStage(4, 6400),
    };
}
