using System.IO;
using System.Text.Json;
using DeathAdderManager.Core.Domain.Models;
using DeathAdderManager.Core.Interfaces;
using DeathAdderManager.Shared;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Infrastructure.Persistence;

/// <summary>
/// Stores profiles as individual JSON files in the app data folder.
/// Uses atomic write (write to .tmp then rename) to prevent corruption.
///
/// File layout:
///   %APPDATA%\DeathAdderManager\
///     profiles\
///       {guid}.json         ← one file per profile
///     last_active.json      ← maps fingerprint_hash -> profile_id
///     known_devices.json    ← DeviceFingerprintService owns this
/// </summary>
public sealed class JsonProfileRepository : IProfileRepository
{
    private readonly string                        _profilesDir;
    private readonly string                        _lastActivePath;
    private readonly ILogger<JsonProfileRepository> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public JsonProfileRepository(ILogger<JsonProfileRepository> logger)
    {
        _logger          = logger;
        _profilesDir     = Path.Combine(AppDataFolder.Path, "profiles");
        _lastActivePath  = Path.Combine(AppDataFolder.Path, "last_active.json");
        Directory.CreateDirectory(_profilesDir);
    }

    public async Task<List<MouseProfile>> GetAllAsync()
    {
        var list = new List<MouseProfile>();

        foreach (var file in Directory.EnumerateFiles(_profilesDir, "*.json"))
        {
            var profile = await ReadProfileFileAsync(file);
            if (profile != null) list.Add(profile);
        }

        // Always ensure at least one Default profile exists
        if (list.Count == 0)
        {
            var def = MouseProfile.CreateDefault();
            await SaveAsync(def);
            list.Add(def);
        }

        return list.OrderBy(p => p.CreatedAt).ToList();
    }

    public async Task<MouseProfile?> GetByIdAsync(Guid id)
    {
        var path = ProfilePath(id);
        return File.Exists(path) ? await ReadProfileFileAsync(path) : null;
    }

    public async Task SaveAsync(MouseProfile profile)
    {
        profile.Touch();
        await AtomicJsonWriteAsync(ProfilePath(profile.Id), profile);
        _logger.LogDebug("Saved profile '{Name}' ({Id})", profile.Name, profile.Id);
    }

    public Task DeleteAsync(Guid id)
    {
        var path = ProfilePath(id);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<Guid?> GetLastActiveProfileIdAsync(string deviceFingerprintHash)
    {
        var map = await LoadLastActiveMapAsync();
        return map.TryGetValue(deviceFingerprintHash, out var id) ? id : null;
    }

    public async Task SetLastActiveProfileIdAsync(string deviceFingerprintHash, Guid profileId)
    {
        var map = await LoadLastActiveMapAsync();
        map[deviceFingerprintHash] = profileId;
        await AtomicJsonWriteAsync(_lastActivePath, map);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<MouseProfile?> ReadProfileFileAsync(string path)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<MouseProfile>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Corrupt profile file: {Path} — skipping", path);
            File.Move(path, path + ".corrupt", overwrite: true);
            return null;
        }
    }

    private async Task<Dictionary<string, Guid>> LoadLastActiveMapAsync()
    {
        if (!File.Exists(_lastActivePath))
            return new Dictionary<string, Guid>();

        try
        {
            var json = await File.ReadAllTextAsync(_lastActivePath);
            return JsonSerializer.Deserialize<Dictionary<string, Guid>>(json, JsonOpts) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read last_active.json — resetting");
            return new();
        }
    }

    private static async Task AtomicJsonWriteAsync<T>(string path, T data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp  = path + ".tmp";
        var json = JsonSerializer.Serialize(data, JsonOpts);
        await File.WriteAllTextAsync(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    private string ProfilePath(Guid id) =>
        Path.Combine(_profilesDir, $"{id:N}.json");
}
