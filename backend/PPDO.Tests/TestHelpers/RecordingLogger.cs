using Microsoft.Extensions.Logging;

namespace PPDO.Tests.TestHelpers;

/// <summary>
/// An <see cref="ILogger{T}"/> that keeps every line with the scopes open when it was written
/// (PPDO-229). Scope state is read the way Application Insights and Seq read it: a list of key/value
/// pairs becomes named values; anything else is kept under <c>"Scope"</c> as a plain value.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public sealed record Line(LogLevel Level, string Message, Exception? Exception, IReadOnlyDictionary<string, object?> Scopes)
    {
        /// <summary>The named scope value, or null when no open scope carried it.</summary>
        public object? Scope(string key) => Scopes.TryGetValue(key, out object? value) ? value : null;
    }

    private readonly List<object> _openScopes = [];

    public List<Line> Lines { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        _openScopes.Add(state);
        return new Closer(() => _openScopes.Remove(state));
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Dictionary<string, object?> scopes = [];
        foreach (object scope in _openScopes)
        {
            if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                foreach (KeyValuePair<string, object> pair in pairs) scopes[pair.Key] = pair.Value;
            else
                scopes["Scope"] = scope;
        }
        Lines.Add(new Line(logLevel, formatter(state, exception), exception, scopes));
    }

    private sealed class Closer(Action close) : IDisposable
    {
        public void Dispose() => close();
    }
}
