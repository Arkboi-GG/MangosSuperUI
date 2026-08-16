using System.Text.Json;

namespace MangosSuperUI.Models;

// ─────────────────────────────────────────────────────────────────────────────
// The wire shape the MSUI client's NPC dev window POSTs to /NpcDev/Apply. It is
// the same document DevChangeSet writes to dev-changes/*.json (MSUIClient repo,
// Formats/DevChangeSet.cs) — schemaVersion + session + packets. Target/Before/
// After stay open dictionaries (JsonElement values) so the applier reads exactly
// the keys each packet type carries; NpcDevApplyService is the authority on which
// keys/columns each type is allowed to touch.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class NpcApplyRequest
{
    public int SchemaVersion { get; set; } = 1;
    public NpcApplySession? Session { get; set; }
    public List<NpcApplyPacket> Packets { get; set; } = new();
}

public sealed class NpcApplySession
{
    public DateTime CreatedUtc { get; set; }
    public string Character { get; set; } = "";
    public DateTime SourceSnapshotUtc { get; set; }
    public string SuiBase { get; set; } = "";
}

public sealed class NpcApplyPacket
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public Dictionary<string, JsonElement> Target { get; set; } = new();
    public Dictionary<string, JsonElement> Before { get; set; } = new();
    public Dictionary<string, JsonElement> After { get; set; } = new();
    public Dictionary<string, JsonElement>? Context { get; set; }
}

/// <summary>Per-packet outcome the client renders in the change-set panel.</summary>
public sealed class NpcPacketVerdict
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    /// <summary>applied | stale | missing | unsupported | failed</summary>
    public string Verdict { get; set; } = "";
    public string? Message { get; set; }
    public long? AuditId { get; set; }
}

public sealed class NpcApplyResult
{
    public string? BatchId { get; set; }
    public int Applied { get; set; }
    public int Stale { get; set; }
    public int Failed { get; set; }
    public List<NpcPacketVerdict> Results { get; set; } = new();
}

// ── OG baseline: diff (changed-from-original) + reset-to-original ─────────────

/// <summary>Whether one spawn (and its path / its entry's template) differs from the
/// captured og_creature* baseline. hasBaseline=false ⇒ the owner hasn't run
/// Baseline/Initialize with the creature tables yet, so nothing can be diffed/reset.</summary>
public sealed class NpcDiffResult
{
    public bool HasBaseline { get; set; }
    public uint Guid { get; set; }
    public uint Entry { get; set; }
    public bool SpawnModified { get; set; }
    public bool PathModified { get; set; }
    public bool TemplateModified { get; set; }
    public bool BaselineHasSpawn { get; set; }   // false = this guid isn't in the baseline (e.g. added after capture)
    public bool Modified => SpawnModified || PathModified || TemplateModified;
}

/// <summary>Reset-to-original request: restore each guid's spawn row + path, and each entry's
/// template (detection_range), from the og_creature* baseline.</summary>
public sealed class NpcResetRequest
{
    public string? Character { get; set; }
    public List<uint> Guids { get; set; } = new();
    public List<uint> Entries { get; set; } = new();
}
