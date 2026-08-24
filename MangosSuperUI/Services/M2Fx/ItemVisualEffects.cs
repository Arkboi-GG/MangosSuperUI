using System.Numerics;

namespace MangosSuperUI.Services.M2Fx;

/// <summary>
/// The THIRD way vanilla animates an item, and the one that carries most of the ones people actually
/// name: <c>ItemDisplayInfo.itemVisual</c>.
///
/// Material tracks live on the item's own materials and particle emitters live in the item's own M2,
/// but an enchant glow, Thunderfury's lightning, Sulfuras's flame and the Warglaives' fire are none of
/// those. They are SEPARATE effect models the client mounts onto the item at render time:
///
///   ItemDisplayInfo.itemVisual → ItemVisuals.dbc row → up to 5 ItemVisualEffects.dbc ids
///                              → each names a model, e.g. Spells\Enchantments\RedFlame_Low.mdx
///
/// Nothing in the item's own bytes hints that any of this exists, which is why an item can be fully
/// decoded, correctly rendered, and still look dead next to the game.
///
/// === The mounting rule (measured on the 1.12 client — do NOT re-derive) ===
///
/// Effect SLOT INDEX selects the host attachment with the matching ID. Two clean confirmations:
///
///   Polearm_2H_Trident_C_01 — attachments 1,2,3,4 sit on the three prongs and the shaft;
///     ItemVisuals[25] = [0,45,45,45,45], i.e. slots 1-4 → four copies of RedFlame_Low, one per prong.
///   Sword_2H_Horde_C_02 — attachments 0..4 run hilt→tip up the blade;
///     ItemVisuals[1] = [0,0,0,42,0], i.e. slot 3 → SkullBalls at attachment 3, up the blade.
///
/// A slot with no matching attachment mounts at the model origin, which is what the client does with
/// a single-attachment item and is better than dropping the effect.
/// </summary>
public static class ItemVisualEffects
{
    /// <summary>ItemVisuals.dbc: id + 5 effect ids = 6 fields.</summary>
    private const int VisualSlots = 5;

    /// <summary>One effect model, resolved and ready to fold into a host GLB.</summary>
    /// <param name="ModelPath">MPQ path as ItemVisualEffects.dbc names it.</param>
    /// <param name="M2">The effect model's bytes.</param>
    /// <param name="Textures">Its own textures, by M2 slot, as BLP bytes.</param>
    /// <param name="MountMesh">Where it hangs on the host, in the host's Y-up mesh space.</param>
    public sealed record Effect(string ModelPath, byte[] M2, Dictionary<int, byte[]> Textures, Vector3 MountMesh);

    /// <summary>
    /// Resolve an item visual into loaded effect models, positioned against the host's attachments.
    /// </summary>
    /// <param name="itemVisualId">ItemDisplayInfo.itemVisual. Zero means the item has no visual.</param>
    /// <param name="host">The item's own parsed model — for its attachment points.</param>
    /// <param name="read">MPQ reader. Effect models and their textures both come through it.</param>
    /// <param name="mountForSlot">Optional mount override: slot → position in the preview's Y-up
    /// mesh space (null skips the slot). Used when there is no host M2 to read attachments from —
    /// the GLB import route mounts on the same evenly-spread anchors its forge writes.</param>
    /// <returns>Empty whenever anything is missing. A visual that cannot be resolved must degrade to
    /// "no extra effect", never to an exception on a preview path.</returns>
    public static List<Effect> Resolve(uint itemVisualId, M2Model? host, Func<string, byte[]?> read,
        Func<int, Vector3?>? mountForSlot = null)
    {
        var result = new List<Effect>();
        if (itemVisualId == 0) return result;

        try
        {
            var slots = ReadVisualSlots(itemVisualId, read);
            if (slots.Count == 0) return result;

            var models = ReadEffectModelPaths(read);
            if (models.Count == 0) return result;

            for (int slot = 0; slot < slots.Count; slot++)
            {
                uint effectId = slots[slot];
                if (effectId == 0) continue;
                if (!models.TryGetValue(effectId, out string? modelPath) || string.IsNullOrWhiteSpace(modelPath))
                    continue;

                var bytes = ReadModel(modelPath, read);
                if (bytes is not { Length: > 0x148 }) continue;
                if (System.Text.Encoding.ASCII.GetString(bytes, 0, 4) != "MD20") continue;
                if (BitConverter.ToUInt32(bytes, 4) >= 264) continue;   // v264+ has a different layout

                var mountAt = mountForSlot is not null ? mountForSlot(slot) : MountFor(host, slot);
                if (mountAt is not { } mount) continue;

                var textures = ReadTextureTable(bytes, read);
                result.Add(new Effect(modelPath, bytes, textures, mount));
            }
        }
        catch
        {
            // A visual is an enhancement. Never let a malformed DBC take a preview with it.
        }

        return result;
    }

