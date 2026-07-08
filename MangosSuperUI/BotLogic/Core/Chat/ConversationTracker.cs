using System.Collections.Concurrent;
using MangosSuperUI.BotLogic.Chat.Core;

namespace MangosSuperUI.BotLogic.Chat.Coordinator;

/// <summary>
/// Tier 0 — the live conversation window (CHAT_ARCHITECTURE §7.1). Per active
/// conversation key (botGuid, counterpartName, kind): ring buffer of the last
/// tier0.window_lines lines (in+out, default 10), TTL tier0.ttl_min since the last line
/// (default 10 min). RAM only, lost on restart BY DESIGN (Tier 2 survives). Provides
/// the {live_window} prompt block and the threadActive urge signal (§9.2, consumed C4).
/// </summary>
public class ConversationTracker
{
    private sealed class Window
    {
        public readonly Queue<(string Speaker, string Line)> Lines = new();
        public DateTime LastUtc;
    }

    private readonly ChatSettingsService _settings;
    private readonly ConcurrentDictionary<(int BotGuid, string Counterpart, ChatKind Kind), Window> _windows = new();

    public ConversationTracker(ChatSettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>Append one line (either direction) and refresh the TTL clock.</summary>
    public void Append(int botGuid, string counterpart, ChatKind kind, string speaker, string line)
    {
        int cap = Math.Max(2, _settings.GetInt(0, "tier0.window_lines", 10));
        var w = _windows.GetOrAdd((botGuid, Normalize(counterpart), kind), _ => new Window());
        lock (w)
        {
            w.Lines.Enqueue((speaker, line));
            while (w.Lines.Count > cap) w.Lines.Dequeue();
            w.LastUtc = DateTime.UtcNow;
        }
    }

    /// <summary>The {live_window} block: oldest→newest, ends with the newest line.</summary>
    public IReadOnlyList<(string Speaker, string Line)> GetWindow(int botGuid, string counterpart, ChatKind kind)
    {
        if (!_windows.TryGetValue((botGuid, Normalize(counterpart), kind), out var w))
            return Array.Empty<(string, string)>();
        lock (w) return w.Lines.ToArray();
    }

    /// <summary>The threadActive urge signal (§9.2) — any un-expired window with this counterpart.</summary>
    public bool IsThreadActive(int botGuid, string counterpart)
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _settings.GetInt(0, "tier0.ttl_min", 10)));
        var cp = Normalize(counterpart);
        foreach (var kv in _windows)
        {
            if (kv.Key.BotGuid != botGuid || kv.Key.Counterpart != cp) continue;
            lock (kv.Value)
                if (DateTime.UtcNow - kv.Value.LastUtc < ttl) return true;
        }
        return false;
    }

    /// <summary>
    /// Crosstalk gate input (§9.2): live conversations among the given bots — the doc's
    /// "within say-range of the stimulus origin" approximated by the hearer set itself
    /// (they heard it, they're at the spot).
    /// </summary>
    public int CountActiveThreads(IReadOnlyCollection<int> botGuids)
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _settings.GetInt(0, "tier0.ttl_min", 10)));
        var now = DateTime.UtcNow;
        int count = 0;
        foreach (var kv in _windows)
        {
            if (!botGuids.Contains(kv.Key.BotGuid)) continue;
            lock (kv.Value)
                if (now - kv.Value.LastUtc < ttl) count++;
        }
        return count;
    }

    /// <summary>TTL sweep — called from the coordinator's 1 s housekeeping loop.</summary>
    public void Sweep()
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _settings.GetInt(0, "tier0.ttl_min", 10)));
        var now = DateTime.UtcNow;
        foreach (var kv in _windows)
        {
            bool expired;
            lock (kv.Value) expired = now - kv.Value.LastUtc > ttl;
            if (expired) _windows.TryRemove(kv.Key, out _);
        }
    }

    private static string Normalize(string name) => (name ?? "").Trim();
}
