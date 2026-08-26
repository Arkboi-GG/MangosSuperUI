using System.Text.Json;
using System.Text.Json.Nodes;

namespace MangosSuperUI.Services;

/// <summary>
/// The Spell Completer INBOX: spells pushed straight out of MSUIClient's creator
/// mode, waiting for the data phase.
///
/// Before this existed the design phase ended at a file — the creator wrote
/// spell-session.json to its launch directory and the user hand-carried it to the
/// Completer's drop-zone, which parsed it IN THE BROWSER and then re-uploaded
/// every embedded byte again on each Complete. Now the creator POSTs one finished
/// spell to /SpellCompleter/Push, it lands here, and the page lists what has
/// arrived. Complete names the stored item by id; the bytes never make the round
/// trip through the browser at all.
///
/// Stored under ContentRootPath, NOT wwwroot — unlike CompleterStore's artifacts
/// (which the patch pipeline re-reads and which are already published art), these
/// are unreviewed uploads from a desktop client and have no business being
/// statically served.
///
///   App_Data/spell-inbox/{id}/
///     spell.json   — the pushed entry VERBATIM, base64 payloads and all. The
///                    faithful record: re-serialize it and you have the same
///                    document the creator wrote to spell-session.json.
///     meta.json    — the same entry with every *Base64 field stripped, plus
///                    push bookkeeping. This is what the listing and the page's
///                    render path read, so listing N spells never loads N × MBs
///                    of embedded models.
///
/// The id is the sanitized temp name, so pushing the same temp name again
/// REPLACES — matching the creator's own "same temp name = replace" rule for the
/// session file.
/// </summary>
public static class SpellSessionInbox
{
    private const string FullName = "spell.json";
    private const string MetaName = "meta.json";

    /// <summary>Fields whose values are multi-megabyte base64 blobs. Stripped from
    /// meta.json so the listing stays small; spell.json keeps them.</summary>
    private static readonly string[] Base64Fields = { "m2Base64", "blpBase64", "fileBase64" };

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>One inbox item, as the listing endpoint reports it.</summary>
    public sealed record ItemSummary(
        string Id,
        string TempName,
        int SourceSpellId,
        string SourceSpellName,
        string ExportedAtUtc,
        string PushedAtUtc,
        int Models,
        int TintedBlps,
        int Audio,
        long Bytes,
        int CompletedEntry);

    public static string Root(string contentRoot) =>
        Path.Combine(contentRoot, "App_Data", "spell-inbox");

    /// <summary>Same sanitization shape as <see cref="CompleterStore.SafeName"/>,
    /// with '-' allowed because temp names are throwaway labels people hyphenate.
    /// Returns "" when nothing survives — callers must treat that as invalid.</summary>
    public static string SafeId(string tempName) =>
        new(tempName.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());

    // ── writing ──────────────────────────────────────────────────────────────

    /// <summary>Persist one pushed spell, replacing any item under the same temp
    /// name. Returns the summary, or null when the entry has no usable temp name.</summary>
    public static ItemSummary? Save(string contentRoot, JsonObject spell)
    {
        string tempName = spell["tempName"]?.GetValue<string>() ?? "";
        string id = SafeId(tempName);
        if (id.Length == 0) return null;

        string dir = Path.Combine(Root(contentRoot), id);
        Directory.CreateDirectory(dir);

        string full = spell.ToJsonString(Indented);
        File.WriteAllText(Path.Combine(dir, FullName), full);

        // The stripped twin. Re-parsed rather than mutated in place so the caller's
        // object — which is about to be serialized above — is never touched.
        JsonObject meta = (JsonObject)JsonNode.Parse(full)!;
        StripBase64(meta);
        meta["id"] = id;
        meta["pushedAtUtc"] = DateTime.UtcNow.ToString("o");
        meta["bytes"] = full.Length;
        File.WriteAllText(Path.Combine(dir, MetaName), meta.ToJsonString(Indented));

        return Summarize(meta, id);
    }

