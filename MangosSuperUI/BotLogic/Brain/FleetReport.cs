using System.Text;
using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.Services;

namespace MangosSuperUI.BotLogic.Brain;

// ============================================================================
// FleetReport — the one-shot picture (§3.6).
//
// A pure, allocation-cheap dump of every BotContext into ONE bounded text
// artifact sized to fit a context window: fleet rollups + one compact line per
// bot. This is THE primary debugging surface; it replaces grepping six log
// streams. It reads the context and renders — no state, no decisions.
//
// Two renderers, deliberately (scale work, 2026-08-29):
//
//   RenderSummary  — rollups only. This is what the 30 s Info log emits, because
//                    at thousands of bots the detail rows are a recurring string
//                    and log-provider cost for output nobody reads at Info.
//   RenderDetailed — rollups + the bounded row table, on demand.
//
// This file used to carry CircuitTrace probes on its rendering branches. They
// were removed: the circuit board instruments DECISIONS, and probes here fired
// ~10x per bot per render into every bot's shadow ring, inflating the very
// retention the report is used to investigate.
// ============================================================================
public static class FleetReport
{
    // Hard cap so a large fleet can never blow a context window; the overflow is summarized.
    public const int MaxRows = 60;

    /// <summary>Rollups only — the periodic Info log surface.</summary>
    public static string RenderSummary(IReadOnlyCollection<BotContext> bots)
    {
        var sb = new StringBuilder();
        AppendSummary(sb, bots);
        return sb.ToString();
    }

    /// <summary>Rollups plus the bounded per-bot table — the on-demand surface.</summary>
    public static string RenderDetailed(IReadOnlyCollection<BotContext> bots, int maxRows = MaxRows)
    {
        if (maxRows <= 0)
            maxRows = MaxRows;

        var sb = new StringBuilder();
        AppendSummary(sb, bots);
        sb.Append("  ----\n");

        List<BotContext> rows = SelectRows(bots, maxRows);
        foreach (BotContext b in rows)
            sb.Append("  ").Append(Line(b)).Append('\n');

        int hidden = bots.Count - rows.Count;
        if (hidden > 0)
            sb.Append("  … ").Append(hidden).Append(" more bots (raise maxRows to see all)\n");

        return sb.ToString();
    }

    /// <summary>Back-compat entry point; unchanged meaning (detailed).</summary>
    public static string Render(IReadOnlyCollection<BotContext> bots) => RenderDetailed(bots);

    private static void AppendSummary(StringBuilder sb, IReadOnlyCollection<BotContext> bots)
    {
        var now = DateTime.UtcNow;

        sb.Append("FLEET  ").Append(bots.Count).Append(" bots  @ ")
          .Append(now.ToString("HH:mm:ss")).Append(" UTC\n");

        var byGoal = new SortedDictionary<Goal, int>();
        // Running aggregates rather than a List<int> of every level: the list was
        // one roster-sized allocation plus a full sort on every render.
        int levelCount = 0, levelMin = int.MaxValue, levelMax = int.MinValue;
        long levelSum = 0;
        int stalled = 0, feedStale = 0, dead = 0, inCombat = 0, pending = 0, incompatible = 0;

        foreach (var b in bots)
        {
            byGoal[b.Goal] = byGoal.TryGetValue(b.Goal, out var n) ? n + 1 : 1;
            if (b.Level > 0)
            {
                levelCount++;
                levelSum += b.Level;
                if (b.Level < levelMin) levelMin = b.Level;
                if (b.Level > levelMax) levelMax = b.Level;
            }
            if (b.Stalled) stalled++;
            if (b.SensoryFeedStale) feedStale++;
            if (b.Dead) dead++;
            if (b.InCombat) inCombat++;
            if (b.Pending != null) pending++;
            if (b.BridgeProtocolIncompatible) incompatible++;
        }

        sb.Append("  goals:   ").Append(string.Join("  ", byGoal.Select(kv => $"{kv.Key}={kv.Value}"))).Append('\n');
        if (levelCount > 0)
        {
            sb.Append("  levels:  min=").Append(levelMin)
              .Append(" avg=").Append((levelSum / (double)levelCount).ToString("F1"))
              .Append(" max=").Append(levelMax).Append('\n');
        }
        sb.Append("  stalled: ").Append(stalled).Append('/').Append(bots.Count).Append('\n');
        sb.Append("  feed-stale: ").Append(feedStale).Append('/').Append(bots.Count).Append('\n');
        sb.Append("  dead: ").Append(dead)
          .Append("  combat: ").Append(inCombat)
          .Append("  pending: ").Append(pending)
          .Append("  bridge-incompatible: ").Append(incompatible).Append('\n');
    }

