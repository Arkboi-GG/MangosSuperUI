using System.Text;
using MangosSuperUI.BotLogic.Core;

namespace MangosSuperUI.BotLogic.Brain;

// ============================================================================
// FleetReport — the one-shot picture (§3.6).
//
// A pure, allocation-cheap dump of every BotContext into ONE bounded text
// artifact sized to fit a context window: fleet rollups + one compact line per
// bot. This is THE primary debugging surface; it replaces grepping six log
// streams. It reads the context and renders — no state, no decisions.
// ============================================================================
public static class FleetReport
{
    // Hard cap so a large fleet can never blow a context window; the overflow is summarized.
    public const int MaxRows = 60;

    public static string Render(IReadOnlyCollection<BotContext> bots)
    {
        var sb = new StringBuilder();
        var now = DateTime.UtcNow;

        sb.Append("FLEET  ").Append(bots.Count).Append(" bots  @ ")
          .Append(now.ToString("HH:mm:ss")).Append(" UTC\n");

        // ---- rollups ----
        var byGoal = new SortedDictionary<Goal, int>();
        var levels = new List<int>();
        int stalled = 0;
        foreach (var b in bots)
        {
            byGoal[b.Goal] = byGoal.TryGetValue(b.Goal, out var n) ? n + 1 : 1;
            if (b.Level > 0) levels.Add(b.Level);
            if (b.Stalled) stalled++;
        }

        sb.Append("  goals:   ").Append(string.Join("  ", byGoal.Select(kv => $"{kv.Key}={kv.Value}"))).Append('\n');
        if (levels.Count > 0)
        {
            levels.Sort();
            sb.Append("  levels:  min=").Append(levels[0])
              .Append(" avg=").Append(levels.Average().ToString("F1"))
              .Append(" max=").Append(levels[^1]).Append('\n');
        }
        sb.Append("  stalled: ").Append(stalled).Append('/').Append(bots.Count).Append('\n');
        sb.Append("  ----\n");

        // ---- per-bot lines (stalled first, then by name) ----
        int shown = 0;
        foreach (var b in bots.OrderByDescending(b => b.Stalled).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (shown++ >= MaxRows)
            {
                sb.Append("  … ").Append(bots.Count - MaxRows).Append(" more bots (raise MaxRows to see all)\n");
                break;
            }
            sb.Append("  ").Append(Line(b)).Append('\n');
        }

        return sb.ToString();
    }

    private static string Line(BotContext b)
    {
        var sb = new StringBuilder();

        sb.Append('[').Append(b.Guid).Append("] ");
        sb.Append(Pad(b.Name, 14)).Append(" L").Append(b.Level).Append("  ");
        sb.Append(Pad($"{b.Goal}/{b.Step}", 20)).Append(' ');
        sb.Append("z=").Append(b.ZoneId).Append(' ').Append(b.Pos).Append('@').Append(b.MapId).Append("  ");

        sb.Append("hp=").Append((b.HpPct * 100).ToString("F0")).Append('%');
        if (b.ManaPct > 0f && b.ManaPct < 1f) sb.Append(" mp=").Append((b.ManaPct * 100).ToString("F0")).Append('%');
        sb.Append(" bag=").Append(b.FreeSlots).Append("f cu=").Append(b.Copper).Append("c  ");

        if (b.Target.HasValue) sb.Append("tgt=").Append(b.DistToTarget.ToString("F0")).Append("y  ");

        sb.Append("inStep=").Append(b.TimeInStepSec.ToString("F0")).Append("s ");
        sb.Append("noProg=").Append(b.TimeSinceProgressSec.ToString("F0")).Append("s  ");

        var p = b.Pending;
        sb.Append("pend=");
        if (p != null) sb.Append(p.ExpectedEvent).Append('(').Append(p.AgeSec.ToString("F0")).Append("s)");
        else sb.Append('-');

        if (b.InCombat) sb.Append("  [combat]");
        if (b.Dead) sb.Append("  [dead]");

        var q = b.Quest;
        if (q != null && q.ActiveQuestIds.Count > 0)
            sb.Append("  q=[").Append(string.Join(",", q.ActiveQuestIds)).Append(']');

        if (b.Stalled)
        {
            int secs = (int)(DateTime.UtcNow - b.StalledSinceUtc).TotalSeconds;
            sb.Append("  *STALL ").Append(b.StallReason).Append('(').Append(secs).Append("s)");
        }

        return sb.ToString();
    }

    private static string Pad(string s, int n)
    {
        s = Trunc(s, n);
        return s.PadRight(n);
    }

    private static string Trunc(string s, int n)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..(n - 1)] + "…");
}
