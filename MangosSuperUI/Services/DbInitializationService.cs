using MySqlConnector;
using Dapper;

namespace MangosSuperUI.Services;

/// <summary>
/// Runs at app startup: ensures vmangos_admin database and its tables exist.
/// Exposes per-database health status for the dashboard.
/// Singleton — registered in Program.cs, kicked off after app.Build().
/// </summary>
public class DbInitializationService
{
    private readonly IConfiguration _config;
    private readonly ILogger<DbInitializationService> _logger;

    // Tracks init result for dashboard display
    public bool AdminDbReady { get; private set; }
    public string? AdminDbError { get; private set; }
    public DateTime? InitializedAt { get; private set; }
    public int TablesCreated { get; private set; }
    public int TablesExisted { get; private set; }

    public DbInitializationService(IConfiguration config, ILogger<DbInitializationService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Called once at startup from Program.cs. Creates DB + tables if missing.
    /// Never throws — logs errors and sets AdminDbReady = false.
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("DbInitializationService: Starting vmangos_admin bootstrap...");

        try
        {
            // Step 1: Parse the Admin connection string and strip the Database part
            var adminConnStr = _config.GetConnectionString("Admin");
            if (string.IsNullOrEmpty(adminConnStr))
            {
                AdminDbError = "No 'Admin' connection string configured in appsettings.json or server-config.json.";
                _logger.LogError(AdminDbError);
                return;
            }

            var builder = new MySqlConnectionStringBuilder(adminConnStr);
            var dbName = builder.Database; // "vmangos_admin"
            builder.Database = "";         // Connect without specifying a DB

            // Step 2: Create the database if it doesn't exist
            using (var bootstrapConn = new MySqlConnection(builder.ConnectionString))
            {
                await bootstrapConn.OpenAsync();
                _logger.LogInformation("DbInitializationService: Connected to MariaDB server. Ensuring database '{Db}' exists...", dbName);

                await bootstrapConn.ExecuteAsync(
                    $"CREATE DATABASE IF NOT EXISTS `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci");
            }

            // Step 3: Create tables if they don't exist
            using (var conn = new MySqlConnection(adminConnStr))
            {
                await conn.OpenAsync();

                var created = 0;
                var existed = 0;

                // --- audit_log ---
                if (await TableExistsAsync(conn, dbName, "audit_log"))
                {
                    existed++;
                    _logger.LogDebug("DbInitializationService: audit_log already exists");
                }
                else
                {
                    await conn.ExecuteAsync(Sql_AuditLog);
                    await conn.ExecuteAsync(Sql_AuditLogIndexes);
                    created++;
                    _logger.LogInformation("DbInitializationService: Created audit_log table with indexes");
                }

                // --- config_history ---
                if (await TableExistsAsync(conn, dbName, "config_history"))
                {
                    existed++;
                    _logger.LogDebug("DbInitializationService: config_history already exists");
                }
                else
                {
                    await conn.ExecuteAsync(Sql_ConfigHistory);
                    await conn.ExecuteAsync(Sql_ConfigHistoryIndexes);
                    created++;
                    _logger.LogInformation("DbInitializationService: Created config_history table with indexes");
                }

                // --- scheduled_actions ---
                if (await TableExistsAsync(conn, dbName, "scheduled_actions"))
                {
                    existed++;
                    _logger.LogDebug("DbInitializationService: scheduled_actions already exists");
                }
                else
                {
                    await conn.ExecuteAsync(Sql_ScheduledActions);
                    await conn.ExecuteAsync(Sql_ScheduledActionsIndexes);
                    created++;
                    _logger.LogInformation("DbInitializationService: Created scheduled_actions table with indexes");
                }

                // --- og_baseline_meta ---
                if (await TableExistsAsync(conn, dbName, "og_baseline_meta"))
                {
                    existed++;
                    _logger.LogDebug("DbInitializationService: og_baseline_meta already exists");
                }
                else
                {
                    await conn.ExecuteAsync(Sql_OgBaselineMeta);
                    created++;
                    _logger.LogInformation("DbInitializationService: Created og_baseline_meta table");
                }

                TablesCreated = created;
                TablesExisted = existed;

                // --- column migrations ---
                // The CREATE statements above are skipped whenever a table already
                // exists, so columns added after an install shipped can only reach
                // existing databases through here.
                await MigrateColumnsAsync(conn, dbName);
            }

            AdminDbReady = true;
            InitializedAt = DateTime.UtcNow;
            _logger.LogInformation(
                "DbInitializationService: Bootstrap complete. Created={Created}, AlreadyExisted={Existed}",
                TablesCreated, TablesExisted);
        }
        catch (Exception ex)
        {
            AdminDbReady = false;
            AdminDbError = ex.Message;
            _logger.LogError(ex, "DbInitializationService: Failed to bootstrap vmangos_admin");
        }
    }

