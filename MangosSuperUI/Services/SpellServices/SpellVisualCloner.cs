namespace MangosSuperUI.Services;

/// <summary>
/// Clones an entire SpellVisual DBC chain with new IDs.
/// 
/// The visual chain for a spell is:
///   Spell.dbc[spellVisual field] → SpellVisual.dbc row
///     → SpellVisualKit.dbc rows (precastKit, castKit, impactKit, stateKit, channelKit)
///       → SpellVisualEffectName.dbc rows (headEffect, chestEffect, baseEffect, leftHandEffect, etc.)
///         → M2 file paths (the actual particle/effect models)
///
/// FIELD MAP SOURCE OF TRUTH
///   These indices are the client's, taken from MSUIClient's
///   Formats/SpellVisualCatalog.cs — byte-verified against build 5875 and
///   cross-checked by tools/spellvis/spellvis.py, which is the documented oracle
///   for this layout. A loader that actually RENDERS these rows is the only field
///   map worth trusting; the numbers below were re-confirmed against our own
///   dbc/patch MPQs before being written down.
///
/// SpellVisual.dbc layout (16 fields, 64 bytes per record, all u32):
///   [0]  ID
///   [1]  PrecastKit          → SpellVisualKit ID
///   [2]  CastKit             → SpellVisualKit ID
///   [3]  ImpactKit           → SpellVisualKit ID
///   [4]  StateKit            → SpellVisualKit ID (0 for bolt spells)
///   [5]  ChannelKit          → SpellVisualKit ID (set on 127 of 2165 rows)
///   [6]  HasMissile          → a GATE, not a foreign key. Only ever 0 or 1 on
///                               the shipped table (228 rows carry 1). The client
///                               never reads it — the real missile gate is
///                               Spell.dbc Speed > 0. DO NOT clone this as a kit:
///                               1 is also the id of the dummy SpellVisualKit row,
///                               so treating it as a kit clones that sentinel (and
///                               the dead zzOLD__FireShield_Cast_Base effect it
///                               points at) and overwrites the gate with a kit id.
///   [7]  MissileEffect       → SpellVisualEffectName ID for the projectile model
///                               Fireball=365 "Fireball Missile Low", ShadowBolt=151
///   [8]  (unmapped — 0 on every row we have looked at)
///   [9]  MissileAttachOrdinal  ORDINAL into the client's MissileAttachTable,
///                               NOT an attachment id (Fireball=1)
///   [10] MissileSound        → SoundEntries ID, the in-flight loop (Fireball=3011)
///   [11] AreaGate
///   [12] AreaEffect          → SpellVisualEffectName ID (DynamicObject centre model)
///   [13] AreaKit             → SpellVisualKit ID (its type-9 CharProcs rate the
///                               area shards). NOT cloned per spell — see the note
///                               in SpellCompleterController: patching a shared
///                               area kit would change every spell that uses it.
///   [14] StrikeSound         → SoundEntries ID
///   [15] (unmapped)
///
/// SpellVisualKit.dbc layout (35 fields, 140 bytes per record):
///   [0]  ID
///   [1]  StartAnimID
///   [2]  AnimID              → AnimationData.dbc (53 = directed cast for Fire and Shadow)
///   [3]  HeadEffect          → SpellVisualEffectName ID (0 AND 0xFFFFFFFF = none)
///   [4]  ChestEffect         → SpellVisualEffectName ID
///   [5]  BaseEffect          → SpellVisualEffectName ID
///   [6]  LeftHandEffect      → SpellVisualEffectName ID
///   [7]  RightHandEffect     → SpellVisualEffectName ID
///   [8]  BreathEffect        → SpellVisualEffectName ID
///   [9]  Special1Effect      → SpellVisualEffectName ID
///   [10] Special2Effect      → SpellVisualEffectName ID
///   [11] Special3Effect      → SpellVisualEffectName ID — the NINTH slot. The
///                               client reads nine slots at [3..11]; it is empty on
///                               all 1772 shipped kits, but it is an EFFECT slot,
///                               so nothing else may be written there.
///   [12] (unmapped)
///   [13] SoundID             → SoundEntries ID (Fireball cast=1484, impact=1507)
///   [15-18] CharProc types, [19-34] their parameters (four lanes, transposed)
///
/// THE NONE-SENTINEL
///   "No value" is written as EITHER 0 OR 0xFFFFFFFF, inconsistently, on the same
///   table (of 15948 kit effect slots: 8 zeros, 14087 0xFFFFFFFF). Every foreign
///   key read here folds BOTH, matching SpellVisualCatalog.Fk().
///
/// SpellVisualEffectName.dbc layout — CORRECTED Session 8:
///   [0]  ID
///   [1]  Name        (stringref → display/debug label, e.g. "Fire Cast Hand")
///   [2]  FilePath    (stringref → ACTUAL M2 path, e.g. "Spells\Fire_Cast_Hand.mdx")
///   [3]  AreaEffectSize (uint32, usually 0 or 4)
///   [4]  Scale       (float, 0.0 or 1.0)
///
///   ⚠️ Field [2] is a STRINGREF (FilePath), NOT a float! Session 8 root cause.
///   The client uses field [2] to locate the M2 file. Field [1] is display only.
///   Vanilla DBC uses .mdx extension in FilePath; actual MPQ files are .m2.
///   We write .m2 in the DBC since that matches MPQ contents. If the client
///   can't find it, try switching to .mdx (the client may map internally).
/// </summary>
public class SpellVisualCloner
{
    /// <summary>Result of cloning a visual chain.</summary>
    public class CloneResult
    {
        public uint NewVisualId { get; set; }
        public Dictionary<uint, uint> KitIdMap { get; set; } = new();         // old kit ID → new kit ID
        public Dictionary<uint, uint> EffectNameIdMap { get; set; } = new();  // old effectName ID → new effectName ID
        public List<EffectFileMapping> EffectFiles { get; set; } = new();     // new effect IDs with their M2 paths
        public uint MissileEffectId { get; set; }                              // new missile effect ID (if any)
    }

