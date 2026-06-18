using System.Text.RegularExpressions;

namespace MangosSuperUI.Services;

/// <summary>
/// A bounded, thread-safe in-memory ring of recent brain/bridge log lines, fed by
/// <see cref="BotLogBufferProvider"/>. The Bots "Live" tab polls a per-bot slice of this
/// (filtered by bot name) only while a bot is being watched — nothing is pushed otherwise.
/// This is a debug surface, not a durable log; journald remains the system of record.
/// </summary>
public sealed class BotLogBuffer
{
    public readonly record struct Entry(long Seq, DateTime Utc, string Message);

    private readonly object _lock = new();
    private readonly Entry[] _ring;
    private int _head;       // next write index
    private int _count;
    private long _seq;

    public BotLogBuffer(int capacity = 8000)
    {
        _ring = new Entry[Math.Max(256, capacity)];
    }

    public void Append(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        lock (_lock)
        {
            _ring[_head] = new Entry(++_seq, DateTime.UtcNow, message);
            _head = (_head + 1) % _ring.Length;
            if (_count < _ring.Length) _count++;
        }
    }

    /// <summary>
    /// Lines whose message mentions <paramref name="name"/> (whole-word, case-insensitive)
    /// with Seq &gt; <paramref name="afterSeq"/>, oldest-first, capped at <paramref name="max"/>.
    /// Returns the current max seq so the caller advances its cursor even past non-matching lines.
    /// A null/empty name returns everything (unfiltered).
    /// </summary>
    public (IReadOnlyList<Entry> Lines, long LastSeq) Query(string? name, long afterSeq, int max = 200)
    {
        Entry[] snapshot;
        long lastSeq;
        lock (_lock)
        {
            lastSeq = _seq;
            snapshot = new Entry[_count];
            int start = (_head - _count + _ring.Length) % _ring.Length;
            for (int i = 0; i < _count; i++)
                snapshot[i] = _ring[(start + i) % _ring.Length];
        }

        Regex? rx = string.IsNullOrWhiteSpace(name)
            ? null
            : new Regex($@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase);

        var outList = new List<Entry>();
        foreach (var e in snapshot)
        {
            if (e.Seq <= afterSeq) continue;
            if (rx != null && !rx.IsMatch(e.Message)) continue;
            outList.Add(e);
        }
        if (outList.Count > max)
            outList = outList.GetRange(outList.Count - max, max);

        return (outList, lastSeq);
    }
}

/// <summary>
/// ILoggerProvider that funnels the brain + bridge categories into <see cref="BotLogBuffer"/>.
/// Construct the buffer once, register it as a DI singleton, and add this provider to the
/// logging pipeline (see Program.cs). Only brain/bridge categories are captured.
/// </summary>
public sealed class BotLogBufferProvider : ILoggerProvider
{
    private readonly BotLogBuffer _buffer;
    public BotLogBufferProvider(BotLogBuffer buffer) => _buffer = buffer;

    public ILogger CreateLogger(string categoryName) => new BufferLogger(_buffer, categoryName);
    public void Dispose() { }

    private sealed class BufferLogger : ILogger
    {
        private readonly BotLogBuffer _buffer;
        private readonly bool _capture;

        public BufferLogger(BotLogBuffer buffer, string category)
        {
            _buffer = buffer;
            // Only the brain (BotLogic.*) and the host/bridge services emit per-bot lines.
            _capture = category.Contains("BotLogic")
                       || category.Contains("BotBrainService")
                       || category.Contains("BotBridgeService");
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => _capture && logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                                Func<TState, Exception?, string> formatter)
        {
            if (!_capture) return;
            var msg = formatter(state, exception);
            if (exception != null) msg += " | " + exception.Message;
            _buffer.Append(msg);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
