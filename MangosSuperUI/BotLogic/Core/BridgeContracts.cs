// ============================================================================
// BridgeContracts -- the C#/C++ wire types (carved out of the old DecisionResult.cs).
//
//   BridgeCommand : an outbound command (Type + Payload), reflection-built from an
//                   anonymous payload object; WithCorr stamps the optional story corr.
//   BotEvent      : an inbound bridge event -- the wire event the executor matches on.
//
// The dead DecisionResult + IBotDomain (the old weight-roll / domain contract) were the
// only other residents of the original file and were dropped in the cleanup.
// ============================================================================

namespace MangosSuperUI.BotLogic.Core;

// ======================== BridgeCommand ========================

public class BridgeCommand
{
    public string Type { get; set; }
    public Dictionary<string, object> Payload { get; set; } = new();

    public BridgeCommand(string type, object? payloadObj = null)
    {
        Type = type;
        if (payloadObj != null)
        {   // cb:fold trivial data-shape guard, wire value object with no guid in reach
            foreach (var prop in payloadObj.GetType().GetProperties())
                Payload[prop.Name] = prop.GetValue(payloadObj)!;
        }
    }

    /// <summary>
    /// COMBAT_DIRECTIVE -- the per-member combat seam (grouping §3.6). Stamps a follower
    /// to ASSIST the god-bot-nominated anchor's live victim (GUID-lock focus-fire), or
    /// clears the seam (mode=none). Fire-and-forget like SET_TASK: no corr, no ack -- the
    /// C++ BridgeHandleCombatDirective parses payload.mode + payload.anchor_guid and never
    /// replies. Re-emitted every coordinator tick, idempotent (§3.8.4): an active directive
    /// re-stamps the same assist; an inactive one (Mode==None) emits the clear form.
    /// Wire shape the handler parses:
    ///   {"type":"COMBAT_DIRECTIVE","payload":{"mode":"assist","anchor_guid":N}}
    ///   mode=="assist" + anchor_guid&gt;0 -&gt; assist;  mode=="none" / anchor_guid&lt;=0 -&gt; Clear().
    /// Snake_case payload keys (JsonExtract* idiom, matching SET_TASK GRIND's creature_entry/
    /// kill_count) -- NOT the outbound pipe `key=value` bag.
    /// </summary>
    public static BridgeCommand Combat(CombatDirective directive)
    {
        bool active = directive.IsActive;
        return new BridgeCommand("COMBAT_DIRECTIVE", new
        {
            mode = active ? "assist" : "none",
            anchor_guid = active ? directive.AnchorGuid : 0
        });
    }
}

// ======================== BotEvent ========================

public class BotEvent
{
    public string EventType { get; set; } = "";
    public int CreatureEntry { get; set; }
    public long CreatureGuid { get; set; }
    public int? QuestId { get; set; }
    public string QuestStatus { get; set; } = "";
    public int NewLevel { get; set; }
    public string Sender { get; set; } = "";
    public string Message { get; set; } = "";
    public string ChatType { get; set; } = "";
    public string Data { get; set; } = "";
    public string ChannelName { get; set; } = "";
    // --- Flight path fields (present on FLIGHT_FAILED) ---
    public string Reason { get; set; } = "";
    public uint Have { get; set; }
    public uint Need { get; set; }
    public uint Cost { get; set; }
    public uint SenderGuid { get; set; }
}