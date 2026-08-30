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

using System.Security.Cryptography;

// ===================== Protocol correlation =====================

/// <summary>
/// Generates positive, process-epoch-scoped bridge correlation ids. The time
/// prefix plus random salt prevents an asynchronous outcome retained by a core
/// across a SuperUI restart from colliding with a newly-issued command, while
/// Interlocked keeps ids monotonic within this process for circuit traces.
/// </summary>
public static class BridgeCorrelation
{
    // Keep the entire id below 2^53: circuit probe values and the web viewer use
    // IEEE-754 doubles, whose integers are exact only through that boundary.
    // A millisecond epoch plus 12 random low bits also survives restarts without
    // reusing 1,2,3... while leaving >4k ids per epoch millisecond.
    private const long SaltMask = (1L << 12) - 1;
    private static long _next = CreateSeed();

    public static long NextId() => Interlocked.Increment(ref _next);

    private static long CreateSeed()
    {
        Span<byte> random = stackalloc byte[4];
        RandomNumberGenerator.Fill(random);
        long salt = BitConverter.ToUInt32(random) & SaltMask;
        return (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 12) | salt;
    }
}

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
    /// <summary>C# socket-session identity; never serialized on the wire.</summary>
    public long BridgeSessionId { get; set; }

    /// <summary>
    /// Top-level bridge correlation id (<c>cbt</c>) echoed by the core for a
    /// command outcome. Unsolicited events (KILL, LEVEL_UP, and similar) do not
    /// need one. A terminal event may resolve a WAIT only when this exactly
    /// matches <see cref="Outstanding.CorrelationId"/>.
    /// </summary>
    public long? CorrelationId { get; set; }
    public string EventType { get; set; } = "";
    public int CreatureEntry { get; set; }
    public long CreatureGuid { get; set; }
    /// <summary>
    /// True when the core found the victim's corpse before crediting the kill.
    /// Defaults true so a C#-first deploy and an older-core rollback preserve
    /// the pre-F2 behavior rather than dropping otherwise valid KILL events.
    /// </summary>
    public bool KillConfirmed { get; set; } = true;
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

// ======================== Group mutation authority ========================

/// <summary>
/// The authoritative result of a C# → core group-topology mutation. An
/// unknown result is deliberately distinct from a rejection: the command may
/// have reached the core, so callers must reconcile rather than retrying it.
/// </summary>
public enum GroupMutationStatus
{
    Success,
    Rejected,
    NotSent,
    OutcomeUnknown
}

public sealed record GroupMutationResult
{
    public required GroupMutationStatus Status { get; init; }
    public required string Detail { get; init; }
    public required GroupMutationKind Operation { get; init; }
    public required int LeaderGuid { get; init; }
    public required IReadOnlyList<int> MemberGuids { get; init; }
    public long CorrelationId { get; init; }
    public BotGroup? Group { get; init; }

    public bool Succeeded => Status == GroupMutationStatus.Success;
    public string OperationCode => Operation == GroupMutationKind.Form ? "form" : "disband";

    public string StatusCode => Status switch
    {
        GroupMutationStatus.Success => "success",   // cb:fold pure API token projection
        GroupMutationStatus.Rejected => "rejected",   // cb:fold pure API token projection
        GroupMutationStatus.NotSent => "not_sent",   // cb:fold pure API token projection
        _ => "outcome_unknown"   // cb:fold pure API token projection
    };
}

public sealed record GroupMutationBatchResult
{
    public required IReadOnlyList<GroupMutationResult> Results { get; init; }

    public int SuccessCount => Results.Count(result => result.Succeeded);
    public bool Succeeded => Results.All(result => result.Succeeded);
}

public enum GroupMutationKind
{
    Form,
    Disband
}

internal enum GroupBridgeOutcomeDisposition
{
    Ignore,
    Accepted,
    Rejected,
    ProtocolMismatch
}

internal sealed record GroupBridgeOutcome(
    GroupBridgeOutcomeDisposition Disposition,
    string Detail);

