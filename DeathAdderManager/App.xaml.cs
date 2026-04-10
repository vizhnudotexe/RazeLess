using System.IO;
using System.Windows;
using DeathAdderManager.Application;
using DeathAdderManager.Core.Interfaces;
using DeathAdderManager.Infrastructure.Device;
using DeathAdderManager.Infrastructure.Hid;
using DeathAdderManager.Infrastructure.Persistence;
using DeathAdderManager.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private static readonly string AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeathAdderManager");
    private static readonly string LogFile = Path.Combine(AppDataDir, "app.log");
    private static readonly string ErrorLogFile = Path.Combine(AppDataDir, "error.log");
    private static readonly string ProjectDirLogFile = Path.Combine(Directory.GetCurrentDirectory(), "deathadder_debug.log");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Setup global exception handling
        SetupExceptionHandling();

        Directory.CreateDirectory(AppDataDir);
        var timestamp = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === APP START ===\n";
        File.AppendAllText(LogFile, timestamp);
        File.AppendAllText(ErrorLogFile, timestamp);
        try { File.AppendAllText(ProjectDirLogFile, timestamp); } catch { }

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Information);
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddProvider(new FileLoggerProvider(LogFile, LogLevel.Information));
                logging.AddProvider(new FileLoggerProvider(ErrorLogFile, LogLevel.Warning));
                logging.AddProvider(new FileLoggerProvider(ProjectDirLogFile, LogLevel.Debug));
            })
            .ConfigureServices((_, services) =>
            {
                // Infrastructure
                services.AddSingleton<HidDeviceWatcher>();
                services.AddSingleton<IDeviceFingerprintService, DeviceFingerprintService>();
                services.AddSingleton<IProfileRepository, JsonProfileRepository>();

                // Application services
                services.AddSingleton<MouseService>();
                services.AddSingleton<IMouseService>(sp => sp.GetRequiredService<MouseService>());
                services.AddHostedService(sp => sp.GetRequiredService<MouseService>());
                services.AddSingleton<IStartupRestoreService, StartupRestoreService>();
                
                // Button remapping hook
                services.AddSingleton<MouseHookService>();
                
                // Lighting automation
                services.AddSingleton<LightingAutomationService>();

                // ViewModels
                services.AddSingleton<PerformanceViewModel>();
                services.AddSingleton<LightingViewModel>();
                services.AddSingleton<ButtonsViewModel>();
                services.AddSingleton<ProfilesViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<MainViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();

        // This MUST be set so WPF doesn't close the app early
        this.MainWindow = mainWindow;

        // Log window lifecycle for debugging invisible-window issues
        try
        {
            var logger = _host.Services.GetService<ILogger<App>>();
            logger?.LogInformation("MainWindow constructed, calling Show()");
        }
        catch { }

        try
        {
            File.AppendAllText(ProjectDirLogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MainWindow constructed, calling Show()\n");
        }
        catch { }

        mainWindow.Show();

        try
        {
            var logger = _host.Services.GetService<ILogger<App>>();
            logger?.LogInformation("MainWindow.Show() completed");
        }
        catch { }

        try
        {
            File.AppendAllText(ProjectDirLogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MainWindow.Show() completed\n");
        }
        catch { }

        // Optional debug dialog to force UI to appear when environment var is set.
        try
        {
            if (string.Equals(Environment.GetEnvironmentVariable("DA_SHOW_DIALOG"), "1"))
            {
                MessageBox.Show("MainWindow shown - click OK to continue.", "Startup debug", MessageBoxButton.OK, MessageBoxImage.Information);
                try { File.AppendAllText(ProjectDirLogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Debug dialog shown\n"); } catch { }
            }
        }
        catch { }

        // Now start the background services
        await _host.StartAsync();

        // Ensure the window is visible and brought to front (diagnostic)
        try
        {
            mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            mainWindow.Left = 100;
            mainWindow.Top = 100;
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Topmost = true;
            mainWindow.Activate();
            mainWindow.Topmost = false;
            try { File.AppendAllText(ProjectDirLogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MainWindow forced visible and activated\n"); } catch { }
        }
        catch { }

        // Start mouse hook for button remapping
        var mouseHook = _host.Services.GetRequiredService<MouseHookService>();
        mouseHook.Start();
        
        // Start lighting automation service
        var lightingAutomation = _host.Services.GetRequiredService<LightingAutomationService>();
        lightingAutomation.Start();

        // Load profiles in background
        var profilesVm = _host.Services.GetRequiredService<ProfilesViewModel>();
        await profilesVm.LoadProfilesAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            var mouseHook = _host.Services.GetService<MouseHookService>();
            mouseHook?.Dispose();
            
            var lightingAutomation = _host.Services.GetService<LightingAutomationService>();
            lightingAutomation?.Dispose();
            
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }

    private void SetupExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) => 
            LogUnhandledException((Exception)e.ExceptionObject, "AppDomain.CurrentDomain.UnhandledException");

        DispatcherUnhandledException += (s, e) => 
        {
            LogUnhandledException(e.Exception, "DispatcherUnhandledException");
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, e) => 
        {
            LogUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };
    }

    private void LogUnhandledException(Exception exception, string source)
    {
        var message = $"Unhandled exception in {source}";
        try
        {
            var logger = _host?.Services.GetService<ILogger<App>>();
            if (logger != null)
            {
                logger.LogCritical(exception, message);
            }
            else
            {
                File.AppendAllText(ErrorLogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [CRITICAL] {message}: {exception}\n");
            }
        }
        catch { }
    }
}
