using Dapper;
using MangosSuperUI.Models;
using MySqlConnector;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The single atomic id-reservation authority for both custom namespaces — item_template entries
/// and ItemDisplayInfo display ids (WEAPON_GEN.md §2.2, §4.2, §13.4). It replaces the racy
/// read-then-write MAX+1 schemes in ItemsController / ItemRetextureService for anything the Forge
/// commits.
///
/// Guarantees:
///   • A reservation is taken inside one transaction under a SELECT … FOR UPDATE lock on the
///     allocator row, so two concurrent builds can never mint the same id.
///   • An id is exclusively owned while its weapon exists. Deleting a weapon RELEASES its ids
///     (<see cref="ReleaseAsync"/>) for reuse — the audit log is the history, not this table.
///     Reuse is safe because every reserve recomputes its floor from the live item_template max,
///     the base DBC max, and the surviving reservations.
///   • A retry presenting the same (build_id, kind, slot) receives the SAME id — the allocator is
///     not advanced just because transport or compilation was retried.
///   • Allocation fails closed above the live MEDIUMINT UNSIGNED ceiling (16,777,215); it never
///     wraps or truncates.
///
/// Floor computation (what the first reservation starts from) is domain-specific — it must consult
/// the live item_template max and the clean-base DBC max — so callers pass a floor they computed;
/// <see cref="ReserveAsync"/> only ever raises the allocator to meet that floor, never lowers it.
/// </summary>
public sealed class WeaponIdReservationService
{
    /// <summary>The live MEDIUMINT UNSIGNED ceiling shared by item_template.entry and display_id.</summary>
    public const long MediumIntUnsignedMax = 16_777_215;

    public const string KindItemEntry = "item_entry";
    public const string KindItemDisplay = "item_display";

    /// <summary>Configured custom-entry floor (mirrors ItemsController.CUSTOM_RANGE_START).</summary>
    public const long ItemEntryFloor = 900_000;

    /// <summary>Configured custom-display floor (mirrors ItemRetextureService.CUSTOM_DISPLAY_BASE),
    /// already proven in-client for 60000+ ItemDisplayInfo ids.</summary>
    public const long ItemDisplayFloor = 60_000;

    private readonly ConnectionFactory _db;
    private readonly ILogger<WeaponIdReservationService> _logger;

    public WeaponIdReservationService(ConnectionFactory db, ILogger<WeaponIdReservationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Reserve (or re-fetch) one id in <paramref name="kind"/> for the stable slot
    /// (<paramref name="buildId"/>, <paramref name="slot"/>). <paramref name="floor"/> is the
    /// lowest id that would be safe to hand out given every source the caller knows (live max, DBC
    /// max, prior reservations); the allocator is raised to it if needed but never lowered.
    /// </summary>
    public async Task<ReservationResult> ReserveAsync(string kind, long floor, string buildId, string slot)
    {
        if (kind != KindItemEntry && kind != KindItemDisplay)
            throw new ArgumentException($"Unknown reservation kind '{kind}'.", nameof(kind));
        if (string.IsNullOrWhiteSpace(buildId)) throw new ArgumentException("buildId required.", nameof(buildId));
        if (string.IsNullOrWhiteSpace(slot)) throw new ArgumentException("slot required.", nameof(slot));
        if (floor < 1) floor = 1;

        await using var conn = _db.Admin();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            // Ensure the allocator row exists and is at least the caller's floor. GREATEST only
            // ever raises next_id, so a higher live/DBC max computed on a later build self-heals a
            // row that was bootstrapped from a staler floor.
            await conn.ExecuteAsync(
                @"INSERT INTO custom_id_allocator (kind, next_id) VALUES (@kind, @floor)
                  ON DUPLICATE KEY UPDATE next_id = GREATEST(next_id, @floor), updated_at = CURRENT_TIMESTAMP(3)",
                new { kind, floor }, tx);

            // Idempotency: a prior attempt with this exact slot already owns an id.
            var existing = await conn.QueryFirstOrDefaultAsync<long?>(
                @"SELECT id FROM custom_id_reservation
                  WHERE build_id = @buildId AND kind = @kind AND slot = @slot",
                new { buildId, kind, slot }, tx);
            if (existing is { } prior)
            {
                await tx.CommitAsync();
                return new ReservationResult(kind, prior, buildId, slot, WasNewlyReserved: false);
            }

            // Lock the allocator row and take the next id.
            long id = await conn.ExecuteScalarAsync<long>(
                "SELECT next_id FROM custom_id_allocator WHERE kind = @kind FOR UPDATE",
                new { kind }, tx);

            if (id > MediumIntUnsignedMax)
            {
                await tx.RollbackAsync();
                throw new InvalidOperationException(
                    $"Reservation for '{kind}' would allocate id {id}, above the MEDIUMINT UNSIGNED ceiling {MediumIntUnsignedMax}. Failing closed.");
            }

            await conn.ExecuteAsync(
                @"INSERT INTO custom_id_reservation (kind, id, build_id, slot, state)
                  VALUES (@kind, @id, @buildId, @slot, 'reserved')",
                new { kind, id, buildId, slot }, tx);

            await conn.ExecuteAsync(
                "UPDATE custom_id_allocator SET next_id = next_id + 1, updated_at = CURRENT_TIMESTAMP(3) WHERE kind = @kind",
                new { kind }, tx);

            await tx.CommitAsync();
            _logger.LogInformation("WeaponForge: reserved {Kind} id {Id} for build {Build}/{Slot}", kind, id, buildId, slot);
            return new ReservationResult(kind, id, buildId, slot, WasNewlyReserved: true);
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { /* connection may already be gone */ }
            throw;
        }
    }