    /// <summary>
    /// Checks connectivity to each configured database. Called by HomeController.DbHealth().
    /// </summary>
    public async Task<DbHealthReport> CheckHealthAsync()
    {
        var report = new DbHealthReport
        {
            AdminInitialized = AdminDbReady,
            AdminInitError = AdminDbError,
            InitializedAt = InitializedAt,
            TablesCreated = TablesCreated,
            TablesExisted = TablesExisted
        };

        report.Databases["mangos"] = await PingDatabaseAsync("Mangos");
        report.Databases["characters"] = await PingDatabaseAsync("Characters");
        report.Databases["realmd"] = await PingDatabaseAsync("Realmd");
        report.Databases["logs"] = await PingDatabaseAsync("Logs");
        report.Databases["vmangos_admin"] = await PingDatabaseAsync("Admin");

        return report;
    }

    private async Task<DbPingResult> PingDatabaseAsync(string connStringName)
    {
        var result = new DbPingResult();
        var connStr = _config.GetConnectionString(connStringName);

        if (string.IsNullOrEmpty(connStr))
        {
            result.Reachable = false;
            result.Error = "Connection string not configured";
            return result;
        }

        try
        {
            using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();
            await conn.ExecuteScalarAsync<int>("SELECT 1");
            result.Reachable = true;
        }
        catch (Exception ex)
        {
            result.Reachable = false;
            result.Error = ex.Message;
        }

        return result;
    }

