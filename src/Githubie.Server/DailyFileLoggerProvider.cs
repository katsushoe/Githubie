using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Githubie.Server;

/// <summary>
/// `{logsDirectory}/githubie-yyyyMMdd.log`へ日次ローテーションするシンプルな<see cref="ILoggerProvider"/>です。
/// </summary>
public sealed class DailyFileLoggerProvider(string logsDirectory) : ILoggerProvider
{
    private readonly string _logsDirectory = logsDirectory;
    private readonly ConcurrentDictionary<string, DailyFileLogger> _loggers = new(StringComparer.Ordinal);
    private readonly Lock _writeLock = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new DailyFileLogger(name, this));

    public void Dispose() => _loggers.Clear();

    internal void Write(string line)
    {
        Directory.CreateDirectory(_logsDirectory);
        var path = Path.Combine(_logsDirectory, $"githubie-{DateTime.UtcNow:yyyyMMdd}.log");

        lock (_writeLock)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private sealed class DailyFileLogger(string categoryName, DailyFileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            provider.Write($"{DateTimeOffset.UtcNow:O} [{logLevel}] {categoryName} {message}");
        }
    }
}
