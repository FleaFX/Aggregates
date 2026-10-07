using Microsoft.Extensions.Logging;

namespace Aggregates.Testing;

/// <summary>
/// Writes log messages to the xUnit output of the current test.
/// </summary>
sealed class TestOutputLoggerProvider(ITestOutputHelper output) : ILoggerProvider {
    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new TestOutputLogger(output, categoryName);

    /// <inheritdoc/>
    public void Dispose() { }

    sealed class TestOutputLogger(ITestOutputHelper output, string categoryName) : ILogger {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            var message = $"[{DateTime.Now:HH:mm:ss.fff} {logLevel}] {categoryName}: {formatter(state, exception)}";
            if (exception is not null)
                message += Environment.NewLine + exception;
            try {
                output.WriteLine(message);
            } catch (InvalidOperationException) {
                // Background services can still log after the test has finished.
            }
        }
    }
}