    /// <summary>Maps an effect name ID to its M2 file path (original and custom).</summary>
    public class EffectFileMapping
    {
        public uint NewEffectId { get; set; }
        public string OriginalName { get; set; } = "";    // DBC effect name (e.g. "Fire Cast Hand")
        public string OriginalM2Path { get; set; } = "";  // Derived M2 path (e.g. "Spells\\Fire_Cast_Hand.m2")
        public string CustomName { get; set; } = "";      // New DBC effect name (e.g. "Voidstrike Cast Hand")
        public string CustomM2Path { get; set; } = "";    // New M2 path (e.g. "Spells\\Voidstrike_Cast_Hand.m2")
        public string EffectRole { get; set; } = "";      // "<stage>_<slot>": "cast_leftHand", "channel_base",
                                                          // "impact_chest", or the bare "missile".
                                                          // Stage is one of precast/cast/impact/state/channel;
                                                          // callers split on '_' to key per-phase params.
    }

    /// <summary>
    /// The NINE kit field indices that point to SpellVisualEffectName IDs.
    /// The client reads [3..11]; slot names follow its KitAttachmentIds order
    /// (Head, Chest, Base, LeftHand, RightHand, Breath, Special1..3).
    /// Either 0 or 0xFFFFFFFF means "none" — see <see cref="IsNone"/>.
    /// </summary>
    private static readonly int[] KitEffectFields = { 3, 4, 5, 6, 7, 8, 9, 10, 11 };
    private static readonly string[] KitEffectNames = {
        "head", "chest", "base", "leftHand", "rightHand", "breath",
        "special1", "special2", "special3"
    };

    /// <summary>
    /// SpellVisual field indices that point to SpellVisualKit IDs — the FIVE
    /// stage kits, [1..5]. Field [6] is the never-read missile gate and is NOT a
    /// kit reference; there is no "stateDone" stage on this table.
    /// </summary>
    private static readonly int[] VisualKitFields = { 1, 2, 3, 4, 5 };
    private static readonly string[] VisualKitNames = {
        "precast", "cast", "impact", "state", "channel"
    };

