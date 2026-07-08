using Dapper;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using System.Text.Json;
using MangosSuperUI.BotLogic.Chat.Core;
using Microsoft.Extensions.Options;

namespace MangosSuperUI.BotLogic.Data;

/// <summary>
/// Creates the BotLogic tables in the vmangos_admin MariaDB database.
/// Called once at startup by BotBrainService.
/// Uses CREATE TABLE IF NOT EXISTS — safe to run repeatedly.
/// </summary>
public class BotBrainDbInit
{
    private readonly ConnectionFactory _db;
    private readonly ILogger<BotBrainDbInit> _logger;
    private readonly BotChatSettings _botChat;

    public BotBrainDbInit(ConnectionFactory db, ILogger<BotBrainDbInit> logger,
        IOptions<BotChatSettings> botChat)
    {
        _db = db;
        _logger = logger;
        _botChat = botChat.Value;
    }

    public async Task InitializeAsync()
    {
        try
        {
            using var conn = _db.Admin();

            // --- bot_personality ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS bot_personality (
                    bot_guid            INT NOT NULL PRIMARY KEY,
                    patience            FLOAT NOT NULL DEFAULT 0.5,
                    greed               FLOAT NOT NULL DEFAULT 0.5,
                    curiosity           FLOAT NOT NULL DEFAULT 0.5,
                    sociability         FLOAT NOT NULL DEFAULT 0.5,
                    aggression          FLOAT NOT NULL DEFAULT 0.5,
                    efficiency          FLOAT NOT NULL DEFAULT 0.5,
                    cautiousness        FLOAT NOT NULL DEFAULT 0.5,
                    indecisiveness      FLOAT NOT NULL DEFAULT 0.5,
                    spontaneity         FLOAT NOT NULL DEFAULT 0.5,
                    chat_style          VARCHAR(32) NOT NULL DEFAULT 'casual',
                    temperament         VARCHAR(32) NOT NULL DEFAULT 'friendly',
                    quirk_ids           VARCHAR(256) DEFAULT NULL,
                    created_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // --- bot_activity_log ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS bot_activity_log (
                    id                  INT AUTO_INCREMENT PRIMARY KEY,
                    bot_guid            INT NOT NULL,
                    activity_type       VARCHAR(32) NOT NULL,
                    started_at          DATETIME NOT NULL,
                    ended_at            DATETIME DEFAULT NULL,
                    context_tag         VARCHAR(128) DEFAULT NULL,
                    decision_reason     VARCHAR(256) DEFAULT NULL,
                    weight_snapshot     TEXT DEFAULT NULL,
                    roll_value          FLOAT DEFAULT NULL,
                    INDEX idx_bot_activity_log_guid (bot_guid),
                    INDEX idx_bot_activity_log_started (started_at)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // --- bot_registry ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS bot_registry (
                    bot_guid            INT NOT NULL PRIMARY KEY,
                    bot_name            VARCHAR(64) NOT NULL,
                    race                TINYINT NOT NULL,
                    class_id            TINYINT NOT NULL,
                    level               TINYINT NOT NULL DEFAULT 1,
                    faction             VARCHAR(16) NOT NULL DEFAULT '',
                    spawn_status        VARCHAR(16) NOT NULL DEFAULT 'inactive',
                    current_zone_id     INT DEFAULT NULL,
                    current_activity    VARCHAR(32) DEFAULT NULL,
                    last_seen           DATETIME DEFAULT NULL,
                    created_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_bot_registry_status (spawn_status),
                    INDEX idx_bot_registry_zone (current_zone_id)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // --- bot_wallet (shadow economy) ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS bot_wallet (
                    bot_guid            INT NOT NULL PRIMARY KEY,
                    copper              BIGINT NOT NULL DEFAULT 0,
                    updated_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // --- bot_inventory (shadow inventory persistence) ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS bot_inventory (
                    id                  INT AUTO_INCREMENT PRIMARY KEY,
                    bot_guid            INT NOT NULL,
                    item_id             INT NOT NULL,
                    count               INT NOT NULL DEFAULT 1,
                    quality             TINYINT NOT NULL DEFAULT 0,
                    sell_price          INT NOT NULL DEFAULT 0,
                    source              VARCHAR(32) NOT NULL DEFAULT 'loot',
                    source_creature     INT DEFAULT 0,
                    acquired_at         DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_bot_inv_guid (bot_guid)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // --- bot_groups (Session 31 — grouping system) ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS bot_groups (
                    group_id        INT NOT NULL PRIMARY KEY,
                    leader_guid     INT NOT NULL,
                    member_guids    VARCHAR(256) NOT NULL,
                    leader_type     TINYINT NOT NULL DEFAULT 0,
                    formed_at       DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_bot_groups_leader (leader_guid)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // --- bot_settings (Session 31 — server-level bot config) ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS bot_settings (
                    setting_key     VARCHAR(64) NOT NULL PRIMARY KEY,
                    setting_value   VARCHAR(256) NOT NULL DEFAULT '',
                    updated_at      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // ==================== CHAT_ARCHITECTURE §4 — the AiBot social layer ====================
            // All chat_* / bot_persona tables live HERE, in vmangos_admin, alongside
            // bot_personality / bot_settings (locked 2026-07-07). DDL is §4 verbatim.

            // --- §6.3 chat_voice: ~300 offline-generated persona cards (pre-assignment pool) ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS chat_voice (
                  id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
                  card_json   MEDIUMTEXT   NOT NULL,
                  era_pack_id INT UNSIGNED NULL,
                  created_utc DATETIME     NOT NULL,
                  retired     TINYINT(1)   NOT NULL DEFAULT 0,
                  PRIMARY KEY (id)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // --- §6 bot_persona: assigned, materialized persona per bot (extends bot_personality) ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS bot_persona (
                  guid          INT UNSIGNED NOT NULL,
                  voice_id      INT UNSIGNED NULL,
                  card_json     MEDIUMTEXT   NOT NULL,
                  mood_valence  FLOAT        NOT NULL DEFAULT 0,
                  mood_energy   FLOAT        NOT NULL DEFAULT 0,
                  situation     VARCHAR(300) NOT NULL DEFAULT '',
                  narrative     TEXT         NOT NULL,
                  updated_utc   DATETIME     NOT NULL,
                  PRIMARY KEY (guid)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // --- §7.2 chat_log: Tier 1 verbatim interaction log ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS chat_log (
                  id               BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
                  utc              DATETIME     NOT NULL,
                  bot_guid         INT UNSIGNED NOT NULL,
                  counterpart_name VARCHAR(48)  NOT NULL,
                  counterpart_guid INT UNSIGNED NOT NULL DEFAULT 0,
                  direction        ENUM('in','out') NOT NULL,
                  kind             ENUM('say','whisper','channel','party','yell') NOT NULL,
                  channel_name     VARCHAR(64)  NOT NULL DEFAULT '',
                  zone_id          INT UNSIGNED NOT NULL DEFAULT 0,
                  message          TEXT         NOT NULL,
                  salience         TINYINT      NOT NULL DEFAULT 1,
                  compacted        TINYINT(1)   NOT NULL DEFAULT 0,
                  PRIMARY KEY (id),
                  KEY idx_bot_utc (bot_guid, utc),
                  KEY idx_bot_cp  (bot_guid, counterpart_name, compacted)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // --- §7.3 chat_relationship: Tier 2 per-counterpart summaries ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS chat_relationship (
                  bot_guid          INT UNSIGNED NOT NULL,
                  counterpart_name  VARCHAR(48)  NOT NULL,
                  counterpart_guid  INT UNSIGNED NOT NULL DEFAULT 0,
                  summary           TEXT         NOT NULL,
                  strength          FLOAT        NOT NULL DEFAULT 0,
                  interact_count    INT UNSIGNED NOT NULL DEFAULT 0,
                  first_interact_utc DATETIME    NOT NULL,
                  last_interact_utc DATETIME     NOT NULL,
                  PRIMARY KEY (bot_guid, counterpart_name),
                  KEY idx_strength (bot_guid, strength)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // --- §14 chat_settings: every tunable. scope: 'global' or 'zone:<zoneId>' (D10) ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS chat_settings (
                  scope   VARCHAR(24)  NOT NULL,
                  name    VARCHAR(64)  NOT NULL,
                  value   VARCHAR(256) NOT NULL,
                  PRIMARY KEY (scope, name)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // --- §14.2 chat_preset: named bundles applied over defaults ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS chat_preset (
                  name          VARCHAR(48) NOT NULL,
                  settings_json MEDIUMTEXT  NOT NULL,
                  builtin       TINYINT(1)  NOT NULL DEFAULT 0,
                  PRIMARY KEY (name)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // --- §13 chat_era_pack: uploaded source + compiled digest ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS chat_era_pack (
                  id         INT UNSIGNED NOT NULL AUTO_INCREMENT,
                  name       VARCHAR(64)  NOT NULL,
                  source_md  MEDIUMTEXT   NOT NULL,
                  digest     TEXT         NOT NULL,
                  banned_json TEXT        NOT NULL,
                  active     TINYINT(1)   NOT NULL DEFAULT 0,
                  uploaded_utc DATETIME   NOT NULL,
                  PRIMARY KEY (id),
                  UNIQUE KEY uq_name (name)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // --- §12 chat_inference_profile: hot-swap from SuperUI ---
            await conn.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS chat_inference_profile (
                  id               INT UNSIGNED NOT NULL AUTO_INCREMENT,
                  name             VARCHAR(48)  NOT NULL,
                  endpoint_url     VARCHAR(256) NOT NULL,
                  api_flavor       VARCHAR(16)  NOT NULL DEFAULT 'ollama',
                  model_reactive   VARCHAR(96)  NOT NULL,
                  model_ambient    VARCHAR(96)  NOT NULL,
                  model_batch      VARCHAR(96)  NOT NULL DEFAULT '',
                  ctx_budget_tokens INT         NOT NULL DEFAULT 3000,
                  concurrency      INT          NOT NULL DEFAULT 2,
                  reactive_reserved INT         NOT NULL DEFAULT 1,
                  ambient_rate_mult FLOAT       NOT NULL DEFAULT 1.0,
                  active           TINYINT(1)   NOT NULL DEFAULT 0,
                  PRIMARY KEY (id),
                  UNIQUE KEY uq_name (name)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

            // api_flavor was added after the initial C1 schema — MariaDB's IF NOT EXISTS
            // makes this a no-op on fresh installs and a painless upgrade on old ones.
            await conn.ExecuteAsync(@"
                ALTER TABLE chat_inference_profile
                ADD COLUMN IF NOT EXISTS api_flavor VARCHAR(16) NOT NULL DEFAULT 'ollama' AFTER endpoint_url");

            await SeedChatDefaultsAsync(conn);

            _logger.LogInformation("BotBrainDbInit: all tables verified/created");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BotBrainDbInit: failed to initialize tables");
        }
    }

    /// <summary>
    /// CHAT_ARCHITECTURE seeds, all idempotent (INSERT IGNORE):
    ///  - every §14.4 default into chat_settings global scope (from ChatSettingsRegistry —
    ///    the single authoritative copy of the table),
    ///  - the five §14.2 built-in presets (builtin=1),
    ///  - the two §4-note inference profiles: a6000 (active) and travel-8gb.
    /// Existing rows are never overwritten — operator edits survive every boot.
    /// </summary>
    private async Task SeedChatDefaultsAsync(MySqlConnector.MySqlConnection conn)
    {
        // §14.4 — seed ALL of these
        var settingRows = ChatSettingsRegistry.All
            .Select(d => new { scope = "global", name = d.Key, value = d.Default });
        var seeded = await conn.ExecuteAsync(
            "INSERT IGNORE INTO chat_settings (scope, name, value) VALUES (@scope, @name, @value)",
            settingRows);

        // §14.2 — built-in presets
        foreach (var (name, pairs) in ChatPresets.BuiltIn)
        {
            await conn.ExecuteAsync(
                "INSERT IGNORE INTO chat_preset (name, settings_json, builtin) VALUES (@name, @json, 1)",
                new { name, json = JsonSerializer.Serialize(pairs) });
        }

        // §4 note — two inference profiles. Model tags are settings, not code: both seed the
        // proven qwen3:4b tag; larger tags are an operator choice on the Capacity tab.
        // a6000 seeds active=1; travel-8gb's endpoint is a placeholder Nico edits at first use.
        // Inference profiles come from the "BotChat" CONFIG SECTION, never from code —
        // the GitHub appsettings.json ships an empty list; operator endpoints live in
        // server-config.json (same split as SpellCreator). INSERT IGNORE: config seeds a
        // profile once; the Capacity page owns it from then on. No profiles configured →
        // chat generation stays off until one is created on the Capacity page.
        int profilesSeeded = 0;
        foreach (var prof in _botChat.InferenceProfiles.Where(x =>
                     !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.EndpointUrl)))
        {
            profilesSeeded += await conn.ExecuteAsync(@"
                INSERT IGNORE INTO chat_inference_profile
                  (name, endpoint_url, api_flavor, model_reactive, model_ambient, model_batch,
                   ctx_budget_tokens, concurrency, reactive_reserved, ambient_rate_mult, active)
                VALUES (@Name, @EndpointUrl, @ApiFlavor, @ModelReactive, @ModelAmbient, @ModelBatch,
                        @CtxBudgetTokens, @Concurrency, @ReactiveReserved, @AmbientRateMult, @Active)",
                new
                {
                    prof.Name,
                    prof.EndpointUrl,
                    ApiFlavor = (prof.ApiFlavor ?? "ollama").Trim().ToLowerInvariant(),
                    prof.ModelReactive,
                    prof.ModelAmbient,
                    prof.ModelBatch,
                    prof.CtxBudgetTokens,
                    prof.Concurrency,
                    prof.ReactiveReserved,
                    prof.AmbientRateMult,
                    Active = prof.Active ? 1 : 0
                });
        }
        if (_botChat.InferenceProfiles.Count == 0)
            _logger.LogInformation("BotBrainDbInit: no BotChat:InferenceProfiles configured — create one on the Chat Capacity page to enable generation");

        _logger.LogInformation(
            "BotBrainDbInit: chat layer seeded ({Seeded} new settings, {Presets} presets, {Profiles} new profiles)",
            seeded, ChatPresets.BuiltIn.Count, profilesSeeded);
    }
}