    /// <summary>
    /// The effect model's own texture table, read straight from the bytes.
    ///
    /// Deliberately NOT via <c>M2Reader.Parse</c>: that is a render reader and it rejects these
    /// outright. An enchant effect is a geometry-less M2 — RedFlame_Low is 2.7 KB with one emitter,
    /// no vertices and no views — which is exactly the shape a mesh parser has no reason to accept
    /// and exactly the shape this needs. Only the texture table and the emitter records matter here,
    /// and both are plain header arrays.
    /// </summary>
    private static Dictionary<int, byte[]> ReadTextureTable(byte[] m2, Func<string, byte[]?> read)
    {
        const int HdrTextures = 0x05C, EntrySize = 16;
        var textures = new Dictionary<int, byte[]>();

        uint count = U32(m2, HdrTextures), offset = U32(m2, HdrTextures + 4);
        if (count == 0 || count > 64 || offset == 0) return textures;

        for (uint i = 0; i < count; i++)
        {
            int entry = (int)(offset + i * EntrySize);
            if (entry + EntrySize > m2.Length) break;

            uint nameLength = U32(m2, entry + 8), nameOffset = U32(m2, entry + 12);
            if (nameLength <= 1 || nameOffset == 0 || nameOffset + nameLength > m2.Length) continue;

            string file = System.Text.Encoding.ASCII
                .GetString(m2, (int)nameOffset, (int)nameLength - 1).TrimEnd('\0');
            if (file.Length == 0) continue;

            var blp = read(file) ?? read(file.ToLowerInvariant());
            if (blp is { Length: > 0 }) textures[(int)i] = blp;
        }
        return textures;
    }

    /// <summary>
    /// Host attachment whose id matches the effect slot.
    ///
    /// Null when the host HAS attachments but none with that id: the client cannot mount an effect on
    /// a point that does not exist, and piling it at the origin puts a flame inside the grip. The
    /// Trident is the case — ids 1..4 for its prongs and no id 0, while the visual fills all five
    /// slots. A host with no attachments at all falls back to the origin, which is the only place it
    /// could go and is better than dropping the effect entirely.
    /// </summary>
    private static Vector3? MountFor(M2Model? host, int slot)
    {
        if (host is null || host.Attachments.Count == 0) return Vector3.Zero;
        foreach (var attachment in host.Attachments)
            if (attachment.Id == (uint)slot)
                return attachment.Position;
        return null;
    }

    /// <summary>The five effect ids of one ItemVisuals row.</summary>
    private static List<uint> ReadVisualSlots(uint itemVisualId, Func<string, byte[]?> read)
    {
        var slots = new List<uint>();
        var dbc = read(@"DBFilesClient\ItemVisuals.dbc");
        if (dbc is not { Length: > 20 }) return slots;

        uint records = U32(dbc, 4), recordSize = U32(dbc, 12);
        if (recordSize < (VisualSlots + 1) * 4) return slots;

        for (uint r = 0; r < records; r++)
        {
            int o = (int)(20 + r * recordSize);
            if (o + recordSize > dbc.Length) break;
            if (U32(dbc, o) != itemVisualId) continue;
            for (int k = 1; k <= VisualSlots; k++) slots.Add(U32(dbc, o + k * 4));
            break;
        }
        return slots;
    }

    /// <summary>ItemVisualEffects.dbc: effect id → model path. Read whole because a visual usually
    /// names several and the table is small (a few hundred rows).</summary>
    private static Dictionary<uint, string> ReadEffectModelPaths(Func<string, byte[]?> read)
    {
        var map = new Dictionary<uint, string>();
        var dbc = read(@"DBFilesClient\ItemVisualEffects.dbc");
        if (dbc is not { Length: > 20 }) return map;

        uint records = U32(dbc, 4), recordSize = U32(dbc, 12), stringSize = U32(dbc, 16);
        if (recordSize < 8) return map;
        int stringBase = (int)(20 + records * recordSize);

        for (uint r = 0; r < records; r++)
        {
            int o = (int)(20 + r * recordSize);
            if (o + recordSize > dbc.Length) break;
            uint id = U32(dbc, o), nameOffset = U32(dbc, o + 4);
            if (id == 0 || nameOffset == 0) continue;

            int start = stringBase + (int)nameOffset;
            if (start >= dbc.Length || nameOffset >= stringSize) continue;
            int end = start;
            while (end < dbc.Length && dbc[end] != 0) end++;
            if (end > start) map[id] = System.Text.Encoding.ASCII.GetString(dbc, start, end - start);
        }
        return map;
    }

    /// <summary>ItemVisualEffects names models with the .mdx extension the client swaps for .m2.</summary>
    private static byte[]? ReadModel(string path, Func<string, byte[]?> read)
    {
        var bytes = read(path);
        if (bytes is { Length: > 0 }) return bytes;

        if (path.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase))
        {
            string swapped = path[..^4] + ".m2";
            bytes = read(swapped) ?? read(swapped.ToLowerInvariant());
            if (bytes is { Length: > 0 }) return bytes;
        }
        return read(path.ToLowerInvariant());
    }

    private static uint U32(byte[] d, int o) =>
        o < 0 || o + 4 > d.Length ? 0u : BitConverter.ToUInt32(d, o);
}