    /// <summary>
    /// Fold BOTH none-sentinels. The shipped tables write "no value" as either 0
    /// or 0xFFFFFFFF, inconsistently, on the same column. Mirrors
    /// SpellVisualCatalog.Fk() in the client.
    /// </summary>
    private static bool IsNone(uint id) => id == 0 || id == 0xFFFFFFFF;

    /// <summary>
    /// Derive the M2 file path from a SpellVisualEffectName display name.
    /// Convention: spaces → underscores, prepend "Spells\\", append ".m2"
    /// This path is used BOTH for the MPQ file path AND the DBC FilePath field [2].
    /// </summary>
    public static string EffectNameToM2Path(string effectName)
    {
        return $"Spells\\{effectName.Replace(' ', '_')}.m2";
    }

    /// <summary>
    /// Normalize a DBC FilePath to the actual MPQ file extension.
    /// Vanilla DBC uses .mdx/.mdl extensions but actual MPQ files are .m2.
    /// e.g. "Spells\Fire_Cast_Hand.mdx" → "Spells\Fire_Cast_Hand.m2"
    ///      "Particles\FireShield_Cast_Base.mdl" → "Particles\FireShield_Cast_Base.m2"
    /// </summary>
    public static string NormalizeM2Extension(string dbcFilePath)
    {
        if (string.IsNullOrEmpty(dbcFilePath))
            return dbcFilePath;

        // Replace .mdx or .mdl with .m2 for MPQ lookup
        if (dbcFilePath.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase) ||
            dbcFilePath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            return dbcFilePath.Substring(0, dbcFilePath.Length - 4) + ".m2";
        }
        return dbcFilePath;
    }

    /// <summary>
    /// Build a custom effect name from a spell name and a role descriptor.
    /// e.g. ("Voidstrike", "cast_leftHand") → "Voidstrike Cast LeftHand"
    /// The M2 path is then derived: "Spells\\Voidstrike_Cast_LeftHand.m2"
    /// </summary>
    public static string BuildCustomEffectName(string spellName, string role)
    {
        // Convert role like "cast_leftHand" to "Cast LeftHand"
        string rolePart = string.Join(" ", role.Split('_')
            .Select(p => p.Length > 0 ? char.ToUpper(p[0]) + p.Substring(1) : p));
        return $"{spellName} {rolePart}";
    }

    /// <summary>
    /// Clone an entire SpellVisual chain, assigning new IDs and creating new
    /// SpellVisualEffectName entries with custom names and FilePaths.
    /// </summary>
    public static CloneResult Clone(
        DbcWriterService spellVisualDbc,
        DbcWriterService spellVisualKitDbc,
        DbcWriterService spellVisualEffectNameDbc,
        uint sourceVisualId,
        uint newVisualId,
        uint baseKitId,
        uint baseEffectId,
        string spellName)
    {
        var result = new CloneResult { NewVisualId = newVisualId };
        uint nextKitId = baseKitId;
        uint nextEffectId = baseEffectId;

        // ── Step 1: Clone the SpellVisual row ──
        var visualRow = spellVisualDbc.CloneRow(sourceVisualId, newVisualId);

        // ── Step 2: For each kit reference in the visual, clone the kit ──
        for (int i = 0; i < VisualKitFields.Length; i++)
        {
            int fieldIdx = VisualKitFields[i];
            uint oldKitId = visualRow[fieldIdx];
            if (IsNone(oldKitId)) continue;

            uint newKitId = nextKitId++;
            result.KitIdMap[oldKitId] = newKitId;

            var kitRow = spellVisualKitDbc.CloneRow(oldKitId, newKitId);
            spellVisualDbc.PatchRow(newVisualId, fieldIdx, newKitId);

            // ── Step 3: For each effect reference in the kit, clone the effect ──
            for (int j = 0; j < KitEffectFields.Length; j++)
            {
                int effectFieldIdx = KitEffectFields[j];
                uint oldEffectId = kitRow[effectFieldIdx];
                if (IsNone(oldEffectId)) continue;

                if (!result.EffectNameIdMap.TryGetValue(oldEffectId, out uint newEffectId))
                {
                    newEffectId = nextEffectId++;
                    result.EffectNameIdMap[oldEffectId] = newEffectId;

                    var effectRow = spellVisualEffectNameDbc.CloneRow(oldEffectId, newEffectId);
                    string originalName = spellVisualEffectNameDbc.ReadString(effectRow[1]);

                    // Read the ACTUAL original file path from field [2] (not derived from name!)
                    // e.g. "Particles\FireShield_Cast_Base.mdl" or "Spells\Fire_Cast_Hand.mdx"
                    string originalFilePath = spellVisualEffectNameDbc.ReadString(effectRow[2]);
                    // For MPQ lookup, normalize extension to .m2 (client files are .m2)
                    string originalM2Path = NormalizeM2Extension(originalFilePath);

                    // Build custom name using the naming convention
                    string role = $"{VisualKitNames[i]}_{KitEffectNames[j]}";
                    string customName = BuildCustomEffectName(spellName, role);
                    string customM2Path = EffectNameToM2Path(customName);

                    // Update field [1] — display name
                    uint newNameOffset = spellVisualEffectNameDbc.AddString(customName);
                    spellVisualEffectNameDbc.PatchRow(newEffectId, 1, newNameOffset);

                    // ═══ SESSION 9 FIX: Patch field [2] — FilePath (the ACTUAL M2 path) ═══
                    // Session 8 root cause: field [2] is a stringref to the M2 file path.
                    // The client loads M2s from this field, NOT from field [1].
                    // Without this patch, custom M2s in the MPQ are never loaded.
                    uint newPathOffset = spellVisualEffectNameDbc.AddString(customM2Path);
                    spellVisualEffectNameDbc.PatchRow(newEffectId, 2, newPathOffset);

                    result.EffectFiles.Add(new EffectFileMapping
                    {
                        NewEffectId = newEffectId,
                        OriginalName = originalName,
                        OriginalM2Path = originalM2Path,
                        CustomName = customName,
                        CustomM2Path = customM2Path,
                        EffectRole = role
                    });
                }

                spellVisualKitDbc.PatchRow(newKitId, effectFieldIdx, newEffectId);
            }
        }

        // ── Step 4: Handle missile effect ──
        // Field 7 is the missile's SpellVisualEffectName ID (Fireball=365).
        // Field 6 is only the gate and field 8 is unmapped/zero — neither is cloned.
        uint oldMissileEffectId = visualRow[7];
        if (!IsNone(oldMissileEffectId))
        {
            if (!result.EffectNameIdMap.TryGetValue(oldMissileEffectId, out uint newMissileEffectId))
            {
                newMissileEffectId = nextEffectId++;
                result.EffectNameIdMap[oldMissileEffectId] = newMissileEffectId;

                var missileRow = spellVisualEffectNameDbc.CloneRow(oldMissileEffectId, newMissileEffectId);
                string originalName = spellVisualEffectNameDbc.ReadString(missileRow[1]);
                string originalFilePath = spellVisualEffectNameDbc.ReadString(missileRow[2]);
                string originalM2Path = NormalizeM2Extension(originalFilePath);

                string customName = BuildCustomEffectName(spellName, "missile");
                string customM2Path = EffectNameToM2Path(customName);

                // Update field [1] — display name
                uint newNameOffset = spellVisualEffectNameDbc.AddString(customName);
                spellVisualEffectNameDbc.PatchRow(newMissileEffectId, 1, newNameOffset);

                // ═══ SESSION 9 FIX: Patch field [2] — FilePath ═══
                uint newPathOffset = spellVisualEffectNameDbc.AddString(customM2Path);
                spellVisualEffectNameDbc.PatchRow(newMissileEffectId, 2, newPathOffset);

                result.EffectFiles.Add(new EffectFileMapping
                {
                    NewEffectId = newMissileEffectId,
                    OriginalName = originalName,
                    OriginalM2Path = originalM2Path,
                    CustomName = customName,
                    CustomM2Path = customM2Path,
                    EffectRole = "missile"
                });
            }

            result.MissileEffectId = newMissileEffectId;
            spellVisualDbc.PatchRow(newVisualId, 7, newMissileEffectId);
        }

        return result;
    }
}