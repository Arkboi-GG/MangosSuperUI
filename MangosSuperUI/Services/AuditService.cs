using MangosSuperUI.Models;
using Dapper;

namespace MangosSuperUI.Services;

/// <summary>
/// Records all MangosSuperUI panel actions to the vmangos_admin.audit_log table.
/// Singleton — inject and call LogAsync() from any controller or hub.
/// </summary>
public class AuditService
{
    private readonly ConnectionFactory _db;
    private readonly ILogger<AuditService> _logger;
    private readonly StateCaptureService _stateCapture;

    public AuditService(ConnectionFactory db, StateCaptureService stateCapture, ILogger<AuditService> logger)
    {
        _db = db;
        _stateCapture = stateCapture;
        _logger = logger;
    }

    /// <summary>
    /// Full lifecycle: capture state before → execute RA command → capture state after → log everything.
    /// Use this from controllers and hubs instead of manually calling LogCommandAsync().
    /// Returns (response, success) so callers can forward the result.
    /// </summary>
    public async Task<(string response, bool success)> ExecuteAndLogAsync(
        RaService raService,
        string command,
        string? operatorIp = null,
        string? operator_ = null,
        string? notes = null)
    {
        // Step 1: Capture state BEFORE the command
        CaptureResult? capture = null;
        try
        {
            capture = await _stateCapture.CaptureBeforeAsync(command);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pre-capture failed for: {Command}", command);
        }

        // Step 2: Execute the RA command
        string response;
        bool success;
        try
        {
            response = await raService.SendCommandAsync(command);
            success = true;
        }
        catch (Exception ex)
        {
            response = ex.Message;
            success = false;
            notes = (notes != null ? notes + " | " : "") + "Exception: " + ex.GetType().Name;
        }

        // Step 3: Capture state AFTER the command (only if it succeeded and we have a before snapshot)
        string? stateAfter = null;
        if (success && capture != null)
        {
            try
            {
                // Small delay to let VMaNGOS process the change
                await Task.Delay(200);
                stateAfter = await _stateCapture.CaptureAfterAsync(capture);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Post-capture failed for: {Command}", command);
            }
        }

        // Step 4: Log to audit trail with full state
        await LogCommandAsync(
            command, response, success,
            targetType: capture?.TargetType,
            targetName: capture?.TargetName,
            targetId: capture?.TargetId,
            stateBefore: capture?.StateBefore,
            stateAfter: stateAfter,
            isReversible: capture?.IsReversible ?? false,
            operator_: operator_,
            operatorIp: operatorIp,
            notes: notes);

        return (response, success);
    }

    /// <summary>
    /// Log an action to the audit trail.
    /// </summary>
    public async Task<long> LogAsync(AuditEntry entry)
    {
        try
        {
            // A caller inside a batch scope doesn't have to thread the id through every
            // call site — the ambient scope fills it in.
            entry.BatchId ??= AuditBatch.CurrentId;
            entry.BatchLabel ??= AuditBatch.CurrentLabel;

            using var conn = _db.Admin();
            var id = await conn.ExecuteScalarAsync<long>(
                @"INSERT INTO audit_log
                    (batch_id, batch_label, operator, operator_ip, category, action, target_type, target_name, target_id,
                     ra_command, ra_response, state_before, state_after, is_reversible, revert_kind, reverses_id, success, notes)
                  VALUES
                    (@BatchId, @BatchLabel, @Operator, @OperatorIp, @Category, @Action, @TargetType, @TargetName, @TargetId,
                     @RaCommand, @RaResponse, @StateBefore, @StateAfter, @IsReversible, @RevertKind, @ReversesId, @Success, @Notes);
                  SELECT LAST_INSERT_ID();",
                entry);

            return id;
        }
        catch (Exception ex)
        {
            // Audit logging should never crash the app — log and continue
            _logger.LogError(ex, "Failed to write audit log: {Category}/{Action} on {Target}",
                entry.Category, entry.Action, entry.TargetName);
            return 0;
        }
    }

