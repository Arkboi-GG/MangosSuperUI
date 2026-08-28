using MangosSuperUI.BotLogic.Core;

namespace MangosSuperUI.BotLogic.Brain;

/// <summary>
/// Pure classifier for events that could terminate the single outstanding
/// bridge WAIT. Type determines whether an event belongs to the command; the
/// top-level cbt then has to match exactly. Keeping this decision separate from
/// BotExecutor's state mutations makes the stale-outcome invariant testable.
/// </summary>
internal static class WaitOutcomeMatcher
{
    internal enum Disposition
    {
        NotForPending,
        CorrelationMismatch,
        Positive,
        Negative
    }

    internal static Disposition Classify(Outstanding pending, BotEvent evt)
    {
        bool positive = !string.IsNullOrEmpty(pending.ExpectedEvent)
            && string.Equals(evt.EventType, pending.ExpectedEvent, StringComparison.OrdinalIgnoreCase);

        bool negative = IsNegativeFor(pending.CommandType, evt);
        if (!positive && !negative)   // cb:fold pure classifier arm; caller probes disposition and focused tests cover it
            return Disposition.NotForPending;   // cb:fold pure classifier arm; caller probes disposition and tests cover it

        // A zero id denotes an internal/virtual wait and must never be released
        // by a real socket event. Missing cbt is likewise fail-closed.
        if (pending.CorrelationId <= 0 || evt.CorrelationId != pending.CorrelationId)   // cb:fold pure classifier arm; caller probes rejected cbt
            return Disposition.CorrelationMismatch;   // cb:fold pure classifier arm; caller probes rejected cbt

        return positive ? Disposition.Positive : Disposition.Negative;
    }

    /// <summary>
    /// Classify terminal feedback for a command intentionally issued without a
    /// deadline WAIT. Only the latest retained owner may contribute durable
    /// failure/control state; older same-type outcomes fail closed by cbt.
    /// </summary>
    internal static Disposition Classify(NoWaitCommandOwner owner, BotEvent evt)
    {
        string eventType = evt.EventType;
        bool positive = owner.OwnsTaskMotion
            && eventType.Equals("TASK_COMPLETE", StringComparison.OrdinalIgnoreCase);

        bool controlDrop = eventType.Equals("POSSESSED_DROP", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("CONSCRIPTED_DROP", StringComparison.OrdinalIgnoreCase);
        bool negative = controlDrop
            ? string.Equals(evt.Data?.Trim(), owner.CommandType, StringComparison.OrdinalIgnoreCase)
            : (owner.CommandType.Equals("MOVE_TO", StringComparison.OrdinalIgnoreCase)
                && (eventType.Equals("MOVE_FAILED", StringComparison.OrdinalIgnoreCase)
                    || eventType.Equals("PATH_UNSAFE", StringComparison.OrdinalIgnoreCase))
              || owner.CommandType.Equals("SET_TASK", StringComparison.OrdinalIgnoreCase)
                && eventType.Equals("MOVE_FAILED", StringComparison.OrdinalIgnoreCase)
                && HasPipeField(evt.Data, "source", "set_task_approach")
              || owner.CanGrindBlock
                && eventType.Equals("GRIND_BLOCKED", StringComparison.OrdinalIgnoreCase));

        if (!positive && !negative)   // cb:fold pure classifier arm; caller probes disposition and focused tests cover it
            return Disposition.NotForPending;   // cb:fold pure classifier arm; caller probes disposition and focused tests cover it
        if (owner.CorrelationId <= 0 || evt.CorrelationId != owner.CorrelationId)   // cb:fold pure classifier arm; caller probes rejected cbt
            return Disposition.CorrelationMismatch;   // cb:fold pure classifier arm; caller probes rejected cbt
        return positive ? Disposition.Positive : Disposition.Negative;
    }

    private static bool IsNegativeFor(string commandType, BotEvent evt)
    {
        string eventType = evt.EventType;
        if (eventType.Equals("POSSESSED_DROP", StringComparison.OrdinalIgnoreCase)   // cb:fold pure attribution check; caller probes accepted/rejected disposition
            || eventType.Equals("CONSCRIPTED_DROP", StringComparison.OrdinalIgnoreCase))
        {   // cb:fold pure attribution check; caller probes accepted/rejected disposition
            // The core fence sends the exact dropped msgType as raw EVENT.data.
            // Validate both attribution fields so even a malformed core event
            // cannot terminate a different command that happens to share cbt.
            return string.Equals(evt.Data?.Trim(), commandType, StringComparison.OrdinalIgnoreCase);
        }

        return commandType.ToUpperInvariant() switch
        {
            "MOVE_TO" => eventType.Equals("MOVE_FAILED", StringComparison.OrdinalIgnoreCase)   // cb:fold pure command/event table; caller probes final disposition
                         || eventType.Equals("PATH_UNSAFE", StringComparison.OrdinalIgnoreCase)
                         || eventType.Equals("GRIND_BLOCKED", StringComparison.OrdinalIgnoreCase),
            "QUEST_INTERACT" => eventType.Equals("QUEST_INTERACT_FAIL", StringComparison.OrdinalIgnoreCase),   // cb:fold pure command/event table; caller probes final disposition
            "QUEST_CAST" => eventType.Equals("QUEST_CAST_FAIL", StringComparison.OrdinalIgnoreCase),   // cb:fold pure command/event table; caller probes final disposition
            "TRAIN_AT_NPC" => eventType.Equals("TRAIN_FAIL", StringComparison.OrdinalIgnoreCase),   // cb:fold pure command/event table; caller probes final disposition
            "SELL_ITEMS" => eventType.Equals("SELL_FAIL", StringComparison.OrdinalIgnoreCase),   // cb:fold pure command/event table; caller probes final disposition
            "REPAIR_AT_NPC" => eventType.Equals("REPAIR_FAIL", StringComparison.OrdinalIgnoreCase),   // cb:fold pure command/event table; caller probes final disposition
            "TELEPORT_TO" => eventType.Equals("TELEPORT_FAIL", StringComparison.OrdinalIgnoreCase),   // cb:fold pure command/event table; caller probes final disposition
            "RESET_COMBAT_STUCK" => eventType.Equals("COMBAT_RESET_FAIL", StringComparison.OrdinalIgnoreCase),   // cb:fold exact combat-still terminal; caller probes cbt admission
            "SET_TASK" => (eventType.Equals("MOVE_FAILED", StringComparison.OrdinalIgnoreCase)   // cb:fold pure source-tagged table; focused test covers telemetry exclusion
                           && HasPipeField(evt.Data, "source", "set_task_approach"))
                          || eventType.Equals("GRIND_BLOCKED", StringComparison.OrdinalIgnoreCase),
            _ => false   // cb:fold pure default arm; caller probes not-for-pending
        };
    }

    private static bool HasPipeField(string? data, string key, string value)
        => !string.IsNullOrEmpty(data)
           && data.Split('|').Any(segment =>
           {
               var pair = segment.Split('=', 2);
               return pair.Length == 2
                      && pair[0].Equals(key, StringComparison.OrdinalIgnoreCase)
                      && pair[1].Equals(value, StringComparison.OrdinalIgnoreCase);
           });
}
