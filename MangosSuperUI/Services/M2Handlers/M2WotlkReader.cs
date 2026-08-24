using System.Numerics;
using System.Text;

namespace MangosSuperUI.Services;

/// <summary>
/// Reader for the WotLK (3.x) M2 layout — <c>MD20</c> version <b>264</b> — producing the same
/// <see cref="M2Model"/> the vanilla/TBC <see cref="M2Reader"/> produces, so everything downstream
/// (<c>LegacyWeaponMeshExtractor</c>, the donor-scaffold writers, the GLB preview) is version-agnostic.
///
/// What changed between 2.4.3 (v260–263) and 3.3.5 (v264), all measured against the local clients:
///
///   • <b>Header</b>: <c>playableAnimationLookup</c> and the texture-flipbook ("unknown") array were
///     removed, and the inline view table became a bare <c>uint32 nViews</c> — so every header
///     field from <c>bones</c> (0x2C) onward sits at a different offset than the ≤263 layout the
///     vanilla reader hard-codes.
///   • <b>Views live in external <c>.skin</c> files</b> (<c>{Model}00.skin</c> … one per LOD). The
///     caller supplies the bytes of profile 0; the skin keeps the ≤263 record shapes (48-byte
///     submesh, 24-byte batch) behind a <c>'SKIN'</c> header.
///   • <b>Animation tracks</b> (<c>M2Track</c>) shrank from 28 to 20 bytes: the per-sequence range
///     table is gone; timestamps and values are <i>arrays of per-sequence arrays</i>. Colors are
///     2 tracks (40 B), transparency 1 (20 B), UV transforms 3 (60 B), bones 88 B, attachments 40 B,
///     sequences 64 B.
///
/// Like the TBC path this is a <i>static rest-pose</i> reader for the fidelity import: bone TRS
/// tracks are not decoded (the forge re-emits rigid geometry on a vanilla donor), while the
/// material tracks reachable from view-0 batches are sampled at their rest key with the same
/// validation/fail-closed contract the TBC reader applies, and range-free global-sequence
/// UV animation is decoded for transplant.
/// </summary>
public static class M2WotlkReader
{
    public const uint MinVersion = 264;
    /// <summary>Upper bound we accept — Cataclysm raised the version again (272+) with a different
    /// header; nothing above 264 ships in a 3.3.5a client.</summary>
    public const uint MaxVersion = 264;

    private const int VERTEX_STRIDE = 48;
    private const int SEQUENCE_STRIDE = 64;
    private const int BONE_STRIDE = 88;
    private const int ATTACHMENT_STRIDE = 40;
    private const int TRACK_STRIDE = 20;

    // Header offsets (v264, see class summary).
    private const int H_Name = 0x08, H_GlobalFlags = 0x10, H_GlobalSeq = 0x14, H_Sequences = 0x1C,
        H_Bones = 0x2C, H_KeyBoneLookup = 0x34, H_Vertices = 0x3C, H_NumViews = 0x44, H_Colors = 0x48,
        H_Textures = 0x50, H_Transparency = 0x58, H_TexTransforms = 0x60, H_RenderFlags = 0x70,
        H_TexLookup = 0x80, H_TexCoordLookup = 0x88, H_TransLookup = 0x90, H_TexTransformLookup = 0x98,
        H_Attachments = 0xF0, H_AttachLookup = 0xF8, H_Ribbons = 0x120, H_Particles = 0x128;
    private const int HeaderSize = 0x130;