    private static async Task<bool> TableExistsAsync(MySqlConnection conn, string database, string table)
    {
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = @database AND TABLE_NAME = @table",
            new { database, table });
        return count > 0;
    }

    private static async Task<bool> ColumnExistsAsync(MySqlConnection conn, string database, string table, string column)
    {
        var count = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM information_schema.COLUMNS
              WHERE TABLE_SCHEMA = @database AND TABLE_NAME = @table AND COLUMN_NAME = @column",
            new { database, table, column });
        return count > 0;
    }

    private static async Task<bool> IndexExistsAsync(MySqlConnection conn, string database, string table, string index)
    {
        var count = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM information_schema.STATISTICS
              WHERE TABLE_SCHEMA = @database AND TABLE_NAME = @table AND INDEX_NAME = @index",
            new { database, table, index });
        return count > 0;
    }

    /// <summary>
    /// Adds columns and indexes introduced after a table's original CREATE. Safe to run on
    /// every boot: each change is guarded by an information_schema check, so this is a no-op
    /// once an install is current. A failure here is logged and swallowed — a missing optional
    /// column degrades the change graph, it does not justify refusing to start the panel.
    /// </summary>
    private async Task MigrateColumnsAsync(MySqlConnection conn, string dbName)
    {
        // audit_log: change-graph columns. batch_id groups the rows a single tool run
        // produced; revert_kind tells the graph HOW a row can be undone rather than the
        // bare is_reversible yes/no; reverted_at marks rows that have already been undone
        // so they render struck-through instead of disappearing from history.
        var auditColumns = new (string Column, string Ddl)[]
        {
            ("batch_id",       "ADD COLUMN batch_id VARCHAR(32) NULL"),
            ("batch_label",    "ADD COLUMN batch_label VARCHAR(190) NULL"),
            ("revert_kind",    "ADD COLUMN revert_kind VARCHAR(24) NOT NULL DEFAULT 'none'"),
            ("reverted_at",    "ADD COLUMN reverted_at DATETIME(3) NULL"),
            ("reverted_by_id", "ADD COLUMN reverted_by_id BIGINT UNSIGNED NULL"),
        };

        var auditIndexes = new (string Index, string Ddl)[]
        {
            ("idx_batch",    "CREATE INDEX idx_batch ON audit_log (batch_id)"),
            ("idx_reverted", "CREATE INDEX idx_reverted ON audit_log (reverted_at)"),
        };

        try
        {
            if (!await TableExistsAsync(conn, dbName, "audit_log")) return;

            var added = 0;
            foreach (var (column, ddl) in auditColumns)
            {
                if (await ColumnExistsAsync(conn, dbName, "audit_log", column)) continue;
                await conn.ExecuteAsync($"ALTER TABLE audit_log {ddl}");
                added++;
                _logger.LogInformation("DbInitializationService: Added audit_log.{Column}", column);
            }

            foreach (var (index, ddl) in auditIndexes)
            {
                if (await IndexExistsAsync(conn, dbName, "audit_log", index)) continue;
                await conn.ExecuteAsync(ddl);
                _logger.LogInformation("DbInitializationService: Added audit_log index {Index}", index);
            }

            // Existing history predates revert_kind and would show up in the graph as
            // permanently un-undoable. Classify it from the action names that were already
            // being written, so the graph is useful against history from day one.
            //
            // Version-gated rather than run-once: the classifier gets corrected as its
            // blind spots surface, and a fix is worthless if it never reaches the rows that
            // were misclassified by the previous version. Re-running is safe because the
            // classifier is deterministic and agrees with every revert_kind the controllers
            // set explicitly at insert time.
            await conn.ExecuteAsync(Sql_SchemaMeta);

            var storedVersion = await conn.ExecuteScalarAsync<int?>(
                "SELECT CAST(meta_value AS SIGNED) FROM schema_meta WHERE meta_key = 'revert_classifier'");

            if (storedVersion is null || storedVersion < RevertClassifierVersion)
            {
                var classified = await BackfillRevertKindsAsync(conn);
                await conn.ExecuteAsync(
                    @"INSERT INTO schema_meta (meta_key, meta_value) VALUES ('revert_classifier', @v)
                      ON DUPLICATE KEY UPDATE meta_value = @v, updated_at = CURRENT_TIMESTAMP(3)",
                    new { v = RevertClassifierVersion.ToString() });

                _logger.LogInformation(
                    "DbInitializationService: Classified revert_kind on {Count} audit rows (classifier v{From} → v{To})",
                    classified, storedVersion?.ToString() ?? "none", RevertClassifierVersion);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DbInitializationService: audit_log column migration failed — " +
                                 "the change graph will run with reduced capability");
        }
    }

    /// <summary>
    /// Bump when the CASE below changes so corrected rules reach rows the previous
    /// version misclassified.
    /// v1 → v2: deletions are never revertable, and custom-range entries (spells 40000-49999,
    ///          items/objects 900000+) are 'delete_custom' rather than 'baseline' — they have
    ///          no og_* row, so the baseline restore they advertised could never succeed.
    /// </summary>
    private const int RevertClassifierVersion = 2;

    /// <summary>
    /// Classifies audit rows into revert strategies. Deterministic and idempotent: it runs
    /// over every successful row, not just unclassified ones, so a corrected classifier can
    /// repair earlier mistakes. Every value a controller sets explicitly at insert time
    /// matches what this produces, so re-running never fights the write path.
    /// </summary>
    private static async Task<int> BackfillRevertKindsAsync(MySqlConnection conn)
    {
        // One UPDATE with an ordered CASE rather than a sequence of UPDATEs. Sequential
        // rules relied on matched rows leaving the revert_kind='none' pool, which makes it
        // impossible to express "deletions are NEVER revertable" — assigning 'none' does
        // not remove a row, so a later rule would still claim it. CASE makes precedence
        // explicit and evaluates each row exactly once.
        //
        // Custom-content entry ranges matter here. A cloned spell lives at 40000-49999 and
        // custom items/objects at 900000+; none of them have an og_* row, so classifying
        // them 'baseline' would offer a restore that cannot possibly work.
        const string customSpell = "(target_id BETWEEN 40000 AND 49999)";
        const string customItem = "(target_id >= 900000)";

        var sql = $@"
            UPDATE audit_log SET revert_kind = CASE

                -- Deletions removed something and captured no before-state. There is
                -- nothing to put back, so they are never revertable.
                WHEN action LIKE '%\_delete' AND (state_before IS NULL OR CHAR_LENGTH(state_before) <= 2)
                    THEN 'none'

                -- Tools that own a rollback path through their own registry tables.
                WHEN action LIKE 'lootifier\_%' OR action LIKE 'quest\_lootifier\_%'
                     OR action LIKE 'crafting\_lootifier\_%'
                    THEN 'registry'

                -- Creations: undoing means deleting what was made.
                WHEN action IN ('spell_clone','spell_completer_run','patch_generate')
                     OR target_type IN ('item_custom','gameobject_custom')
                     OR (target_type = 'spell_template' AND {customSpell})
                     OR (target_type IN ('item_template','item_base_game') AND {customItem})
                     OR (target_type IN ('gameobject_template','gameobject_base_game') AND {customItem})
                    THEN 'delete_custom'

                -- Edits to base-game rows go back via the og_* snapshot tables. Custom
                -- ranges are already handled above, so anything reaching here has a baseline.
                WHEN target_type IN ('item_base_game','item_template','spell_template',
                                     'gameobject_base_game','gameobject_template',
                                     'creature_loot','loot_row','loot_table','loot_tables')
                    THEN 'baseline'

                -- Whatever is left but captured its own before-state can be re-applied.
                WHEN state_before IS NOT NULL AND CHAR_LENGTH(state_before) > 2
                    THEN 'state_before'

                ELSE 'none'
            END
            WHERE success = 1";

        return await conn.ExecuteAsync(sql);
    }

    // ==================== DDL Statements ====================
    //
    // NOTE: state/changes/action_data columns use LONGTEXT (not the native JSON type)
    // for cross-engine portability. MySQL did not add the JSON type until 5.7.8, so a
    // JSON column declaration is a parse error on MySQL 5.6. These columns only ever
    // store JSON text written/read as strings via Dapper (no server-side JSON_EXTRACT/->>),
    // so LONGTEXT is behaviorally identical on MySQL 5.6/5.7/8.0 and MariaDB. Existing
    // installs are unaffected: the C# TableExistsAsync guard skips CREATE when the table
    // already exists, so their original column types are left untouched.

    private const string Sql_AuditLog = @"
        CREATE TABLE audit_log (
            id              BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            timestamp       DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
            batch_id        VARCHAR(32)     NULL,
            batch_label     VARCHAR(190)    NULL,
            operator        VARCHAR(64)     NOT NULL DEFAULT 'system',
            operator_ip     VARCHAR(45)     NULL,
            category        VARCHAR(32)     NOT NULL,
            action          VARCHAR(64)     NOT NULL,
            target_type     VARCHAR(32)     NULL,
            target_name     VARCHAR(128)    NULL,
            target_id       INT UNSIGNED    NULL,
            ra_command      TEXT            NULL,
            ra_response     TEXT            NULL,
            state_before    LONGTEXT        NULL,
            state_after     LONGTEXT        NULL,
            is_reversible   TINYINT(1)      NOT NULL DEFAULT 0,
            revert_kind     VARCHAR(24)     NOT NULL DEFAULT 'none',
            reverted_at     DATETIME(3)     NULL,
            reverted_by_id  BIGINT UNSIGNED NULL,
            reverses_id     BIGINT UNSIGNED NULL,
            success         TINYINT(1)      NOT NULL DEFAULT 1,
            notes           TEXT            NULL
        ) ENGINE=InnoDB;";

    private const string Sql_AuditLogIndexes = @"
        CREATE INDEX idx_timestamp   ON audit_log (timestamp);
        CREATE INDEX idx_category    ON audit_log (category);
        CREATE INDEX idx_action      ON audit_log (action);
        CREATE INDEX idx_target      ON audit_log (target_type, target_name);
        CREATE INDEX idx_operator    ON audit_log (operator);
        CREATE INDEX idx_reversible  ON audit_log (is_reversible);
        CREATE INDEX idx_batch       ON audit_log (batch_id);
        CREATE INDEX idx_reverted    ON audit_log (reverted_at);";

    private const string Sql_ConfigHistory = @"
        CREATE TABLE config_history (
            id              BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            timestamp       DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
            operator        VARCHAR(64)     NOT NULL DEFAULT 'system',
            config_json     MEDIUMTEXT      NOT NULL,
            changes         LONGTEXT        NULL,
            notes           TEXT            NULL
        ) ENGINE=InnoDB;";

    private const string Sql_ConfigHistoryIndexes = @"
        CREATE INDEX idx_timestamp ON config_history (timestamp);";

    private const string Sql_ScheduledActions = @"
        CREATE TABLE scheduled_actions (
            id              BIGINT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            created_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
            execute_at      DATETIME(3)     NOT NULL,
            executed_at     DATETIME(3)     NULL,
            operator        VARCHAR(64)     NOT NULL DEFAULT 'system',
            action_type     VARCHAR(64)     NOT NULL,
            action_data     LONGTEXT        NOT NULL,
            status          VARCHAR(16)     NOT NULL DEFAULT 'pending',
            result          TEXT            NULL,
            audit_log_id    BIGINT UNSIGNED NULL
        ) ENGINE=InnoDB;";

    private const string Sql_ScheduledActionsIndexes = @"
        CREATE INDEX idx_execute_at ON scheduled_actions (execute_at);
        CREATE INDEX idx_status     ON scheduled_actions (status);";

    // Tiny key/value table for migration bookkeeping — lets data migrations be versioned
    // and re-run when their logic is corrected, instead of firing once and never again.
    private const string Sql_SchemaMeta = @"
        CREATE TABLE IF NOT EXISTS schema_meta (
            meta_key        VARCHAR(64)     NOT NULL PRIMARY KEY,
            meta_value      VARCHAR(255)    NOT NULL,
            updated_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3)
        ) ENGINE=InnoDB;";

    private const string Sql_OgBaselineMeta = @"
        CREATE TABLE IF NOT EXISTS og_baseline_meta (
            id              INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,
            table_name      VARCHAR(64)     NOT NULL,
            source_table    VARCHAR(64)     NOT NULL,
            source_database VARCHAR(64)     NOT NULL DEFAULT 'mangos',
            row_count       INT UNSIGNED    NOT NULL DEFAULT 0,
            created_at      DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
            UNIQUE KEY idx_table (table_name)
        ) ENGINE=InnoDB;";
}

// ==================== Health DTOs ====================

public class DbHealthReport
{
    public bool AdminInitialized { get; set; }
    public string? AdminInitError { get; set; }
    public DateTime? InitializedAt { get; set; }
    public int TablesCreated { get; set; }
    public int TablesExisted { get; set; }
    public Dictionary<string, DbPingResult> Databases { get; set; } = new();
}

public class DbPingResult
{
    public bool Reachable { get; set; }
    public string? Error { get; set; }
}