    /// <summary>Record which spell_template entry this item was completed as, so a
    /// re-push or a second visit to the page can show it as already done instead
    /// of inviting a duplicate clone.</summary>
    public static void MarkCompleted(string contentRoot, string id, int spellEntry)
    {
        string path = Path.Combine(Root(contentRoot), SafeId(id), MetaName);
        if (!File.Exists(path)) return;
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject meta) return;
            meta["completedEntry"] = spellEntry;
            meta["completedAtUtc"] = DateTime.UtcNow.ToString("o");
            File.WriteAllText(path, meta.ToJsonString(Indented));
        }
        catch (Exception)
        {
            // Bookkeeping only — a spell that completed successfully must not be
            // reported as failed because its inbox marker could not be written.
        }
    }

    // ── reading ──────────────────────────────────────────────────────────────

    /// <summary>Every pending item, newest push first. Unreadable directories are
    /// skipped rather than failing the whole listing.</summary>
    public static List<ItemSummary> List(string contentRoot)
    {
        var items = new List<ItemSummary>();
        string root = Root(contentRoot);
        if (!Directory.Exists(root)) return items;

        foreach (string dir in Directory.GetDirectories(root))
        {
            string id = Path.GetFileName(dir);
            if (LoadMeta(contentRoot, id) is { } meta)
                items.Add(Summarize(meta, id));
        }
        return items.OrderByDescending(i => i.PushedAtUtc, StringComparer.Ordinal).ToList();
    }

    /// <summary>The base64-stripped entry — what the page renders from.</summary>
    public static JsonObject? LoadMeta(string contentRoot, string id) =>
        Read(contentRoot, id, MetaName);

    /// <summary>The entry with its embedded bytes — what Complete decodes.</summary>
    public static JsonObject? LoadFull(string contentRoot, string id) =>
        Read(contentRoot, id, FullName);

    public static bool Delete(string contentRoot, string id)
    {
        string safe = SafeId(id);
        if (safe.Length == 0) return false;
        string dir = Path.Combine(Root(contentRoot), safe);
        if (!Directory.Exists(dir)) return false;
        Directory.Delete(dir, recursive: true);
        return true;
    }

    private static JsonObject? Read(string contentRoot, string id, string file)
    {
        string safe = SafeId(id);
        if (safe.Length == 0) return null;
        string path = Path.Combine(Root(contentRoot), safe, file);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Walk the whole document and null out every base64 payload. Recursive
    /// because the payloads sit at three different depths (models[], tintedBlps[],
    /// audio[]) and a schema v3 could add a fourth.</summary>
    private static void StripBase64(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (string field in Base64Fields)
                    if (obj.ContainsKey(field)) obj[field] = null;
                foreach (var (_, child) in obj.ToList()) StripBase64(child);
                break;
            case JsonArray arr:
                foreach (JsonNode? child in arr) StripBase64(child);
                break;
        }
    }

    private static ItemSummary Summarize(JsonObject meta, string id) => new(
        Id: id,
        TempName: meta["tempName"]?.GetValue<string>() ?? id,
        SourceSpellId: Int(meta["sourceSpellId"]),
        SourceSpellName: meta["sourceSpellName"]?.GetValue<string>() ?? "",
        ExportedAtUtc: meta["exportedAtUtc"]?.GetValue<string>() ?? "",
        PushedAtUtc: meta["pushedAtUtc"]?.GetValue<string>() ?? "",
        Models: (meta["models"] as JsonArray)?.Count ?? 0,
        TintedBlps: (meta["tintedBlps"] as JsonArray)?.Count ?? 0,
        Audio: (meta["audio"] as JsonArray)?.Count ?? 0,
        Bytes: Int(meta["bytes"]),
        CompletedEntry: Int(meta["completedEntry"]));

    private static int Int(JsonNode? node)
    {
        try { return node is null ? 0 : node.GetValue<int>(); }
        catch (Exception) { return 0; }
    }
}
