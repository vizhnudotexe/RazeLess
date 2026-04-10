using Microsoft.Win32;
using DeathAdderManager.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager.Application;

/// <summary>
/// Manages Windows startup registry entry and handles profile restoration on boot.
/// Registry key: HKCU\Software\Microsoft\Windows\CurrentVersion\Run
/// </summary>
public sealed class StartupRestoreService : IStartupRestoreService
{
    private const string RegKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegAppName = "DeathAdderManager";

    private readonly IMouseService              _mouseService;
    private readonly ILogger<StartupRestoreService> _logger;

    public StartupRestoreService(IMouseService mouseService, ILogger<StartupRestoreService> logger)
    {
        _mouseService = mouseService;
        _logger       = logger;
    }

    /// <summary>
    /// Called at application boot. The MouseService's BackgroundService already
    /// handles profile restoration on connect, so this just logs intent.
    /// </summary>
    public async Task<bool> RestoreLastProfileAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("StartupRestore: awaiting device connection for auto-restore");
        // Profile restoration is event-driven inside MouseService
        await Task.CompletedTask;
        return _mouseService.IsDeviceConnected;
    }

    public void SetRunOnStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKeyPath, writable: true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
                key.SetValue(RegAppName, $"\"{exePath}\"");
                _logger.LogInformation("Run-on-startup enabled");
            }
            else
            {
                key.DeleteValue(RegAppName, throwOnMissingValue: false);
                _logger.LogInformation("Run-on-startup disabled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set startup registry entry");
        }
    }

    public bool IsRunOnStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKeyPath);
            return key?.GetValue(RegAppName) != null;
        }
        catch
        {
            return false;
        }
    }
}
