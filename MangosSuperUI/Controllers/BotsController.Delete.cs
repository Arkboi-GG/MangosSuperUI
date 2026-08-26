using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;

namespace MangosSuperUI.Controllers;

// ============================================================================
//  Bot Kick + Deletion — single + mass
// ============================================================================
// RA's `.character erase` (the core's real deletion command) is unusable until
// the remoteAccess account gotcha is fixed. This purges the same tables
// SuperUI-Core's own Player::DeleteFromDB (charDelete_method 0) deletes, run
// directly against the characters DB.
//
// Safety: a guid must exist in `playerbot` before anything is touched (never
// deletes a real player), and a bot must be offline before its rows are touched
// — deleting DB rows out from under a live Player object risks the core's
// periodic autosave silently re-inserting a bare `characters` row right after.
// The difference from the old flow is that we now bring the bot offline
// ourselves instead of refusing and telling the operator to do it.
//
//  ── The four operations this file exposes ───────────────────────────────────
//      KickBot        one bot offline, stays in the DB and the core's roster
//      KickAllBots    whole fleet offline, same
//      DeleteBot      one bot offline, then purged from the DB
//      DeleteAllBots  whole fleet offline, then purged
//
//  ── Which console command does what (their names are misleading) ────────────
//  `.bot delete <name>`  does NOT delete. PlayerBotMgr::DeleteBot only sets
//      state = PB_STATE_OFFLINE and prints "disconnected"; the PlayerBotEntry
//      stays in m_bots, which is exactly what AddAllBots() looks for. So
//      `.bot delete` -> `.bot add_all` is the designed disconnect/reconnect
//      pair and the bot comes back as its real AiBotAI. That is KICK here.
//  `.kick <name>`        is the destructive one. It sets requestRemoval and
//      PlayerBotMgr::Update then ERASES the entry from m_bots (SuperUI bots are
//      customBot=true, set in PlayerBotMgr::Load whenever playerbot.ai is
//      "AiBotAI"). Re-adding afterwards falls into AddBot's no-entry branch,
//      which builds a plain PlayerBotAI with no SuperUI brain — only
//      `.bot reload` or a restart restores it. Harmless when we are about to
//      purge the row anyway, so that is DELETE here — and it is not merely
//      acceptable but REQUIRED: leaving a parked entry behind for a guid whose
//      rows we just purged means the next `.bot add_all` reaches
//      AiBotAI::OnSessionLoaded, finds no `characters` row, takes the
//      fresh-spawn branch and RECREATES the character that was just deleted.
//  `.bot stop`           is DeleteAll(): parks EVERY online entry at
//      PB_STATE_OFFLINE in one command. Used by mass delete instead of looping
//      `.kick`, because RaService serializes and the roster runs to hundreds.
//
// History: `.bot delete` was tried as an auto-kick step here and reliably
// segfaulted mangosd (status=11/SEGV, confirmed 2026-08-25 — killed the whole
// world server). Root cause was NOT the command: SuiPossess::DetachUnattendedAI
// unconditionally `delete`d the AI behind Player::AI() during OnLogout, but a
// PlayerBot's AI is owned by PlayerBotEntry::ai (a unique_ptr), so every bot
// logout freed it out from under PlayerBotMgr. Fixed in SuperUI-Core 2026-08-26
// by gating that delete on AiBotAI::IsUnattendedRealCharacter(); `.kick` on a
// bot verified non-fatal the same day. Both lanes below are safe again.
public partial class BotsController
{
    // Bot names are concatenated into a live RA console command, so they are
    // whitelisted the same way the class/race tokens in BotsController.cs are.
    // 1.12 character names are letters only, 2-12 chars.
    private static readonly Regex _botNameRe = new("^[A-Za-z]{2,12}$", RegexOptions.Compiled);

    // How long to wait for a kicked bot's bridge socket to drop before giving up.
    // PlayerBotMgr::Update runs on its own cadence, then LogoutPlayer tears the
    // Player down and ~Player -> RemoveAI() -> AiBotAI::Remove() closes the brain
    // socket, so this settles in seconds rather than milliseconds.
    private const int OfflineWaitMs = 20000;
    private const int OfflinePollMs = 250;
    // Ceiling for the whole-roster wait (see DeleteAllBots): 300+ logouts land over
    // many world ticks, but a wedged core must still fail rather than hang forever.
    private const int MassOfflineWaitMaxMs = 180000;

    [HttpGet]
    public async Task<IActionResult> RosterSummary()
    {
        using var conn = _db.Characters();
        var guids = (await conn.QueryAsync<uint>("SELECT char_guid FROM playerbot")).ToList();
        var online = guids.Count(g => _bridge.Connections.ContainsKey((int)g));
        return Json(new { total = guids.Count, online });
    }

