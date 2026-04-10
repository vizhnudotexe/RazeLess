using System.Text.Json.Serialization;
using DeathAdderManager.Core.Domain.Enums;

namespace DeathAdderManager.Core.Domain.Models;

/// <summary>
/// Complete snapshot of all mouse settings for one named profile.
/// This is the root aggregate that gets persisted and applied to hardware.
/// </summary>
public sealed class MouseProfile
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Which stage is currently active (0-based index into DpiStages).
    /// Hardware cycles through enabled stages when DPI button is pressed.
    /// </summary>
    [JsonPropertyName("activeDpiStage")]
    public int ActiveDpiStage { get; set; } = 0;

    [JsonPropertyName("dpiStages")]
    public List<DpiStage> DpiStages { get; set; } = DpiStage.CreateDefaults();

    [JsonPropertyName("pollingRate")]
    public PollingRate PollingRate { get; set; } = PollingRate.Hz1000;

    [JsonPropertyName("lighting")]
    public LightingSettings Lighting { get; set; } = LightingSettings.CreateDefault();

    [JsonPropertyName("buttons")]
    public ButtonMappings Buttons { get; set; } = ButtonMappings.CreateDefault();

    public void Touch() => UpdatedAt = DateTime.UtcNow;

    /// <summary>Factory default profile matching out-of-box Razer settings.</summary>
    public static MouseProfile CreateDefault() => new()
    {
        Name           = "Default",
        ActiveDpiStage = 0,
        DpiStages      = DpiStage.CreateDefaults(),
        PollingRate    = PollingRate.Hz1000,
        Lighting       = LightingSettings.CreateDefault(),
        Buttons        = ButtonMappings.CreateDefault(),
    };
}
