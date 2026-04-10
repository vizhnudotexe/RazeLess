using System.IO;
using Microsoft.Extensions.Logging;

namespace DeathAdderManager;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly LogLevel _minLevel;

    public FileLoggerProvider(string path, LogLevel minLevel = LogLevel.Information)
    {
        _path = path;
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(_path, categoryName, _minLevel);

    public void Dispose() { }
}

public sealed class FileLogger : ILogger
{
    private readonly string _path;
    private readonly string _category;
    private readonly LogLevel _minLevel;
    private static readonly object _lock = new();

    public FileLogger(string path, string category, LogLevel minLevel)
    {
        _path = path;
        _category = category;
        _minLevel = minLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{logLevel,-11}] [{_category}] {formatter(state, exception)}";
        if (exception != null) msg += "\n" + exception;
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_path, msg + "\n");
            }
            catch { /* Best effort logging */ }
        }
    }
}