    // ==================== Kick (disconnect, still re-addable) ====================
    // Takes the bot offline but leaves its PlayerBotEntry parked at
    // PB_STATE_OFFLINE and its `playerbot` row intact, so `.bot add_all`
    // (POST /Bots/AddAll, the "Load SuperUI Bots" button) brings it back with its
    // AiBotAI still attached rather than a bare PlayerBotAI.
    [HttpPost]
    public async Task<IActionResult> KickBot([FromBody] DeleteBotRequest req)
    {
        string? name, error;
        using (var conn = _db.Characters())
        {
            await conn.OpenAsync();
            (name, error) = await ResolveBotNameAsync(conn, req.Guid);
        }
        if (error != null)
            return Json(new { success = false, error });

        if (!_bridge.Connections.ContainsKey((int)req.Guid))
            return Json(new { success = false, error = "Bot is not online" });

        try
        {
            await _ra.SendCommandAsync($".bot delete {name}");
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = $"RA command failed: {ex.Message}" });
        }

        return await WaitForOfflineAsync(new[] { req.Guid })
            ? Json(new { success = true, name })
            : Json(new { success = false, error = $"{name} did not go offline within {OfflineWaitMs / 1000}s" });
    }

    // ==================== Kick all (disconnect the fleet, keep the roster) ====================
    // `.bot stop` is PlayerBotMgr::DeleteAll: it parks EVERY online entry at
    // PB_STATE_OFFLINE in a single command. Deliberately no `.bot reload` here —
    // the parked entries are exactly what AddAllBots() looks for, so "Load SuperUI
    // Bots" brings the whole fleet back with its AiBotAI intact.
    [HttpPost]
    public async Task<IActionResult> KickAllBots()
    {
        List<uint> guids;
        using (var conn = _db.Characters())
        {
            await conn.OpenAsync();
            guids = (await conn.QueryAsync<uint>("SELECT char_guid FROM playerbot")).ToList();
        }

        var online = guids.Where(g => _bridge.Connections.ContainsKey((int)g)).ToList();
        if (online.Count == 0)
            return Json(new { success = true, kicked = 0 });

        try
        {
            await _ra.SendCommandAsync(".bot stop");
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = $"RA '.bot stop' failed: {ex.Message}" });
        }

        var waitMs = Math.Clamp(online.Count * 300, OfflineWaitMs, MassOfflineWaitMaxMs);
        if (!await WaitForOfflineAsync(online, waitMs))
        {
            var stuck = online.Count(g => _bridge.Connections.ContainsKey((int)g));
            return Json(new
            {
                success = false,
                error = $"{online.Count - stuck} of {online.Count} kicked, but {stuck} still online after {waitMs / 1000}s",
                online = stuck
            });
        }

        return Json(new { success = true, kicked = online.Count });
    }

    // ==================== Delete (kick, then purge) ====================
    [HttpPost]
    public async Task<IActionResult> DeleteBot([FromBody] DeleteBotRequest req)
    {
        string? name, error;
        using (var conn = _db.Characters())
        {
            await conn.OpenAsync();
            (name, error) = await ResolveBotNameAsync(conn, req.Guid);
        }
        if (error != null)
            return Json(new { success = false, error });

        // The DB connection is deliberately closed across the kick + wait below:
        // WaitForOfflineAsync can block for OfflineWaitMs, and holding a pooled
        // connection idle for that long for an admin action is pure waste.
        if (_bridge.Connections.ContainsKey((int)req.Guid))
        {
            try
            {
                await _ra.SendCommandAsync($".kick {name}");
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = $"RA kick failed: {ex.Message}" });
            }

            if (!await WaitForOfflineAsync(new[] { req.Guid }))
                return Json(new
                {
                    success = false,
                    error = $"{name} did not go offline within {OfflineWaitMs / 1000}s — nothing was deleted"
                });
        }

        using (var conn = _db.Characters())
        {
            await conn.OpenAsync();
            await DeleteBotRowsAsync(conn, req.Guid);
        }
        _brain.EvictBot((int)req.Guid);
        _bridge.RemoveBotState((int)req.Guid);

        return Json(new { success = true });
    }

    // Mass delete deliberately does NOT loop `.kick` per bot. RaService serializes
    // commands, so a 300+ bot roster would mean 300+ sequential RA round-trips inside
    // one HTTP request. `.bot stop` (PlayerBotMgr::DeleteAll) parks every online entry
    // at PB_STATE_OFFLINE in a single command, and `.bot reload` afterwards re-runs
    // PlayerBotMgr::Load, whose first act is `DeleteAll(); m_bots.clear()` — so the
    // core's roster ends up resynced against the now-empty `playerbot` table instead
    // of holding 300 parked entries for characters that no longer exist.
    [HttpPost]
    public async Task<IActionResult> DeleteAllBots()
    {
        List<uint> guids;
        using (var conn = _db.Characters())
        {
            await conn.OpenAsync();
            guids = (await conn.QueryAsync<uint>("SELECT char_guid FROM playerbot")).ToList();
        }
        if (guids.Count == 0)
            return Json(new { success = true, deleted = 0 });

        var online = guids.Where(g => _bridge.Connections.ContainsKey((int)g)).ToList();
        if (online.Count > 0)
        {
            try
            {
                await _ra.SendCommandAsync(".bot stop");
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = $"RA '.bot stop' failed: {ex.Message}" });
            }

            // Each logout is a full Player teardown (plus a save when
            // PlayerBot.AllowSaving is on) spread across World::UpdateSessions
            // passes, so the whole fleet needs far longer than one bot. Scale with
            // the batch and cap it so a wedged core still fails rather than hangs.
            var massWaitMs = Math.Clamp(online.Count * 300, OfflineWaitMs, MassOfflineWaitMaxMs);
            if (!await WaitForOfflineAsync(online, massWaitMs))
            {
                var stuck = online.Count(g => _bridge.Connections.ContainsKey((int)g));
                return Json(new
                {
                    success = false,
                    error = $"{stuck} bot(s) did not go offline within {massWaitMs / 1000}s — nothing was deleted",
                    online = stuck
                });
            }
        }

        using (var conn = _db.Characters())
        {
            await conn.OpenAsync();
            foreach (var guid in guids)
            {
                await DeleteBotRowsAsync(conn, guid);
                _brain.EvictBot((int)guid);
                _bridge.RemoveBotState((int)guid);
            }
        }

        // Best-effort: the rows are already gone, so a failure here is cosmetic
        // (stale parked entries until the next restart), not a failed delete.
        try { await _ra.SendCommandAsync(".bot reload"); }
        catch { /* ignored — see above */ }

        return Json(new { success = true, deleted = guids.Count });
    }

    /// <summary>
    /// Confirms the guid is a registered bot (never a real player) and returns its
    /// character name, validated before it can reach a live console command.
    /// </summary>
    private static async Task<(string? Name, string? Error)> ResolveBotNameAsync(MySqlConnection conn, uint guid)
    {
        var isBot = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM playerbot WHERE char_guid = @Guid", new { Guid = guid });
        if (isBot == 0)
            return (null, "Not a registered bot");

        var name = await conn.ExecuteScalarAsync<string?>(
            "SELECT `name` FROM `characters` WHERE `guid` = @Guid", new { Guid = guid });
        if (string.IsNullOrWhiteSpace(name))
            return (null, "Bot has no character row — nothing to kick");
        if (!_botNameRe.IsMatch(name))
            return (null, $"Refusing to send an unexpected name to the console: '{name}'");

        return (name, null);
    }

    /// <summary>
    /// Polls the bridge until every guid's socket has dropped. The bridge connection
    /// is the authoritative offline signal: the core closes it from
    /// AiBotAI::Remove()/~AiBotAI when the Player is torn down.
    /// </summary>
    private async Task<bool> WaitForOfflineAsync(IReadOnlyCollection<uint> guids, int timeoutMs = OfflineWaitMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (guids.All(g => !_bridge.Connections.ContainsKey((int)g)))
                return true;
            await Task.Delay(OfflinePollMs);
        }
        return guids.All(g => !_bridge.Connections.ContainsKey((int)g));
    }

    /// <summary>
    /// Mirrors SuperUI-Core's Player::DeleteFromDB (charDelete_method 0, full purge) —
    /// see src/game/Objects/Player.cpp. Keep this list in sync if that ever changes.
    /// </summary>
    private static async Task DeleteBotRowsAsync(MySqlConnection conn, uint guid)
    {
        await using var tx = await conn.BeginTransactionAsync();
        var p = new { Guid = guid };

        await conn.ExecuteAsync("DELETE FROM `characters` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_action` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_aura` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_battleground_data` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_deleted_items` WHERE `player_guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_gifts` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_homebind` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_instance` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `group_instance` WHERE `leader_guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_inventory` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_queststatus` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_reputation` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_skills` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_forgotten_skills` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_spell` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_spell_cooldown` WHERE `guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `item_instance` WHERE `owner_guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_social` WHERE `guid` = @Guid OR `friend` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `mail` WHERE `receiver_guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `mail_items` WHERE `receiver_guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `character_pet` WHERE `owner_guid` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `guild_eventlog` WHERE `player_guid1` = @Guid OR `player_guid2` = @Guid", p, tx);
        await conn.ExecuteAsync("DELETE FROM `playerbot` WHERE `char_guid` = @Guid", p, tx);

        await tx.CommitAsync();
    }
}

public class DeleteBotRequest
{
    public uint Guid { get; set; }
}