internal sealed class PendingGroupMutation
{
    public required GroupMutationKind Kind { get; init; }
    public required int LeaderGuid { get; init; }
    public required long SessionId { get; init; }
    public required long CorrelationId { get; init; }
    public required int[] MemberGuids { get; init; }
    public TaskCompletionSource<GroupBridgeOutcome> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Pure admission rule for group terminal events. Success requires the exact
/// active session, cbt, leader, and complete member set that owned the command.
/// A correlated negative result is a rejection. A correlated but malformed or
/// contradictory ACK is indeterminate and must never commit C# topology.
/// </summary>
internal static class GroupBridgeOutcomeMatcher
{
    internal static bool IsGroupTerminal(string eventType)
        => eventType.Equals("FORM_GROUP_ACK", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("FORM_GROUP_FAIL", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("GROUP_DISBANDED", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("GROUP_DISBAND_FAIL", StringComparison.OrdinalIgnoreCase);

    internal static GroupBridgeOutcome Classify(
        PendingGroupMutation pending,
        int eventGuid,
        BotEvent evt)
    {
        string eventType = evt.EventType.Trim().ToUpperInvariant();
        if (!IsGroupTerminal(eventType))
            return new(GroupBridgeOutcomeDisposition.Ignore, "not_a_group_terminal");   // cb:fold pure classifier; caller probes admitted/rejected outcome

        if (eventGuid != pending.LeaderGuid
            || evt.BridgeSessionId != pending.SessionId
            || evt.CorrelationId != pending.CorrelationId)
            return new(GroupBridgeOutcomeDisposition.Ignore, "owner_mismatch");   // cb:fold pure classifier; caller probes owner mismatch

        string expectedSuccess = pending.Kind == GroupMutationKind.Form
            ? "FORM_GROUP_ACK"
            : "GROUP_DISBANDED";
        string expectedFailure = pending.Kind == GroupMutationKind.Form
            ? "FORM_GROUP_FAIL"
            : "GROUP_DISBAND_FAIL";

        if (eventType == expectedFailure)
        {   // cb:fold pure classifier; caller probes rejected outcome
            string reason = string.IsNullOrWhiteSpace(evt.Reason) ? evt.Data : evt.Reason;
            return new(   // cb:fold pure classifier; caller probes rejected outcome
                GroupBridgeOutcomeDisposition.Rejected,
                string.IsNullOrWhiteSpace(reason) ? "core_rejected" : reason.Trim());
        }

        if (eventType != expectedSuccess)
            return new(GroupBridgeOutcomeDisposition.Ignore, "wrong_operation");   // cb:fold pure classifier; caller probes owner mismatch

        if (!TryParseExactTopology(evt.Data, out int leaderGuid, out int[] members))
            return new(GroupBridgeOutcomeDisposition.ProtocolMismatch, "malformed_topology_ack");   // cb:fold pure classifier; caller probes protocol mismatch

        if (leaderGuid != pending.LeaderGuid
            || !members.SequenceEqual(pending.MemberGuids))
            return new(GroupBridgeOutcomeDisposition.ProtocolMismatch, "topology_ack_mismatch");   // cb:fold pure classifier; caller probes protocol mismatch

        return new(GroupBridgeOutcomeDisposition.Accepted, "core_acknowledged");
    }

    private static bool TryParseExactTopology(
        string data,
        out int leaderGuid,
        out int[] members)
    {
        leaderGuid = 0;
        members = Array.Empty<int>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in (data ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = part.IndexOf('=');
            if (separator <= 0)
                return false;   // cb:fold pure ACK parser; caller probes malformed result
            fields[part[..separator].Trim()] = part[(separator + 1)..].Trim();
        }

        if (!fields.TryGetValue("leader_guid", out string? leaderText)
            || !int.TryParse(leaderText, out leaderGuid)
            || leaderGuid <= 0
            || !fields.TryGetValue("member_guids", out string? memberText))
            return false;   // cb:fold pure ACK parser; caller probes malformed result

        string[] tokens = memberText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
            return false;   // cb:fold pure ACK parser; caller probes malformed result

        var parsed = new List<int>(tokens.Length);
        foreach (string token in tokens)
        {
            if (!int.TryParse(token, out int guid) || guid <= 0)
                return false;   // cb:fold pure ACK parser; caller probes malformed result
            parsed.Add(guid);
        }

        if (parsed.Distinct().Count() != parsed.Count || !parsed.Contains(leaderGuid))
            return false;   // cb:fold pure ACK parser; caller probes malformed result

        members = parsed.OrderBy(guid => guid).ToArray();
        return true;
    }
}