    /// <summary>
    /// Convenience: log an RA command with its response.
    /// </summary>
    public async Task<long> LogCommandAsync(
        string command,
        string? response,
        bool success,
        string? targetType = null,
        string? targetName = null,
        int? targetId = null,
        string? stateBefore = null,
        string? stateAfter = null,
        bool isReversible = false,
        string? operator_ = null,
        string? operatorIp = null,
        string? notes = null)
    {
        var category = CategorizeCommand(command);
        var action = ActionFromCommand(command);

        return await LogAsync(new AuditEntry
        {
            Operator = operator_ ?? "admin",
            OperatorIp = operatorIp,
            Category = category,
            Action = action,
            TargetType = targetType,
            TargetName = targetName,
            TargetId = targetId,
            RaCommand = command,
            RaResponse = response,
            StateBefore = stateBefore,
            StateAfter = stateAfter,
            IsReversible = isReversible,
            Success = success,
            Notes = notes
        });
    }

    /// <summary>
    /// Log a config change with before/after JSON.
    /// </summary>
    public async Task LogConfigChangeAsync(string configJson, string? changesJson, string? operator_ = null)
    {
        try
        {
            using var conn = _db.Admin();
            await conn.ExecuteAsync(
                @"INSERT INTO config_history (operator, config_json, changes)
                  VALUES (@Operator, @ConfigJson, @Changes)",
                new { Operator = operator_ ?? "admin", ConfigJson = configJson, Changes = changesJson });

            await LogAsync(new AuditEntry
            {
                Operator = operator_ ?? "admin",
                Category = "config",
                Action = "save_config",
                TargetType = "config",
                TargetName = "server-config.json",
                StateAfter = changesJson,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log config change");
        }
    }

    /// <summary>
    /// Get recent audit log entries.
    /// </summary>
    public async Task<IEnumerable<AuditLogRow>> GetRecentAsync(int count = 50, string? category = null)
    {
        try
        {
            using var conn = _db.Admin();
            var sql = @"SELECT id, timestamp, operator, operator_ip AS operatorIp, 
                               category, action, target_type AS targetType, target_name AS targetName, 
                               target_id AS targetId, ra_command AS raCommand, ra_response AS raResponse,
                               state_before AS stateBefore, state_after AS stateAfter,
                               is_reversible AS isReversible, reverses_id AS reversesId, success, notes,
                         batch_id AS batchId, batch_label AS batchLabel, revert_kind AS revertKind,
                         reverted_at AS revertedAt, reverted_by_id AS revertedById
                        FROM audit_log";

            if (!string.IsNullOrEmpty(category))
                sql += " WHERE category = @category";

            sql += " ORDER BY id DESC LIMIT @count";

            return await conn.QueryAsync<AuditLogRow>(sql, new { count, category });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query audit log");
            return Enumerable.Empty<AuditLogRow>();
        }
    }

    /// <summary>
    /// Get audit history for a specific target.
    /// </summary>
    public async Task<IEnumerable<AuditLogRow>> GetTargetHistoryAsync(string targetType, string targetName, int count = 50)
    {
        try
        {
            using var conn = _db.Admin();
            return await conn.QueryAsync<AuditLogRow>(
                @"SELECT id, timestamp, operator, operator_ip AS operatorIp,
                         category, action, target_type AS targetType, target_name AS targetName,
                         target_id AS targetId, ra_command AS raCommand, ra_response AS raResponse,
                         state_before AS stateBefore, state_after AS stateAfter,
                         is_reversible AS isReversible, reverses_id AS reversesId, success, notes,
                         batch_id AS batchId, batch_label AS batchLabel, revert_kind AS revertKind,
                         reverted_at AS revertedAt, reverted_by_id AS revertedById
                  FROM audit_log
                  WHERE target_type = @targetType AND target_name = @targetName
                  ORDER BY id DESC LIMIT @count",
                new { targetType, targetName, count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query target history: {Type}/{Name}", targetType, targetName);
            return Enumerable.Empty<AuditLogRow>();
        }
    }

    // ==================== Helpers ====================

    private static string CategorizeCommand(string command)
    {
        var cmd = command.TrimStart('.').ToLower();
        if (cmd.StartsWith("account")) return "account";
        if (cmd.StartsWith("character") || cmd.StartsWith("reset")) return "character";
        if (cmd.StartsWith("ban") || cmd.StartsWith("unban")) return "ban";
        if (cmd.StartsWith("guild")) return "guild";
        if (cmd.StartsWith("server") || cmd.StartsWith("saveall") || cmd.StartsWith("reload")) return "system";
        if (cmd.StartsWith("bot") || cmd.StartsWith("ahbot") || cmd.StartsWith("battlebot")) return "bot";
        if (cmd.StartsWith("send") || cmd.StartsWith("kick") || cmd.StartsWith("mute") || cmd.StartsWith("unmute")) return "character";
        if (cmd.StartsWith("tele")) return "character";
        if (cmd.StartsWith("antispam") || cmd.StartsWith("spamer")) return "system";
        if (cmd.StartsWith("lookup") || cmd.StartsWith("spell") || cmd.StartsWith("list")) return "query";
        return "command";
    }

    private static string ActionFromCommand(string command)
    {
        var parts = command.TrimStart('.').Split(' ', 3);
        if (parts.Length >= 2)
            return (parts[0] + "_" + parts[1]).ToLower().Replace(".", "_");
        return parts[0].ToLower();
    }
}

// ==================== DTOs ====================

/// <summary>
/// Ambient batch scope. Wrap a tool run in <c>using (AuditBatch.Begin("ARPG Lootifier — Deadmines"))</c>
/// and every AuditEntry written inside it — including ones written deep inside services
/// that know nothing about batching — is stamped with the same batch_id. That is what lets
/// the Change Graph draw "one Lootifier pass" instead of 400 unrelated rows.
///
/// AsyncLocal, so the scope follows the request across awaits and does not leak between
/// concurrent requests.
/// </summary>
public sealed class AuditBatch : IDisposable
{
    private static readonly AsyncLocal<AuditBatch?> _current = new();

    public string Id { get; }
    public string Label { get; }

    private readonly AuditBatch? _parent;
    private bool _disposed;

    private AuditBatch(string label)
    {
        Id = Guid.NewGuid().ToString("N")[..16];
        Label = label;
        _parent = _current.Value;
        _current.Value = this;
    }

    public static string? CurrentId => _current.Value?.Id;
    public static string? CurrentLabel => _current.Value?.Label;

    /// <summary>Opens a batch scope. Nested scopes keep the innermost label.</summary>
    public static AuditBatch Begin(string label) => new(label);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _current.Value = _parent;
    }
}

/// <summary>
/// How a logged change can be undone. The Change Graph dispatches on this instead of
/// guessing from the action name, and <see cref="None"/> means the graph shows the row
/// as history only rather than offering an undo that would fail.
/// </summary>
public static class RevertKind
{
    /// <summary>No supported undo path.</summary>
    public const string None = "none";

    /// <summary>Restore the target rows from the matching og_* baseline table.</summary>
    public const string Baseline = "baseline";

    /// <summary>Re-apply the rows captured in state_before.</summary>
    public const string StateBefore = "state_before";

    /// <summary>The change created something; undoing means deleting it again.</summary>
    public const string DeleteCustom = "delete_custom";

    /// <summary>The originating tool owns a rollback path through its own registry tables.</summary>
    public const string Registry = "registry";
}

public class AuditEntry
{
    public string Operator { get; set; } = "admin";
    public string? OperatorIp { get; set; }
    public string Category { get; set; } = "command";
    public string Action { get; set; } = "";
    public string? TargetType { get; set; }
    public string? TargetName { get; set; }
    public int? TargetId { get; set; }
    public string? RaCommand { get; set; }
    public string? RaResponse { get; set; }
    public string? StateBefore { get; set; }
    public string? StateAfter { get; set; }
    public bool IsReversible { get; set; }
    public long? ReversesId { get; set; }
    public bool Success { get; set; } = true;
    public string? Notes { get; set; }

    /// <summary>Groups every row a single tool run produced. Null for one-off actions.</summary>
    public string? BatchId { get; set; }

    /// <summary>Human title for the batch, e.g. "ARPG Lootifier — Deadmines".</summary>
    public string? BatchLabel { get; set; }

    /// <summary>One of <see cref="RevertKind"/>.</summary>
    public string RevertKind { get; set; } = Services.RevertKind.None;
}

public class AuditLogRow
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Operator { get; set; } = "";
    public string? OperatorIp { get; set; }
    public string Category { get; set; } = "";
    public string Action { get; set; } = "";
    public string? TargetType { get; set; }
    public string? TargetName { get; set; }
    public int? TargetId { get; set; }
    public string? RaCommand { get; set; }
    public string? RaResponse { get; set; }
    public string? StateBefore { get; set; }
    public string? StateAfter { get; set; }
    public bool IsReversible { get; set; }
    public long? ReversesId { get; set; }
    public bool Success { get; set; }
    public string? Notes { get; set; }
    public string? BatchId { get; set; }
    public string? BatchLabel { get; set; }
    public string RevertKind { get; set; } = Services.RevertKind.None;
    public DateTime? RevertedAt { get; set; }
    public long? RevertedById { get; set; }
}