    /// <summary>
    /// The <paramref name="maxRows"/> rows that would sort first, without sorting
    /// the whole fleet. A bounded set keeps this O(N log maxRows) instead of the
    /// O(N log N) three-key OrderBy that ran over every bot to print 60 lines.
    /// </summary>
    private static List<BotContext> SelectRows(IReadOnlyCollection<BotContext> bots, int maxRows)
    {
        var top = new SortedSet<BotContext>(RowOrder.Instance);
        foreach (BotContext b in bots)
        {
            top.Add(b);
            if (top.Count > maxRows)
                top.Remove(top.Max!);
        }

        return top.ToList();
    }

    /// <summary>
    /// Stalled/stale first, then by name. Guid is the final key purely to make
    /// the order total, so the bounded set can never discard a bot that merely
    /// shares a name with another.
    /// </summary>
    private sealed class RowOrder : IComparer<BotContext>
    {
        public static readonly RowOrder Instance = new();

        public int Compare(BotContext? x, BotContext? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return 1;
            if (y is null) return -1;

            int c = y.SensoryFeedStale.CompareTo(x.SensoryFeedStale);
            if (c != 0) return c;

            c = y.Stalled.CompareTo(x.Stalled);
            if (c != 0) return c;

            c = string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : x.Guid.CompareTo(y.Guid);
        }
    }

    private static string Line(BotContext b)
    {
        var sb = new StringBuilder();

        sb.Append('[').Append(b.Guid).Append("] ");
        sb.Append(Pad(b.Name, 14)).Append(" L").Append(b.Level).Append("  ");
        sb.Append(Pad($"{b.Goal}/{b.Step}", 20)).Append(' ');
        sb.Append("z=").Append(b.ZoneId).Append(' ').Append(b.Pos).Append('@').Append(b.MapId).Append("  ");

        sb.Append("hp=").Append((b.HpPct * 100).ToString("F0")).Append('%');
        if (b.ManaPct > 0f && b.ManaPct < 1f)
            sb.Append(" mp=").Append((b.ManaPct * 100).ToString("F0")).Append('%');
        sb.Append(" bag=").Append(b.FreeSlots).Append("f cu=").Append(b.Copper).Append("c  ");

        if (b.Target.HasValue)
            sb.Append("tgt=").Append(b.DistToTarget.ToString("F0")).Append("y  ");

        sb.Append("inStep=").Append(b.TimeInStepSec.ToString("F0")).Append("s ");
        sb.Append("noProg=").Append(b.TimeSinceProgressSec.ToString("F0")).Append("s  ");

        var p = b.Pending;
        sb.Append("pend=");
        if (p != null)
            sb.Append(p.ExpectedEvent).Append('(').Append(p.AgeSec.ToString("F0")).Append("s)");
        else
            sb.Append('-');

        if (b.InCombat)
            sb.Append("  [combat]");
        if (b.Dead)
            sb.Append("  [dead]");
        if (b.SensoryFeedStale)
            sb.Append("  [FEED_STALE ").Append(b.SensoryStateAgeSec.ToString("F0")).Append("s]");
        if (b.BridgeProtocolIncompatible)
        {
            sb.Append("  [BRIDGE_PROTOCOL ").Append(b.BridgeProtocol).Append('<')
              .Append(BotBridgeService.RequiredCorrelatedOutcomeProtocol).Append(']');
        }
        if (b.Possessed)
            sb.Append("  [possessed]");
        else if (b.Conscripted)
            sb.Append("  [conscripted]");

        var q = b.Quest;
        if (q != null && q.ActiveQuestIds.Count > 0)
            sb.Append("  q=[").Append(string.Join(",", q.ActiveQuestIds)).Append(']');

        if (b.Stalled)
        {
            int secs = (int)(DateTime.UtcNow - b.StalledSinceUtc).TotalSeconds;
            sb.Append("  *STALL ").Append(b.StallReason).Append('(').Append(secs).Append("s)");
        }

        // Why the bot is in this goal (the arbitration's reasoning) — explains a grinding
        // bot at a glance: "q av=12 pick=0" = quests available but none pass the pick filter.
        if (!string.IsNullOrEmpty(b.GoalReason))
            sb.Append("  why=").Append(b.GoalReason);

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
