using Microsoft.AspNetCore.Mvc;
using Dapper;
using MySqlConnector;

namespace MangosSuperUI.Controllers;

// ============================================================================
//  Bot Deletion — single + mass
// ============================================================================
// RA's `.character erase` (the core's real deletion command) is unusable until
// the remoteAccess account gotcha is fixed. This purges the same tables
// SuperUI-Core's own Player::DeleteFromDB (charDelete_method 0) deletes, run
// directly against the characters DB.
//
// Safety: a guid must exist in `playerbot` before anything is touched (never
// deletes a real player), and a bot must be offline (no open bridge socket)
// before its rows are touched — deleting DB rows out from under a live Player
// object risks the core's periodic autosave silently re-inserting a bare
// `characters` row right after.
//
// `.bot delete <name>` over RA (PlayerBotMgr::DeleteBot -> OnBotLogout) was
// tried as an auto-kick step here and reliably segfaults mangosd
// (status=11/SEGV, confirmed 2026-08-25 — killed the whole world server, not
// just the targeted bot). Do not call it from here again until that crash is
// root-caused and fixed in SuperUI-Core. Both single and mass delete just
// block if the target(s) are still online.
public partial class BotsController
{
    [HttpGet]
    public async Task<IActionResult> RosterSummary()
    {
        using var conn = _db.Characters();
        var guids = (await conn.QueryAsync<uint>("SELECT char_guid FROM playerbot")).ToList();
        var online = guids.Count(g => _bridge.Connections.ContainsKey((int)g));
        return Json(new { total = guids.Count, online });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteBot([FromBody] DeleteBotRequest req)
    {
        using var conn = _db.Characters();
        await conn.OpenAsync();

        var isBot = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM playerbot WHERE char_guid = @Guid", new { req.Guid });
        if (isBot == 0)
            return Json(new { success = false, error = "Not a registered bot" });

        if (_bridge.Connections.ContainsKey((int)req.Guid))
            return Json(new { success = false, error = "Bot is still online — wait for it to disconnect first" });

        await DeleteBotRowsAsync(conn, req.Guid);
        _brain.EvictBot((int)req.Guid);

        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAllBots()
    {
        using var conn = _db.Characters();
        await conn.OpenAsync();

        var guids = (await conn.QueryAsync<uint>("SELECT char_guid FROM playerbot")).ToList();
        if (guids.Count == 0)
            return Json(new { success = true, deleted = 0 });

        var onlineCount = guids.Count(g => _bridge.Connections.ContainsKey((int)g));
        if (onlineCount > 0)
        {
            return Json(new
            {
                success = false,
                error = $"{onlineCount} of {guids.Count} bot(s) still online — wait for them to disconnect first",
                online = onlineCount
            });
        }

        foreach (var guid in guids)
        {
            await DeleteBotRowsAsync(conn, guid);
            _brain.EvictBot((int)guid);
        }

        return Json(new { success = true, deleted = guids.Count });
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
