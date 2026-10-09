using System.Text.Json.Serialization;
using Lingarr.Server.Services.Plugins;

namespace Lingarr.Server.Providers
{
    public class LogEntry
    {
        [JsonIgnore]
        public LogLevel LogLevel { get; set; }

        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("logLevel")]
        public string LogLevelString => LogLevel.ToString();

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("exception")]
        public string? ExceptionText { get; set; }

        [JsonPropertyName("hint")]
        public string? Hint { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("formattedTime")]
        public string FormattedTime => Timestamp.ToString("HH:mm:ss");

        [JsonPropertyName("formattedDate")]
        public string FormattedDate => Timestamp.ToString("yyyy-MM-dd");

        [JsonPropertyName("formattedSource")]
        public string FormattedSource => Category?.Split('.').LastOrDefault() ?? Category ?? string.Empty;
    }

    public static class InMemoryLogSink
    {
        private static readonly LogBuffer Buffer = new();

        public static LogEntry AddLog(LogEntry logEntry) => Buffer.Add(logEntry);

        public static LogPage Page(long? before, long? after, int limit, LogLevel? minimum) =>
            Buffer.Page(before, after, limit, minimum);

        public static Task<IReadOnlyList<LogEntry>> WaitAsync(
            long after,
            LogLevel? minimum,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Buffer.WaitAsync(after, minimum, timeout, cancellationToken);

        public static void Clear() => Buffer.Clear();

        public static long LatestId => Buffer.LatestId;
    }

    public class InMemoryLogger : ILogger
    {
        private readonly string _categoryName;

        public InMemoryLogger(string categoryName)
        {
            _categoryName = categoryName;
        }

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            InMemoryLogSink.AddLog(new LogEntry
            {
                LogLevel = logLevel,
                Message = message,
                ExceptionText = exception?.ToString(),
                Timestamp = DateTime.UtcNow,
                Category = _categoryName
            });
            PluginLogHub.Publish(logLevel.ToString(), _categoryName, message);
        }
    }

    public class InMemoryLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new InMemoryLogger(categoryName);
        }

        public void Dispose()
        {
        }
    }
}