    /// <summary>Advance a reservation's terminal state (committed / failed / handed_off).</summary>
    public async Task MarkStateAsync(string kind, long id, string state)
    {
        await using var conn = _db.Admin();
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            @"UPDATE custom_id_reservation
              SET state = @state, committed_at = CASE WHEN @state = 'committed' THEN CURRENT_TIMESTAMP(3) ELSE committed_at END
              WHERE kind = @kind AND id = @id",
            new { kind, id, state });
    }

    /// <summary>
    /// Release a deleted weapon's id back to the pool: the reservation row is removed and the
    /// allocator falls back to one above the highest SURVIVING reservation, so freed ids get
    /// reused. Done under the same allocator FOR UPDATE lock as ReserveAsync, so a concurrent
    /// build can't race the fallback. The audit log carries the deletion history.
    /// </summary>
    public async Task ReleaseAsync(string kind, long id)
    {
        await using var conn = _db.Admin();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await conn.ExecuteScalarAsync<long?>(
                "SELECT next_id FROM custom_id_allocator WHERE kind = @kind FOR UPDATE",
                new { kind }, tx);

            await conn.ExecuteAsync(
                "DELETE FROM custom_id_reservation WHERE kind = @kind AND id = @id",
                new { kind, id }, tx);

            await conn.ExecuteAsync(
                @"UPDATE custom_id_allocator
                  SET next_id = (SELECT COALESCE(MAX(id), 0) + 1 FROM custom_id_reservation WHERE kind = @kind),
                      updated_at = CURRENT_TIMESTAMP(3)
                  WHERE kind = @kind",
                new { kind }, tx);

            await tx.CommitAsync();
            _logger.LogInformation("WeaponForge: released {Kind} id {Id} for reuse", kind, id);
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { /* connection may already be gone */ }
            throw;
        }
    }

    /// <summary>
    /// Compute the safe item_template entry floor: the max of the configured floor, one above the
    /// current live custom-range max, and one above the highest entry ever reserved. Best-effort —
    /// an unreachable mangos DB degrades to the configured floor + reservation history rather than
    /// throwing, so a display-only proof can still run.
    /// </summary>
    public async Task<long> ComputeItemEntryFloorAsync()
    {
        long floor = ItemEntryFloor;

        try
        {
            await using var mangos = _db.Mangos();
            await mangos.OpenAsync();
            var liveMax = await mangos.ExecuteScalarAsync<long?>(
                "SELECT MAX(entry) FROM item_template WHERE entry >= @floor", new { floor = ItemEntryFloor });
            if (liveMax is { } m) floor = Math.Max(floor, m + 1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WeaponForge: item_template max unavailable; entry floor falls back to reservations only");
        }

        floor = Math.Max(floor, await MaxReservedPlusOneAsync(KindItemEntry));
        return floor;
    }

    /// <summary>
    /// Compute the safe display-id floor. <paramref name="cleanDbcMaxId"/> is the highest id in the
    /// clean-base ItemDisplayInfo.dbc (the caller reads it; 0 if unknown). Also folds in the
    /// existing retexture/atlas display maxes and the reservation history.
    /// </summary>
    public async Task<long> ComputeDisplayIdFloorAsync(uint cleanDbcMaxId)
    {
        long floor = Math.Max(ItemDisplayFloor, (long)cleanDbcMaxId + 1);

        try
        {
            await using var admin = _db.Admin();
            await admin.OpenAsync();
            var retexMax = await admin.ExecuteScalarAsync<long?>(
                @"SELECT MAX(m) FROM (
                    SELECT MAX(new_display_id) AS m FROM custom_item_retexture
                    UNION ALL SELECT MAX(new_display_id) FROM custom_item_retexture_atlas
                    UNION ALL SELECT MAX(display_id) FROM custom_weapon_display
                  ) t");
            if (retexMax is { } m) floor = Math.Max(floor, m + 1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WeaponForge: custom display maxes unavailable; display floor uses DBC/reservations only");
        }

        floor = Math.Max(floor, await MaxReservedPlusOneAsync(KindItemDisplay));
        return floor;
    }

    private async Task<long> MaxReservedPlusOneAsync(string kind)
    {
        try
        {
            await using var conn = _db.Admin();
            await conn.OpenAsync();
            var max = await conn.ExecuteScalarAsync<long?>(
                "SELECT MAX(id) FROM custom_id_reservation WHERE kind = @kind", new { kind });
            return (max ?? 0) + 1;
        }
        catch
        {
            return 1;
        }
    }
}

/// <summary>Outcome of a reservation: the id, its stable slot, and whether it was freshly taken
/// (false = an earlier attempt for this slot already owned it).</summary>
public sealed record ReservationResult(string Kind, long Id, string BuildId, string Slot, bool WasNewlyReserved);