    /// <summary>Skin-profile member name for an M2 MPQ path: <c>Dir\Model.m2</c> → <c>Dir\Model00.skin</c>.</summary>
    public static string SkinPathFor(string m2MpqPath, int profile = 0)
    {
        string stem = m2MpqPath;
        if (stem.EndsWith(".m2", StringComparison.OrdinalIgnoreCase) || stem.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase))
            stem = stem[..stem.LastIndexOf('.')];
        return $"{stem}{profile:D2}.skin";
    }

    /// <summary>True when the bytes carry a WotLK-era (v264) MD20 header.</summary>
    public static bool IsWotlk(byte[] data)
    {
        if (data is null || data.Length < 8) return false;
        if (data[0] != 'M' || data[1] != 'D' || data[2] != '2' || data[3] != '0') return false;
        uint v = BitConverter.ToUInt32(data, 4);
        return v >= MinVersion && v <= MaxVersion;
    }

    /// <summary>Parse a v264 M2 plus its profile-0 skin. Null when either is malformed.</summary>
    public static M2Model? Parse(byte[] data, byte[]? skin)
    {
        if (data is null || data.Length < HeaderSize || skin is null || skin.Length < 0x30) return null;
        try
        {
            if (Encoding.ASCII.GetString(data, 0, 4) != "MD20") return null;
            // SourceBytes stays null on this lane on purpose: M2FxReader decodes the ≤ v263 track
            // layout (28-byte tracks, flat value arrays) and a v264 record would decode to garbage.
            // The armor pre-import preview renders a WotLK model directly, so it simply gets no
            // animation manifest — the POST-import preview reads the forged v256 model and does.
            var model = new M2Model { Version = ReadUInt32(data, 0x04) };
            if (model.Version < MinVersion || model.Version > MaxVersion) return null;

            uint nName = ReadUInt32(data, H_Name), ofsName = ReadUInt32(data, H_Name + 4);
            if (nName > 0 && ofsName > 0 && ofsName + nName <= data.Length)
                model.Name = Encoding.ASCII.GetString(data, (int)ofsName, (int)nName).TrimEnd('\0');

            ParseGlobalSequences(data, ReadUInt32(data, H_GlobalSeq), ReadUInt32(data, H_GlobalSeq + 4), model);
            ParseSequences(data, ReadUInt32(data, H_Sequences), ReadUInt32(data, H_Sequences + 4), model);
            ParseBones(data, ReadUInt32(data, H_Bones), ReadUInt32(data, H_Bones + 4), model);
            ParseShortLookup(data, ReadUInt32(data, H_KeyBoneLookup), ReadUInt32(data, H_KeyBoneLookup + 4), model.KeyBoneLookup);

            uint nVertices = ReadUInt32(data, H_Vertices), ofsVertices = ReadUInt32(data, H_Vertices + 4);
            if (nVertices == 0 || ofsVertices == 0 || ofsVertices >= data.Length) return null;
            if (!ParseVertices(data, nVertices, ofsVertices, model)) return null;

            if (ReadUInt32(data, H_NumViews) == 0) return null;
            if (!ParseSkin(skin, model)) return null;

            ParseTextures(data, ReadUInt32(data, H_Textures), ReadUInt32(data, H_Textures + 4), model);
            ParseUShortLookup(data, ReadUInt32(data, H_TexLookup), ReadUInt32(data, H_TexLookup + 4), model.TextureLookup);
            ParseUShortLookup(data, ReadUInt32(data, H_TexCoordLookup), ReadUInt32(data, H_TexCoordLookup + 4), model.TextureCoordinateLookup);
            ParseUShortLookup(data, ReadUInt32(data, H_TexTransformLookup), ReadUInt32(data, H_TexTransformLookup + 4), model.TextureTransformLookup);
            ParseRenderFlags(data, ReadUInt32(data, H_RenderFlags), ReadUInt32(data, H_RenderFlags + 4), model);
            ParseTransparencyStaticAlphas(data, ReadUInt32(data, H_Transparency), ReadUInt32(data, H_Transparency + 4), model);
            ParseUShortLookup(data, ReadUInt32(data, H_TransLookup), ReadUInt32(data, H_TransLookup + 4), model.TransparencyLookup);
            ParseReachableMaterialTracks(data, model);
            model.RibbonEmitterCount = ReadUInt32(data, H_Ribbons);
            model.ParticleEmitterCount = ReadUInt32(data, H_Particles);
            ParseParticleEmitters(data, model.ParticleEmitterCount, ReadUInt32(data, H_Particles + 4), model);

            ParseAttachments(data, ReadUInt32(data, H_Attachments), ReadUInt32(data, H_Attachments + 4), model);
            ParseShortLookup(data, ReadUInt32(data, H_AttachLookup), ReadUInt32(data, H_AttachLookup + 4), model.AttachmentLookup);

            return model.IsValid ? model : null;
        }
        catch
        {
            return null;
        }
    }

    // ── vertices (identical record to ≤263) ─────────────────────────────
    private static bool ParseVertices(byte[] data, uint count, uint offset, M2Model model)
    {
        if (offset + (long)count * VERTEX_STRIDE > data.Length) return false;
        model.Vertices.Capacity = (int)count;
        for (uint i = 0; i < count; i++)
        {
            int off = (int)(offset + i * VERTEX_STRIDE);
            float px = ReadFloat(data, off), py = ReadFloat(data, off + 4), pz = ReadFloat(data, off + 8);
            float nx = ReadFloat(data, off + 20), ny = ReadFloat(data, off + 24), nz = ReadFloat(data, off + 28);
            model.Vertices.Add(new M2Vertex
            {
                PosX = px, PosY = pz, PosZ = -py,
                NormX = nx, NormY = nz, NormZ = -ny,
                TexU = ReadFloat(data, off + 32), TexV = ReadFloat(data, off + 36),
                TexU2 = ReadFloat(data, off + 40), TexV2 = ReadFloat(data, off + 44),
                BoneWeight0 = data[off + 12], BoneWeight1 = data[off + 13], BoneWeight2 = data[off + 14], BoneWeight3 = data[off + 15],
                BoneIndex0 = data[off + 16], BoneIndex1 = data[off + 17], BoneIndex2 = data[off + 18], BoneIndex3 = data[off + 19],
            });
        }
        return model.Vertices.Count > 0;
    }

    // ── skin profile ('SKIN' + 5 M2Arrays + boneCountMax) ───────────────
    //   0x00 magic, 0x04 vertices(uint16), 0x0C indices(uint16), 0x14 bones(ubyte4),
    //   0x1C submeshes(48 B), 0x24 batches(24 B), 0x2C boneCountMax
    private static bool ParseSkin(byte[] skin, M2Model model)
    {
        if (Encoding.ASCII.GetString(skin, 0, 4) != "SKIN") return false;
        uint nLocal = ReadUInt32(skin, 0x04), ofsLocal = ReadUInt32(skin, 0x08);
        uint nTri = ReadUInt32(skin, 0x0C), ofsTri = ReadUInt32(skin, 0x10);
        uint nSub = ReadUInt32(skin, 0x1C), ofsSub = ReadUInt32(skin, 0x20);
        uint nBatch = ReadUInt32(skin, 0x24), ofsBatch = ReadUInt32(skin, 0x28);

        if (nLocal == 0 || ofsLocal == 0 || ofsLocal + (long)nLocal * 2 > skin.Length) return false;
        if (nTri == 0 || ofsTri == 0 || ofsTri + (long)nTri * 2 > skin.Length) return false;

        var localMap = new ushort[nLocal];
        for (uint i = 0; i < nLocal; i++) localMap[i] = ReadUInt16(skin, (int)(ofsLocal + i * 2));

        model.Indices.Capacity = (int)nTri;
        for (uint i = 0; i < nTri; i++)
        {
            ushort local = ReadUInt16(skin, (int)(ofsTri + i * 2));
            model.Indices.Add(local < nLocal ? localMap[local] : (ushort)0);
        }

        if (nSub > 0)
        {
            if (ofsSub == 0 || ofsSub + (long)nSub * 48 > skin.Length) return false;
            for (uint i = 0; i < nSub; i++)
            {
                int o = checked((int)(ofsSub + i * 48L));
                // 0x00 id, 0x02 level (high word for the start fields on huge models — zero here),
                // 0x04 vertexStart, 0x06 vertexCount, 0x08 indexStart, 0x0A indexCount …
                model.Submeshes.Add(new M2Submesh
                {
                    Id = ReadUInt16(skin, o),
                    VertexStart = ReadUInt16(skin, o + 4),
                    VertexCount = ReadUInt16(skin, o + 6),
                    IndexStart = ReadUInt16(skin, o + 8),
                    IndexCount = ReadUInt16(skin, o + 10),
                });
            }
        }

        if (nBatch > 0 && ofsBatch > 0 && ofsBatch + (long)nBatch * 24 <= skin.Length)
        {
            for (uint i = 0; i < nBatch; i++)
            {
                int b = (int)(ofsBatch + i * 24);
                model.Batches.Add(new M2Batch
                {
                    Flags = skin[b],
                    PriorityPlane = unchecked((sbyte)skin[b + 1]),
                    ShaderId = ReadUInt16(skin, b + 2),
                    SubmeshIndex = ReadUInt16(skin, b + 4),
                    GeosetIndex = ReadUInt16(skin, b + 6),
                    ColorIndex = (short)ReadUInt16(skin, b + 8),
                    MaterialIndex = ReadUInt16(skin, b + 10),
                    MaterialLayer = ReadUInt16(skin, b + 12),
                    TextureCount = ReadUInt16(skin, b + 14),
                    TextureIndex = ReadUInt16(skin, b + 16),
                    TextureCoordinateIndex = ReadUInt16(skin, b + 18),
                    TextureWeightIndex = ReadUInt16(skin, b + 20),
                    TextureTransformIndex = ReadUInt16(skin, b + 22),
                });
            }
        }
        return model.Indices.Count >= 3;
    }

    // ── sequences (64 B) / global sequences ─────────────────────────────
    private static void ParseSequences(byte[] data, uint count, uint offset, M2Model model)
    {
        if (count == 0 || offset == 0 || offset + (long)count * SEQUENCE_STRIDE > data.Length) return;
        model.Sequences.Capacity = (int)count;
        for (uint i = 0; i < count; i++)
        {
            int off = (int)(offset + i * SEQUENCE_STRIDE);
            uint duration = ReadUInt32(data, off + 4);
            model.Sequences.Add(new M2Sequence
            {
                AnimationId = ReadUInt16(data, off),
                VariationId = ReadUInt16(data, off + 2),
                StartTimestamp = 0,
                EndTimestamp = duration,
                Flags = ReadUInt32(data, off + 12),
            });
        }
    }

    private static void ParseGlobalSequences(byte[] data, uint count, uint offset, M2Model model)
    {
        if (count == 0 || offset == 0 || offset + (long)count * 4 > data.Length) return;
        for (uint i = 0; i < count; i++) model.GlobalSequenceDurations.Add(ReadUInt32(data, (int)(offset + i * 4)));
    }

    // ── bones (88 B; pivot at +76; TRS tracks not decoded — rigid import) ──
    private static void ParseBones(byte[] data, uint count, uint offset, M2Model model)
    {
        if (count == 0 || offset == 0 || offset + (long)count * BONE_STRIDE > data.Length) return;
        model.Bones.Capacity = (int)count;
        for (uint i = 0; i < count; i++)
        {
            int off = (int)(offset + i * BONE_STRIDE);
            float px = ReadFloat(data, off + 76), py = ReadFloat(data, off + 80), pz = ReadFloat(data, off + 84);
            model.Bones.Add(new M2Bone
            {
                KeyBoneId = (int)ReadUInt32(data, off),
                Flags = ReadUInt32(data, off + 4),
                ParentBone = (short)ReadUInt16(data, off + 8),
                SubmeshId = ReadUInt16(data, off + 10),
                Pivot = new Vector3(px, pz, -py),
            });
        }
    }

    // ── attachments (40 B: id u32, bone u16, pad u16, pos, animateAttached track) ──
    private static void ParseAttachments(byte[] data, uint count, uint offset, M2Model model)
    {
        if (count == 0 || offset == 0 || offset + (long)count * ATTACHMENT_STRIDE > data.Length) return;
        for (uint i = 0; i < count; i++)
        {
            int off = (int)(offset + i * ATTACHMENT_STRIDE);
            float px = ReadFloat(data, off + 8), py = ReadFloat(data, off + 12), pz = ReadFloat(data, off + 16);
            model.Attachments.Add(new M2Attachment
            {
                Id = ReadUInt32(data, off),
                BoneIndex = ReadUInt16(data, off + 4),
                Position = new Vector3(px, pz, -py),
            });
        }
    }

    // ── textures / lookups / materials (same records as ≤263) ───────────
    private static void ParseTextures(byte[] data, uint count, uint offset, M2Model model)
    {
        if (count == 0 || offset == 0 || offset + (long)count * 16 > data.Length) return;
        for (uint i = 0; i < count; i++)
        {
            int t = (int)(offset + i * 16);
            uint nFile = ReadUInt32(data, t + 8), ofsFile = ReadUInt32(data, t + 12);
            string file = "";
            if (nFile > 1 && ofsFile > 0 && ofsFile + nFile <= data.Length)
                file = Encoding.ASCII.GetString(data, (int)ofsFile, (int)nFile).TrimEnd('\0');
            model.Textures.Add(new M2TextureRef { Type = ReadUInt32(data, t), Flags = ReadUInt32(data, t + 4), Filename = file });
        }
    }

    private static void ParseUShortLookup(byte[] data, uint count, uint offset, List<ushort> dest)
    {
        if (count == 0 || offset == 0 || offset + (long)count * 2 > data.Length) return;
        for (uint i = 0; i < count; i++) dest.Add(ReadUInt16(data, (int)(offset + i * 2)));
    }

    private static void ParseShortLookup(byte[] data, uint count, uint offset, List<short> dest)
    {
        if (count == 0 || offset == 0 || offset + (long)count * 2 > data.Length) return;
        for (uint i = 0; i < count; i++) dest.Add((short)ReadUInt16(data, (int)(offset + i * 2)));
    }

    private static void ParseRenderFlags(byte[] data, uint count, uint offset, M2Model model)
    {
        if (count == 0 || offset == 0 || offset + (long)count * 4 > data.Length) return;
        for (uint i = 0; i < count; i++)
        {
            int off = (int)(offset + i * 4);
            model.RenderFlags.Add(new M2RenderFlag { Flags = ReadUInt16(data, off), BlendingMode = ReadUInt16(data, off + 2) });
        }
    }

    // ── M2Track (20 B) rest sampling ─────────────────────────────────────
    //   +0 u16 interpolation, +2 i16 globalSequence,
    //   +4 M2Array<M2Array<u32>> timestamps, +12 M2Array<M2Array<T>> values.
    // Global-sequence tracks keep their keys in sub-array 0; sequence-driven tracks have one
    // sub-array per sequence — the rest sample is the first key of the Stand (anim 0) sequence
    // (or sequence 0 when the model has no Stand), mirroring the TBC reader's range selection.
    private delegate T KeyReader<T>(byte[] data, int offset);

    private static bool TryReadRestTrack<T>(byte[] data, int track, M2Model model, int valueStride, T defaultValue,
        KeyReader<T> readValue, out T value, out bool animationFrozen, out string? error) where T : struct
    {
        value = defaultValue; animationFrozen = false; error = null;
        if (track < 0 || track + TRACK_STRIDE > data.Length) { error = "20-byte track header is out of bounds"; return false; }

        ushort interpolation = ReadUInt16(data, track);
        short globalSequence = (short)ReadUInt16(data, track + 2);
        uint nTimeArrays = ReadUInt32(data, track + 4), ofsTimeArrays = ReadUInt32(data, track + 8);
        uint nValueArrays = ReadUInt32(data, track + 12), ofsValueArrays = ReadUInt32(data, track + 16);

        if (interpolation > 3) { error = $"invalid interpolation {interpolation}"; return false; }
        if (globalSequence < -1 || (globalSequence >= 0 && globalSequence >= model.GlobalSequenceDurations.Count))
        { error = $"global sequence {globalSequence} is outside count {model.GlobalSequenceDurations.Count}"; return false; }
        if (nValueArrays == 0) return true;                       // static: default value
        if (nTimeArrays != nValueArrays) { error = $"timestamp/value array counts differ ({nTimeArrays}/{nValueArrays})"; return false; }
        if (ofsTimeArrays == 0 || ofsTimeArrays + (long)nTimeArrays * 8 > data.Length) { error = "timestamp array table is out of bounds"; return false; }
        if (ofsValueArrays == 0 || ofsValueArrays + (long)nValueArrays * 8 > data.Length) { error = "value array table is out of bounds"; return false; }

        // Pick the rest sub-array.
        uint pick = 0;
        if (globalSequence < 0 && nValueArrays > 1)
        {
            int seq = model.TryFindSequenceIndexByAnimationId(0);
            if (seq >= 0 && seq < nValueArrays) pick = (uint)seq;
        }

        int storedStride = checked(valueStride * (interpolation is 2 or 3 ? 3 : 1));
        bool anyAnimated = false;
        T? sampled = null;
        for (uint a = 0; a < nValueArrays; a++)
        {
            int tA = checked((int)(ofsTimeArrays + a * 8L)), vA = checked((int)(ofsValueArrays + a * 8L));
            uint nT = ReadUInt32(data, tA), ofsT = ReadUInt32(data, tA + 4);
            uint nV = ReadUInt32(data, vA), ofsV = ReadUInt32(data, vA + 4);
            if (nT != nV) { error = $"sub-array {a}: timestamp/key counts differ ({nT}/{nV})"; return false; }
            if (nV == 0) continue;
            if (ofsT == 0 || ofsT + (long)nT * 4 > data.Length) { error = $"sub-array {a}: timestamp array is out of bounds"; return false; }
            if (ofsV == 0 || ofsV + (long)nV * storedStride > data.Length) { error = $"sub-array {a}: key array is out of bounds"; return false; }
            if (nV > 1) anyAnimated = true;
            if (a == pick) sampled = readValue(data, checked((int)ofsV));
        }
        // Stand had no keys but another sequence did: fall back to the first populated sub-array.
        if (sampled is null)
        {
            for (uint a = 0; a < nValueArrays; a++)
            {
                int vA = checked((int)(ofsValueArrays + a * 8L));
                uint nV = ReadUInt32(data, vA), ofsV = ReadUInt32(data, vA + 4);
                if (nV > 0) { sampled = readValue(data, checked((int)ofsV)); break; }
            }
        }
        if (sampled is not null) value = sampled.Value;
        animationFrozen = anyAnimated;
        return true;
    }

    /// <summary>Bounded transplantable animation: a multi-key global-sequence track (sub-array 0) with
    /// step/linear interpolation and strictly increasing timestamps. Sequence-driven animation fails
    /// closed exactly as in the TBC reader (it depends on the source sequence table).</summary>
    private static bool TryReadSupportedGlobalAnimation<T>(byte[] data, int track, M2Model model, int valueStride,
        KeyReader<T> readValue, Func<T, bool> isValid, out ushort interpolation, out int sourceGlobalSequence,
        out uint durationMs, out uint[]? timestamps, out T[]? keys, out string? error) where T : struct
    {
        interpolation = 0; sourceGlobalSequence = -1; durationMs = 0; timestamps = null; keys = null; error = null;
        if (track < 0 || track + TRACK_STRIDE > data.Length) { error = "20-byte track header is out of bounds"; return false; }

        interpolation = ReadUInt16(data, track);
        short globalSequence = (short)ReadUInt16(data, track + 2);
        uint nTimeArrays = ReadUInt32(data, track + 4), ofsTimeArrays = ReadUInt32(data, track + 8);
        uint nValueArrays = ReadUInt32(data, track + 12), ofsValueArrays = ReadUInt32(data, track + 16);
        if (nValueArrays == 0 || nTimeArrays == 0) return true;
        if (ofsTimeArrays == 0 || ofsTimeArrays + (long)nTimeArrays * 8 > data.Length ||
            ofsValueArrays == 0 || ofsValueArrays + (long)nValueArrays * 8 > data.Length)
        { error = "track sub-array tables are out of bounds"; return false; }

        // Multi-key anywhere?
        uint maxKeys = 0;
        for (uint a = 0; a < nValueArrays; a++) maxKeys = Math.Max(maxKeys, ReadUInt32(data, checked((int)(ofsValueArrays + a * 8L))));
        if (maxKeys <= 1) return true;

        if (interpolation > 1)
        { error = $"animated interpolation {interpolation} is not representable by the bounded vanilla writer (only step/linear are supported)"; return false; }
        if (globalSequence < 0)
        { error = "animated non-global track depends on the source sequence table and cannot be transplanted safely"; return false; }
        if ((uint)globalSequence >= (uint)model.GlobalSequenceDurations.Count)
        { error = $"global sequence {globalSequence} is outside count {model.GlobalSequenceDurations.Count}"; return false; }
        durationMs = model.GlobalSequenceDurations[globalSequence];
        if (durationMs == 0) { error = $"global sequence {globalSequence} has zero duration"; return false; }

        int tA = checked((int)ofsTimeArrays), vA = checked((int)ofsValueArrays);
        uint nT = ReadUInt32(data, tA), ofsT = ReadUInt32(data, tA + 4);
        uint nV = ReadUInt32(data, vA), ofsV = ReadUInt32(data, vA + 4);
        if (nT != nV) { error = $"timestamp/key counts differ ({nT}/{nV})"; return false; }
        if (nV <= 1) return true;
        if (ofsT == 0 || ofsT + (long)nT * 4 > data.Length) { error = "timestamp array is out of bounds"; return false; }
        if (ofsV == 0 || ofsV + (long)nV * valueStride > data.Length) { error = "key array is out of bounds"; return false; }

        timestamps = new uint[nT]; keys = new T[nV];
        uint prev = 0;
        for (int i = 0; i < timestamps.Length; i++)
        {
            uint ts = ReadUInt32(data, checked((int)(ofsT + i * 4L)));
            if (i > 0 && ts <= prev) { error = $"timestamps are not strictly increasing at key {i} ({prev}, {ts})"; timestamps = null; keys = null; return false; }
            if (ts > durationMs) { error = $"timestamp {ts} at key {i} exceeds global-sequence duration {durationMs}"; timestamps = null; keys = null; return false; }
            T v = readValue(data, checked((int)(ofsV + i * (long)valueStride)));
            if (!isValid(v)) { error = $"key {i} contains a non-finite or invalid value"; timestamps = null; keys = null; return false; }
            timestamps[i] = ts; keys[i] = v; prev = ts;
        }
        sourceGlobalSequence = globalSequence;
        return true;
    }

    private static void ParseTransparencyStaticAlphas(byte[] data, uint count, uint offset, M2Model model)
    {
        if (count == 0 || offset == 0 || offset + (long)count * TRACK_STRIDE > data.Length) return;
        for (uint i = 0; i < count; i++)
        {
            int off = (int)(offset + i * TRACK_STRIDE);
            if (TryReadRestTrack(data, off, model, 2, 1f, static (d, o) => Math.Clamp((short)ReadUInt16(d, o) / 32767f, 0f, 1f),
                    out float alpha, out bool frozen, out string? error))
            {
                model.TransparencyStaticAlphas.Add(alpha);
                if (frozen) model.FrozenTransparencyTracks.Add((int)i);
            }
            else
            {
                model.TransparencyStaticAlphas.Add(float.NaN);
                model.TransparencyStaticAlphaErrors[(int)i] = error ?? "unknown malformed track";
            }
        }
    }

    private static void ParseReachableMaterialTracks(byte[] data, M2Model model)
    {
        model.ColorTrackCount = ReadUInt32(data, H_Colors);
        uint colorOffset = ReadUInt32(data, H_Colors + 4);
        model.TextureTransformCount = ReadUInt32(data, H_TexTransforms);
        uint transformOffset = ReadUInt32(data, H_TexTransforms + 4);

        foreach (int colorIndex in model.Batches.Where(b => b.ColorIndex >= 0).Select(b => (int)b.ColorIndex).Distinct())
        {
            if ((uint)colorIndex >= model.ColorTrackCount) continue;
            long record = colorOffset + (long)colorIndex * 40;
            if (colorOffset == 0 || record + 40 > data.Length) { model.RestColorErrors[colorIndex] = "color record is outside the source file"; continue; }
            if (!TryReadRestTrack(data, checked((int)record), model, 12, Vector3.One, ReadVec3, out Vector3 rgb, out bool rgbAnim, out string? rgbErr))
            { model.RestColorErrors[colorIndex] = "RGB track: " + rgbErr; continue; }
            if (!TryReadRestTrack(data, checked((int)record + 20), model, 2, 1f, static (d, o) => Math.Clamp((short)ReadUInt16(d, o) / 32767f, 0f, 1f),
                    out float alpha, out bool alphaAnim, out string? alphaErr))
            { model.RestColorErrors[colorIndex] = "alpha track: " + alphaErr; continue; }
            if (!IsFinite(rgb) || !float.IsFinite(alpha)) { model.RestColorErrors[colorIndex] = "rest sample contains non-finite values"; continue; }
            model.ReachableRestColors[colorIndex] = new M2RestColor(rgb, alpha, rgbAnim || alphaAnim);
        }

        var reachable = new HashSet<int>();
        foreach (var batch in model.Batches)
        {
            if (batch.TextureTransformIndex == ushort.MaxValue) continue;
            for (int unit = 0; unit < batch.TextureCount; unit++)
            {
                int combo = batch.TextureTransformIndex + unit;
                if ((uint)combo >= (uint)model.TextureTransformLookup.Count) continue;
                ushort t = model.TextureTransformLookup[combo];
                if (t != ushort.MaxValue) reachable.Add(t);
            }
        }

        foreach (int ti in reachable)
        {
            if ((uint)ti >= model.TextureTransformCount) continue;
            long record = transformOffset + (long)ti * 60;
            if (transformOffset == 0 || record + 60 > data.Length) { model.RestTextureTransformErrors[ti] = "UV-transform record is outside the source file"; continue; }

            if (!TryReadRestTrack(data, checked((int)record), model, 12, Vector3.Zero, ReadVec3, out Vector3 translation, out bool trAnim, out string? err))
            { model.RestTextureTransformErrors[ti] = "translation track: " + err; continue; }
            if (!TryReadSupportedGlobalAnimation(data, checked((int)record), model, 12, ReadVec3, IsFinite,
                    out ushort trInterp, out int trSeq, out uint trDur, out uint[]? trTs, out Vector3[]? trKeys, out err))
            { model.RestTextureTransformErrors[ti] = "translation track: " + err; continue; }

            if (!TryReadRestTrack(data, checked((int)record + 20), model, 8, Quaternion.Identity, ReadCompQuat, out Quaternion rotation, out bool rotAnim, out err))
            { model.RestTextureTransformErrors[ti] = "rotation track: " + err; continue; }
            if (!TryReadSupportedGlobalAnimation(data, checked((int)record + 20), model, 8, ReadCompQuat,
                    static q => IsFinite(q) && q.LengthSquared() >= 1e-10f,
                    out ushort rotInterp, out int rotSeq, out uint rotDur, out uint[]? rotTs, out Quaternion[]? rotKeys, out err))
            { model.RestTextureTransformErrors[ti] = "rotation track: " + err; continue; }

            if (!TryReadRestTrack(data, checked((int)record + 40), model, 12, Vector3.One, ReadVec3, out Vector3 scale, out bool scAnim, out err))
            { model.RestTextureTransformErrors[ti] = "scale track: " + err; continue; }
            if (!TryReadSupportedGlobalAnimation(data, checked((int)record + 40), model, 12, ReadVec3, IsFinite,
                    out ushort scInterp, out int scSeq, out uint scDur, out uint[]? scTs, out Vector3[]? scKeys, out err))
            { model.RestTextureTransformErrors[ti] = "scale track: " + err; continue; }

            if (!IsFinite(translation) || !IsFinite(scale) || !IsFinite(rotation)) { model.RestTextureTransformErrors[ti] = "rest sample contains non-finite values"; continue; }
            if (rotation.LengthSquared() < 1e-10f) { model.RestTextureTransformErrors[ti] = "rest rotation is zero-length"; continue; }
            rotation = Quaternion.Normalize(rotation);

            var trAnimation = trTs is not null && trKeys is not null ? new M2GlobalVectorTrack(trInterp, trSeq, trDur, trTs, trKeys) : null;
            var rotAnimation = rotTs is not null && rotKeys is not null ? new M2GlobalQuaternionTrack(rotInterp, rotSeq, rotDur, rotTs, rotKeys) : null;
            var scAnimation = scTs is not null && scKeys is not null ? new M2GlobalVectorTrack(scInterp, scSeq, scDur, scTs, scKeys) : null;
            bool frozen = (trAnim && trAnimation is null) || (rotAnim && rotAnimation is null) || (scAnim && scAnimation is null);
            model.ReachableRestTextureTransforms[ti] = new M2RestTextureTransform(translation, rotation, scale, frozen, trAnimation, rotAnimation, scAnimation);
        }
    }

    // ── particle emitters (rest summary; 476-byte records, measured) ────────
    //   +8 C3Vector position   +22 uint16 texture   +40 uint8 blendingType
    //   +260 FBlock<C3Vector> colour (times M2Array, keys M2Array; floats 0–255)
    //   +292 FBlock<C2Vector> scale
    private const int PARTICLE_STRIDE = 476;

    // ── the ten float tracks, and the two ways v264 differs from ≤ v263 ─────
    //
    // 1. A v264 M2Track is 20 bytes, not 28: the per-sequence `ranges` array is gone and BOTH
    //    remaining arrays are M2Array<M2Array<T>> — an outer array indexed by sequence, each entry
    //    an inner M2Array of the actual keys. Reading the outer array as floats yields the inner
    //    count/offset words, i.e. garbage (measured: every track came back 0.000 that way).
    // 2. The tracks are NOT at a constant stride. v264 inserts a bare float after two of them —
    //    lifespanVary at 0x0AC and emissionRateVary at 0x0C4 — so everything from emissionRate on is
    //    displaced by 4 and then 8 bytes. Walking 0x34 + n*0x14 silently reads the wrong field from
    //    emissionRate onwards, which is exactly the value that decides particle density.
    //
    // Measured against LShoulder_Mail_RaidShaman_G_01.m2 (Worldbreaker, Shaman T8): with these
    // offsets emitter 0 reads lifespan 2.300 / rate 8.000 / speed 0.111 / spread 0,6.283 /
    // area 0.083² — all plausible; with a constant stride the same fields read 0.
    private static readonly int[] PARTICLE_TRACK_STARTS =
        { 0x34, 0x48, 0x5C, 0x70, 0x84, 0x98, 0xB0, 0xC8, 0xDC, 0xF0 };

    /// <summary>First keyframe of a v264 float M2Track (outer sequence array → inner key array),
    /// or 0 when the track is empty/malformed.</summary>
    private static float ReadTrackFirstFloat(byte[] data, int trackStart)
    {
        uint outerCount = ReadUInt32(data, trackStart + 12), outerOfs = ReadUInt32(data, trackStart + 16);
        if (outerCount == 0 || outerOfs == 0 || outerOfs + 8 > data.Length) return 0f;
        uint innerCount = ReadUInt32(data, (int)outerOfs), innerOfs = ReadUInt32(data, (int)outerOfs + 4);
        if (innerCount == 0 || innerOfs == 0 || innerOfs + 4 > data.Length) return 0f;
        float v = ReadFloat(data, (int)innerOfs);
        return float.IsFinite(v) ? v : 0f;
    }

    private static void ParseParticleEmitters(byte[] data, uint count, uint offset, M2Model model)
    {
        if (count == 0 || offset == 0 || offset + (long)count * PARTICLE_STRIDE > data.Length) return;
        for (uint i = 0; i < count; i++)
        {
            int o = (int)(offset + i * PARTICLE_STRIDE);
            float px = ReadFloat(data, o + 8), py = ReadFloat(data, o + 12), pz = ReadFloat(data, o + 16);
            int tex = ReadUInt16(data, o + 22);
            string? texName = tex >= 0 && tex < model.Textures.Count && model.Textures[tex].Type == 0
                ? model.Textures[tex].Filename : null;
            Vector3? colour = null;
            M2EmitterColorRamp? ramp = null;
            uint nCol = ReadUInt32(data, o + 268), ofsCol = ReadUInt32(data, o + 272);
            if (nCol > 0 && ofsCol > 0 && ofsCol + (long)nCol * 12 <= data.Length)
            {
                int k = (int)Math.Min(1, nCol - 1);   // mid key when present
                var c = ReadVec3(data, checked((int)(ofsCol + k * 12L)));
                if (IsFinite(c)) colour = c;

                // v264 stores the ramp as an FBlock of arbitrary length, while v256 has exactly three
                // keyframes. Sample first / middle / last so the curve survives the narrowing —
                // measured, every item emitter in 3.3.5a uses exactly three keys anyway.
                Vector3 Key(long index) => ReadVec3(data, checked((int)(ofsCol + Math.Clamp(index, 0, nCol - 1) * 12L)));
                var (s, m2c, e) = (Key(0), Key((nCol - 1) / 2), Key(nCol - 1));
                if (IsFinite(s) && IsFinite(m2c) && IsFinite(e)) ramp = new M2EmitterColorRamp(s, m2c, e);
            }
            // Peak across ALL keys, not just the first. The scale FBlock is a curve — Worldbreaker's
            // swirl grows 0.031 → 0.063 → 0.125 over each particle's life — and the vanilla/TBC lane
            // (M2Reader) already takes the max of its three keys. Taking key 0 here shipped that
            // emitter at a quarter of its real size, and the transplanter then rebases the donor's
            // whole grow curve onto that number, so the error multiplies through every keyframe.
            // Undersized particles stop overlapping, which is half of why the effect read as
            // separate blips rather than a body of fire.
            float scale = 0f;
            uint nSc = ReadUInt32(data, o + 300), ofsSc = ReadUInt32(data, o + 304);
            if (nSc > 0 && ofsSc > 0 && ofsSc + (long)nSc * 8 <= data.Length)
                for (uint k = 0; k < nSc; k++)
                {
                    int b = checked((int)(ofsSc + k * 8L));
                    scale = MathF.Max(scale, MathF.Max(ReadFloat(data, b), ReadFloat(data, b + 4)));
                }
            int rows = Math.Max(1, (int)ReadUInt16(data, o + 48)), cols = Math.Max(1, (int)ReadUInt16(data, o + 50));
            var t = PARTICLE_TRACK_STARTS;
            var motion = new M2EmitterMotion(
                EmissionSpeed: ReadTrackFirstFloat(data, o + t[0]),
                SpeedVariation: ReadTrackFirstFloat(data, o + t[1]),
                VerticalRange: ReadTrackFirstFloat(data, o + t[2]),
                HorizontalRange: ReadTrackFirstFloat(data, o + t[3]),
                Gravity: ReadTrackFirstFloat(data, o + t[4]),
                Lifespan: ReadTrackFirstFloat(data, o + t[5]),
                EmissionRate: ReadTrackFirstFloat(data, o + t[6]),
                EmissionAreaLength: ReadTrackFirstFloat(data, o + t[7]),
                EmissionAreaWidth: ReadTrackFirstFloat(data, o + t[8]),
                ZSource: ReadTrackFirstFloat(data, o + t[9]));
            model.ParticleEmitters.Add(new M2ParticleEmitterInfo(
                new Vector3(px, pz, -py), texName, colour, float.IsFinite(scale) ? scale : 0f, data[o + 40], rows, cols,
                motion.IsUsable ? motion : null, ramp));
        }
    }

    // ── value readers / helpers ─────────────────────────────────────────
    private static Vector3 ReadVec3(byte[] d, int o) => new(ReadFloat(d, o), ReadFloat(d, o + 4), ReadFloat(d, o + 8));

    /// <summary>M2CompQuat (4 × int16, offset-encoded) — texture space, no axis swap (same as the TBC decode).</summary>
    private static Quaternion ReadCompQuat(byte[] d, int o)
    {
        static float Decode(ushort raw) { short v = unchecked((short)raw); return (v < 0 ? v + 32768 : v - 32767) / 32767f; }
        return new Quaternion(Decode(ReadUInt16(d, o)), Decode(ReadUInt16(d, o + 2)), Decode(ReadUInt16(d, o + 4)), Decode(ReadUInt16(d, o + 6)));
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool IsFinite(Quaternion q) => float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z) && float.IsFinite(q.W);

    private static uint ReadUInt32(byte[] data, int offset) => offset < 0 || offset + 4 > data.Length ? 0u : BitConverter.ToUInt32(data, offset);
    private static ushort ReadUInt16(byte[] data, int offset) => offset < 0 || offset + 2 > data.Length ? (ushort)0 : BitConverter.ToUInt16(data, offset);
    private static float ReadFloat(byte[] data, int offset) => offset < 0 || offset + 4 > data.Length ? 0f : BitConverter.ToSingle(data, offset);
}
