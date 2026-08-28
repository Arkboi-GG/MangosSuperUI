using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using MangosSuperUI.BotLogic.Tracking;
using MangosSuperUI.Hubs;
using MangosSuperUI.Models;
using Microsoft.AspNetCore.SignalR;

namespace MangosSuperUI.Services;

/// <summary>
/// Durable, one-deep queue for combat-loadout intent. The queue is authoritative
/// in vmangos_admin; the in-memory loop is only a dispatcher. A row is claimed
/// before any TCP write, and an interrupted/uncertain dispatch is never retried
/// automatically because a destructive talent reset may already have committed.
/// </summary>
public sealed class BotCombatLoadoutQueueService : BackgroundService
{
    private const string Waiting = "waiting";
    private const string Dispatching = "dispatching";
    private const string Applied = "applied";
    private const string Failed = "failed";
    private const string Uncertain = "uncertain";
    private const string Cancelled = "cancelled";

    private enum TerminalCas
    {
        Waiting,
        OwnedDispatch,
        ExpiredDispatch
    }

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ConnectionHydrationDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaximumStateAge = TimeSpan.FromSeconds(15);
    private const int MaximumConcurrentDispatches = 4;

    private static readonly HashSet<string> TransientCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "runtime_hydrating",
        "runtime_stale",
        "bot_dead",
        "bot_in_combat",
        "bot_casting",
        "bot_teleporting",
        "bot_on_taxi",
        "bot_possessed",
        "bot_in_battleground",
        "bot_busy"
    };

    private static readonly HashSet<string> UncertainAfterClaimCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ack_timeout",
        "outcome_unknown",
        "bridge_send_failed",
        "rotation_persistence_failed",
        "ack_state_mismatch",
        "rollback_failed",
        "bot_offline"
    };

    /// <summary>
    /// These failures are produced before the core starts mutating a build, so a
    /// direct request that loses a race with one of these conditions can safely
    /// become durable queue intent. Connection failures are deliberately absent:
    /// after a socket write their outcome may be unknown.
    /// </summary>
    public static bool CanQueueAfterDirectRejection(string? code)
        => !string.IsNullOrWhiteSpace(code) && TransientCodes.Contains(code);

    private const string QueueSelect = @"
        SELECT bot_guid AS Guid,
               bot_name AS BotName,
               queue_id AS QueueId,
               status AS Status,
               payload_json AS PayloadJson,
               spec_tab AS SpecTab,
               profile_id AS ProfileId,
               profile_name AS ProfileName,
               active_role AS ActiveRole,
               active_role_name AS ActiveRoleName,
               rotation_mode AS RotationMode,
               rotation_profile AS RotationProfile,
               rotation_name AS RotationName,
               rotation_fingerprint AS RotationFingerprint,
               reset_talents AS ResetTalents,
               expected_revision AS ExpectedRevision,
               observed_session_at AS ObservedSessionAt,
               request_id AS RequestId,
               claim_owner AS ClaimOwner,
               claim_expires_at AS ClaimExpiresAt,
               attempt_count AS AttemptCount,
               queued_by AS QueuedBy,
               queued_from AS QueuedFrom,
               created_at AS CreatedAt,
               updated_at AS UpdatedAt,
               next_attempt_at AS NextAttemptAt,
               dispatched_at AS DispatchedAt,
               completed_at AS CompletedAt,
               last_code AS LastCode,
               last_message AS LastMessage,
               created_at <= DATE_SUB(CURRENT_TIMESTAMP(3), INTERVAL 15 MINUTE) AS IsExpired
        FROM bot_combat_loadout_queue";

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConnectionFactory _db;
    private readonly BotCombatLoadoutService _loadouts;
    private readonly BotBridgeService _bridge;
    private readonly IHubContext<BotBridgeHub> _hub;
    private readonly WorldMaintenanceGate _worldMaintenance;
    private readonly ILogger<BotCombatLoadoutQueueService> _logger;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _botGates = new();
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    public BotCombatLoadoutQueueService(
        ConnectionFactory db,
        BotCombatLoadoutService loadouts,
        BotBridgeService bridge,
        IHubContext<BotBridgeHub> hub,
        WorldMaintenanceGate worldMaintenance,
        ILogger<BotCombatLoadoutQueueService> logger)
    {
        _db = db;
        _loadouts = loadouts;
        _bridge = bridge;
        _hub = hub;
        _worldMaintenance = worldMaintenance;
        _logger = logger;
    }

    private async ValueTask<WorldMaintenanceGate.Lease> AcquireQueueOperationAsync()
    {
        WorldMaintenanceGate.Lease? lease = await _worldMaintenance.TryAcquireOperationAsync();
        return lease ?? throw QueueError(503, "queue_unavailable",
            "Combat-loadout queue operations are unavailable while World State restores the world databases.");
    }

    public async Task<BotCombatLoadoutQueueView?> GetAsync(
        int guid,
        CancellationToken cancellationToken = default)
    {
        await using WorldMaintenanceGate.Lease maintenance = await AcquireQueueOperationAsync();
        QueueRow? row = await LoadRowAsync(guid, cancellationToken);
        return row == null || IsTerminalHidden(row.Status) ? null : ToView(row);
    }

    private async Task<bool> HasDispatchBlockingEntryCoreAsync(
        int guid,
        CancellationToken cancellationToken = default)
    {
        QueueRow? row = await LoadRowAsync(guid, cancellationToken);
        return row != null && row.Status is Waiting or Dispatching or Uncertain;
    }

    public async Task<BotCombatLoadoutQueueMutationResult> EnqueueAsync(
        int guid,
        BotCombatLoadoutRequest request,
        string? queuedBy,
        string? queuedFrom,
        CancellationToken cancellationToken = default)
    {
        await using WorldMaintenanceGate.Lease maintenance = await AcquireQueueOperationAsync();
        SemaphoreSlim gate = _botGates.GetOrAdd(guid, static _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            CircuitTrace.Hit(guid, "loadout: enqueue blocked, per-bot gate busy");
            throw QueueError(409, "queue_busy",
                "This bot's combat build is changing right now. Refresh its live state before trying again.");
        }
        try
        {
            return await EnqueueCoreAsync(guid, request, queuedBy, queuedFrom, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BotCombatLoadoutQueueMutationResult> EnqueueCoreAsync(
        int guid,
        BotCombatLoadoutRequest request,
        string? queuedBy,
        string? queuedFrom,
        CancellationToken cancellationToken)
    {
        BotCombatLoadoutQueueValidation validation = await _loadouts.ValidateForQueueAsync(
            guid,
            request,
            cancellationToken);

        string queueId = Guid.NewGuid().ToString("N");
        string payloadJson = JsonSerializer.Serialize(validation.Request, PayloadJsonOptions);
        string actor = CleanAuditValue(queuedBy, "web", 64) ?? "web";
        string? source = CleanAuditValue(queuedFrom, null, 64);

        await using var conn = _db.Admin();
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        bool replaced;
        try
        {
            QueueIdentity? existing = await conn.QueryFirstOrDefaultAsync<QueueIdentity>(new CommandDefinition(
                "SELECT queue_id AS QueueId, status AS Status FROM bot_combat_loadout_queue WHERE bot_guid = @guid FOR UPDATE",
                new { guid }, tx, cancellationToken: cancellationToken));

            if (string.Equals(existing?.Status, Dispatching, StringComparison.OrdinalIgnoreCase))
            {
                CircuitTrace.Hit(guid, "loadout: enqueue rejected, row already dispatching");
                throw QueueError(409, "queue_dispatching",
                    "This bot's queued build is already being dispatched and can no longer be replaced.");
            }
            if (string.Equals(existing?.Status, Uncertain, StringComparison.OrdinalIgnoreCase))
            {
                CircuitTrace.Hit(guid, "loadout: enqueue rejected, prior dispatch uncertain");
                throw QueueError(409, "queue_uncertain",
                    "The prior queued dispatch has an uncertain result. Refresh live state before dismissing or creating another build.");
            }

            string expectedQueueId = (request.ExpectedQueueId ?? "").Trim().ToLowerInvariant();
            bool replaceable = existing?.Status is Waiting or Failed;
            if (replaceable && !string.Equals(expectedQueueId, existing!.QueueId, StringComparison.OrdinalIgnoreCase))
            {
                CircuitTrace.Hit(guid, "loadout: enqueue rejected, queue id CAS mismatch on replaceable row");
                throw QueueError(409, "queue_changed",
                    "The queued build changed in another client. Refresh before replacing it.");
            }
            if (!replaceable && expectedQueueId.Length > 0)
            {
                CircuitTrace.Hit(guid, "loadout: enqueue rejected, row no longer replaceable");
                throw QueueError(409, "queue_changed",
                    "The queued build is no longer replaceable. Refresh its current state.");
            }

            replaced = replaceable;
            await conn.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO bot_combat_loadout_queue
                    (bot_guid, bot_name, queue_id, status, payload_json,
                     spec_tab, profile_id, profile_name, active_role, active_role_name,
                     rotation_mode, rotation_profile, rotation_name, rotation_fingerprint,
                     reset_talents, expected_revision, observed_session_at, request_id,
                     claim_owner, claim_expires_at, attempt_count,
                     queued_by, queued_from, created_at, updated_at, next_attempt_at,
                     dispatched_at, completed_at, last_code, last_message)
                VALUES
                    (@Guid, @BotName, @QueueId, 'waiting', @PayloadJson,
                     @SpecTab, @ProfileId, @ProfileName, @ActiveRole, @ActiveRoleName,
                     @RotationMode, @RotationProfile, @RotationName, @RotationFingerprint,
                     @ResetTalents, @ExpectedRevision, @ObservedSessionAt, NULL,
                     NULL, NULL, 0,
                     @QueuedBy, @QueuedFrom, CURRENT_TIMESTAMP(3), CURRENT_TIMESTAMP(3), CURRENT_TIMESTAMP(3),
                     NULL, NULL, 'queued', 'Waiting for a safe live bot state.')
                ON DUPLICATE KEY UPDATE
                    bot_name = VALUES(bot_name),
                    queue_id = VALUES(queue_id),
                    status = 'waiting',
                    payload_json = VALUES(payload_json),
                    spec_tab = VALUES(spec_tab),
                    profile_id = VALUES(profile_id),
                    profile_name = VALUES(profile_name),
                    active_role = VALUES(active_role),
                    active_role_name = VALUES(active_role_name),
                    rotation_mode = VALUES(rotation_mode),
                    rotation_profile = VALUES(rotation_profile),
                    rotation_name = VALUES(rotation_name),
                    rotation_fingerprint = VALUES(rotation_fingerprint),
                    reset_talents = VALUES(reset_talents),
                    expected_revision = VALUES(expected_revision),
                    observed_session_at = VALUES(observed_session_at),
                    request_id = NULL,
                    claim_owner = NULL,
                    claim_expires_at = NULL,
                    attempt_count = 0,
                    queued_by = VALUES(queued_by),
                    queued_from = VALUES(queued_from),
                    created_at = CURRENT_TIMESTAMP(3),
                    updated_at = CURRENT_TIMESTAMP(3),
                    next_attempt_at = CURRENT_TIMESTAMP(3),
                    dispatched_at = NULL,
                    completed_at = NULL,
                    last_code = 'queued',
                    last_message = 'Waiting for a safe live bot state.'",
                new
                {
                    validation.Guid,
                    validation.BotName,
                    QueueId = queueId,
                    PayloadJson = payloadJson,
                    validation.Request.SpecTab,
                    validation.ProfileId,
                    validation.ProfileName,
                    validation.ActiveRole,
                    validation.ActiveRoleName,
                    RotationMode = validation.Request.RotationMode,
                    RotationProfile = validation.Request.RotationProfile,
                    validation.RotationName,
                    validation.RotationFingerprint,
                    validation.Request.ResetTalents,
                    ExpectedRevision = validation.Request.ExpectedRevision!.Value,
                    validation.ObservedSessionAt,
                    QueuedBy = actor,
                    QueuedFrom = source
                }, tx, cancellationToken: cancellationToken));

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            CircuitTrace.Hit(guid, "loadout: enqueue tx failed, rolling back");
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch { CircuitTrace.Hit(guid, "loadout: enqueue rollback itself failed"); }
            throw;
        }

        QueueRow row = await LoadRowAsync(guid, cancellationToken)
            ?? throw QueueError(503, "queue_persistence_failed", "The queued build could not be read back after it was saved.");
        BotCombatLoadoutQueueView view = ToView(row);
        await NotifyAsync(guid, replaced ? "replaced" : Waiting,
            replaced ? "Queued build replaced." : "Build queued.", view, cancellationToken);
        _logger.LogInformation(
            "[COMBAT-LOADOUT-QUEUE] {Action} {QueueId} for {Bot} ({Guid}): {Profile}/{Role}/{Rotation}, reset={Reset}",
            replaced ? "replaced" : "queued", queueId, validation.BotName, guid,
            validation.ProfileId, validation.ActiveRoleName, validation.RotationName,
            validation.Request.ResetTalents);

        return new BotCombatLoadoutQueueMutationResult
        {
            Status = replaced ? "replaced" : Waiting,
            Message = replaced
                ? "The unsent queued build was replaced. It will apply automatically when the bot is safe."
                : "The build is queued and will apply automatically when the bot is safe.",
            Queue = view
        };
    }

    public async Task<BotCombatLoadoutQueueMutationResult> CancelAsync(
        int guid,
        string? expectedQueueId,
        string? expectedStatus,
        CancellationToken cancellationToken = default)
    {
        await using WorldMaintenanceGate.Lease maintenance = await AcquireQueueOperationAsync();
        SemaphoreSlim gate = _botGates.GetOrAdd(guid, static _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            CircuitTrace.Hit(guid, "loadout: cancel blocked, per-bot gate busy");
            throw QueueError(409, "queue_busy",
                "This bot's queued build is changing right now. Refresh its live state before trying again.");
        }
        try
        {
            return await CancelCoreAsync(guid, expectedQueueId, expectedStatus, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<BotCombatLoadoutQueueMutationResult> CancelCoreAsync(
        int guid,
        string? expectedQueueId,
        string? expectedStatus,
        CancellationToken cancellationToken)
    {
        await using var conn = _db.Admin();
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        QueueRow row;
        bool dismissingUncertain;
        try
        {
            row = await conn.QueryFirstOrDefaultAsync<QueueRow>(new CommandDefinition(
                QueueSelect + " WHERE bot_guid = @guid FOR UPDATE",
                new { guid }, tx, cancellationToken: cancellationToken))
                ?? throw QueueError(404, "queue_not_found", "This bot has no queued combat loadout.");
            if (IsTerminalHidden(row.Status))
            {
                CircuitTrace.Hit(guid, "loadout: cancel target already terminal-hidden");
                throw QueueError(404, "queue_not_found", "This bot has no queued combat loadout.");
            }
            if (!string.Equals(
                    (expectedQueueId ?? "").Trim(),
                    row.QueueId,
                    StringComparison.OrdinalIgnoreCase))
            {
                CircuitTrace.Hit(guid, "loadout: cancel rejected, queue id CAS mismatch");
                throw QueueError(409, "queue_changed",
                    "The queued build changed in another client. Refresh before cancelling or dismissing it.");
            }
            if (row.Status == Dispatching)
            {
                CircuitTrace.Hit(guid, "loadout: cancel rejected, dispatch already started");
                throw QueueError(409, "queue_dispatching",
                    "The core dispatch has already started. It cannot be cancelled safely.");
            }
            string statusToken = (expectedStatus ?? "").Trim().ToLowerInvariant();
            if (!string.Equals(statusToken, row.Status, StringComparison.OrdinalIgnoreCase))
            {
                CircuitTrace.Hit(guid, "loadout: cancel rejected, status CAS mismatch");
                throw QueueError(409, "queue_changed",
                    "The queued build status changed in another client. Refresh before cancelling or dismissing it.");
            }
            if (row.Status is not (Waiting or Failed or Uncertain))
            {
                CircuitTrace.Hit(guid, "loadout: cancel rejected, status not cancellable");
                throw QueueError(409, "queue_not_cancellable",
                    "This queued build can no longer be cancelled or dismissed.");
            }
            dismissingUncertain = row.Status == Uncertain;
            string code = dismissingUncertain ? "uncertain_dismissed" : "cancelled";
            string message = dismissingUncertain
                ? "Uncertain result dismissed after operator review. This did not undo core state."
                : "Cancelled before dispatch.";

            int changed = await conn.ExecuteAsync(new CommandDefinition(@"
                UPDATE bot_combat_loadout_queue
                   SET status = 'cancelled', completed_at = CURRENT_TIMESTAMP(3),
                       last_code = @code, last_message = @message
                 WHERE bot_guid = @guid AND queue_id = @queueId AND status = @expectedStatus",
                new { guid, queueId = row.QueueId, expectedStatus = statusToken, code, message }, tx,
                cancellationToken: cancellationToken));
            if (changed != 1)
            {
                CircuitTrace.Hit(guid, "loadout: cancel update CAS lost");
                throw QueueError(409, "queue_changed",
                    "The queued build changed before it could be cancelled. Refresh its current state.");
            }
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            CircuitTrace.Hit(guid, "loadout: cancel tx failed, rolling back");
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch { CircuitTrace.Hit(guid, "loadout: cancel rollback itself failed"); }
            throw;
        }

        string responseMessage = dismissingUncertain
            ? "The uncertain queue record was dismissed. No core state was changed or undone."
            : "The queued build was cancelled before dispatch.";
        await NotifyAsync(guid, dismissingUncertain ? "dismissed" : Cancelled,
            responseMessage, null, cancellationToken);
        _logger.LogInformation("[COMBAT-LOADOUT-QUEUE] {Action} build record for guid={Guid}",
            dismissingUncertain ? "dismissed uncertain" : "cancelled pending", guid);
        return new BotCombatLoadoutQueueMutationResult
        {
            Status = dismissingUncertain ? "dismissed" : Cancelled,
            Message = responseMessage,
            Queue = null
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        WorldMaintenanceGate.Lease? startupLease = await _worldMaintenance.TryAcquireOperationAsync();
        if (startupLease != null)
        {
            CircuitTrace.Hit(0, "loadout: startup lease acquired, recovering interrupted dispatches");
            await using (startupLease)
                await RecoverInterruptedDispatchesAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                WorldMaintenanceGate.Lease? sweepLease = await _worldMaintenance.TryAcquireOperationAsync();
                if (sweepLease != null)
                {
                    CircuitTrace.Hit(0, "loadout: sweep lease acquired");
                    // One lease covers claim, core acknowledgement, and terminal DB
                    // write so a restore cannot split a sweep across two snapshots.
                    await using (sweepLease)
                    {
                        await RecoverStaleDispatchesAsync(stoppingToken);
                        IReadOnlyList<QueueRow> due = await LoadDueRowsAsync(stoppingToken);
                        if (due.Count > 0)
                        {
                            CircuitTrace.Hit(0, "loadout: dispatching due queue rows", due.Count);
                            await Task.WhenAll(due.Take(MaximumConcurrentDispatches)
                                .Select(row => DispatchOneAsync(row, stoppingToken)));
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                CircuitTrace.Hit(0, "loadout: sweep cancelled, dispatcher stopping");
                break;
            }
            catch (Exception ex)
            {
                CircuitTrace.Hit(0, "loadout: sweep failed, durable queue intact");
                _logger.LogError(ex,
                    "[COMBAT-LOADOUT-QUEUE] sweep failed; the durable queue remains intact and will be retried");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                CircuitTrace.Hit(0, "loadout: sweep delay cancelled, dispatcher stopping");
                break;
            }
        }
    }

    public async Task<BotCombatLoadoutApplyResult> ApplyDirectAsync(
        int guid,
        BotCombatLoadoutRequest request,
        CancellationToken cancellationToken = default,
        string? initiatedBy = null,
        string? initiatedFrom = null)
    {
        await using WorldMaintenanceGate.Lease maintenance = await AcquireQueueOperationAsync();
        SemaphoreSlim gate = _botGates.GetOrAdd(guid, static _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            CircuitTrace.Hit(guid, "loadout: direct apply blocked, per-bot gate busy");
            throw QueueError(409, "queue_busy",
                "This bot's combat build is changing right now. Refresh its live state before trying again.");
        }
        try
        {
            if (await HasDispatchBlockingEntryCoreAsync(guid, cancellationToken))
            {
                CircuitTrace.Hit(guid, "loadout: direct apply rejected, blocking queue entry exists");
                throw QueueError(409, "queue_exists",
                    "This bot already has a pending combat build. Replace or cancel it instead of applying around the queue.");
            }

            // Validation is read-only and may still honor an abandoned HTTP request.
            // Once the durable dispatch claim is inserted below, the operation owns a
            // correlated core request and must finish or become uncertain even if the
            // browser disconnects.
            BotCombatLoadoutQueueValidation validation = await _loadouts.ValidateForQueueAsync(
                guid,
                request,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            string requestId = Guid.NewGuid().ToString("N");
            QueueRow row = await CreateDirectDispatchAsync(
                validation,
                request,
                requestId,
                initiatedBy,
                initiatedFrom,
                CancellationToken.None);
            await NotifyAsync(guid, Dispatching,
                "The direct build is being dispatched to the core.", ToView(row), CancellationToken.None);

            try
            {
                BotCombatLoadoutApplyResult result = await _loadouts.ApplyAsync(
                    guid,
                    validation.Request,
                    CancellationToken.None,
                    operationRequestId: requestId,
                    expectedRotationFingerprint: validation.RotationFingerprint,
                    expectedSessionAt: validation.ObservedSessionAt);
                if (!await MarkTerminalAsync(
                        row, Applied, result.Status, result.Message,
                        TerminalCas.OwnedDispatch, CancellationToken.None))
                {
                    CircuitTrace.Hit(guid, "loadout: direct apply journal CAS lost after core applied");
                    throw QueueError(409, "direct_journal_changed",
                        "The core completed the build, but its durable journal changed before completion was recorded. Refresh live state; do not repeat the reset.");
                }
                return result;
            }
            catch (BotCombatLoadoutException ex) when (TransientCodes.Contains(ex.Code))
            {
                CircuitTrace.HitNote(guid, "loadout: direct apply transient rejection before mutation", ex.Code);
                if (!await MarkTerminalAsync(
                        row, Failed, ex.Code, ex.Message,
                        TerminalCas.OwnedDispatch, CancellationToken.None))
                {
                    CircuitTrace.Hit(guid, "loadout: direct apply failed-mark journal CAS lost");
                    throw QueueError(409, "direct_journal_changed",
                        "The direct build was rejected before mutation, but its durable journal changed. Refresh before trying again.");
                }

                // The controllers preserve their existing UX by converting this
                // verified pre-mutation rejection into a waiting row. Echoing this
                // journal id gives that replacement the same optimistic CAS used by
                // every other failed-row replacement.
                request.ExpectedQueueId = row.QueueId;
                throw;
            }
            catch (BotCombatLoadoutException ex) when (UncertainAfterClaimCodes.Contains(ex.Code))
            {
                CircuitTrace.HitNote(guid, "loadout: direct apply uncertain after claim", ex.Code);
                await MarkTerminalAsync(
                    row, Uncertain, ex.Code,
                    ex.Message + " Automatic retry is disabled.",
                    TerminalCas.OwnedDispatch, CancellationToken.None);
                throw;
            }
            catch (BotCombatLoadoutException ex)
            {
                CircuitTrace.HitNote(guid, "loadout: direct apply failed", ex.Code);
                await MarkTerminalAsync(
                    row, Failed, ex.Code, ex.Message,
                    TerminalCas.OwnedDispatch, CancellationToken.None);
                throw;
            }
            catch (OperationCanceledException)
            {
                CircuitTrace.Hit(guid, "loadout: direct apply interrupted after durable claim");
                await MarkTerminalAsync(
                    row, Uncertain, "dispatch_interrupted",
                    "The direct dispatch was interrupted after its durable claim. Refresh live state; automatic retry is disabled.",
                    TerminalCas.OwnedDispatch, CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                CircuitTrace.Hit(guid, "loadout: direct apply unexpected error, marking uncertain");
                _logger.LogError(ex,
                    "[COMBAT-LOADOUT-QUEUE] unexpected direct dispatch failure for {Bot} ({Guid}); marking uncertain",
                    row.BotName, row.Guid);
                await MarkTerminalAsync(
                    row, Uncertain, "dispatch_uncertain",
                    "An unexpected direct dispatch error occurred. Refresh live state; automatic retry is disabled.",
                    TerminalCas.OwnedDispatch, CancellationToken.None);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<QueueRow> CreateDirectDispatchAsync(
        BotCombatLoadoutQueueValidation validation,
        BotCombatLoadoutRequest originalRequest,
        string requestId,
        string? initiatedBy,
        string? initiatedFrom,
        CancellationToken cancellationToken)
    {
        string queueId = Guid.NewGuid().ToString("N");
        string payloadJson = JsonSerializer.Serialize(validation.Request, PayloadJsonOptions);
        string actor = CleanAuditValue(initiatedBy, "web_direct", 64) ?? "web_direct";
        string? source = CleanAuditValue(initiatedFrom, null, 64);

        await using var conn = _db.Admin();
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            QueueIdentity? existing = await conn.QueryFirstOrDefaultAsync<QueueIdentity>(new CommandDefinition(
                "SELECT queue_id AS QueueId, status AS Status FROM bot_combat_loadout_queue WHERE bot_guid = @guid FOR UPDATE",
                new { guid = validation.Guid }, tx, cancellationToken: cancellationToken));

            if (existing?.Status is Waiting or Dispatching or Uncertain)
            {
                CircuitTrace.Hit(validation.Guid, "loadout: direct claim rejected, pending or uncertain row exists");
                throw QueueError(409, "queue_exists",
                    "This bot already has a pending or uncertain combat build. Review that record before applying another build.");
            }

            string expectedQueueId = (originalRequest.ExpectedQueueId ?? "").Trim().ToLowerInvariant();
            bool replacingFailed = string.Equals(existing?.Status, Failed, StringComparison.OrdinalIgnoreCase);
            if (replacingFailed
                && !string.Equals(expectedQueueId, existing!.QueueId, StringComparison.OrdinalIgnoreCase))
            {
                CircuitTrace.Hit(validation.Guid, "loadout: direct claim rejected, failed-row CAS mismatch");
                throw QueueError(409, "queue_changed",
                    "The prior failed build changed in another client. Refresh before replacing it.");
            }
            if (!replacingFailed && expectedQueueId.Length > 0)
            {
                CircuitTrace.Hit(validation.Guid, "loadout: direct claim rejected, prior row not replaceable");
                throw QueueError(409, "queue_changed",
                    "The prior build record is no longer replaceable. Refresh its current state.");
            }

            await conn.ExecuteAsync(new CommandDefinition(@"
                INSERT INTO bot_combat_loadout_queue
                    (bot_guid, bot_name, queue_id, status, payload_json,
                     spec_tab, profile_id, profile_name, active_role, active_role_name,
                     rotation_mode, rotation_profile, rotation_name, rotation_fingerprint,
                     reset_talents, expected_revision, observed_session_at, request_id,
                     claim_owner, claim_expires_at, attempt_count,
                     queued_by, queued_from, created_at, updated_at, next_attempt_at,
                     dispatched_at, completed_at, last_code, last_message)
                VALUES
                    (@Guid, @BotName, @QueueId, 'dispatching', @PayloadJson,
                     @SpecTab, @ProfileId, @ProfileName, @ActiveRole, @ActiveRoleName,
                     @RotationMode, @RotationProfile, @RotationName, @RotationFingerprint,
                     @ResetTalents, @ExpectedRevision, @ObservedSessionAt, @RequestId,
                     @ClaimOwner, DATE_ADD(CURRENT_TIMESTAMP(3), INTERVAL 45 SECOND), 1,
                     @QueuedBy, @QueuedFrom, CURRENT_TIMESTAMP(3), CURRENT_TIMESTAMP(3), CURRENT_TIMESTAMP(3),
                     CURRENT_TIMESTAMP(3), NULL, 'dispatching', 'Direct dispatch durably claimed; awaiting the correlated core acknowledgement.')
                ON DUPLICATE KEY UPDATE
                    bot_name = VALUES(bot_name),
                    queue_id = VALUES(queue_id),
                    status = 'dispatching',
                    payload_json = VALUES(payload_json),
                    spec_tab = VALUES(spec_tab),
                    profile_id = VALUES(profile_id),
                    profile_name = VALUES(profile_name),
                    active_role = VALUES(active_role),
                    active_role_name = VALUES(active_role_name),
                    rotation_mode = VALUES(rotation_mode),
                    rotation_profile = VALUES(rotation_profile),
                    rotation_name = VALUES(rotation_name),
                    rotation_fingerprint = VALUES(rotation_fingerprint),
                    reset_talents = VALUES(reset_talents),
                    expected_revision = VALUES(expected_revision),
                    observed_session_at = VALUES(observed_session_at),
                    request_id = VALUES(request_id),
                    claim_owner = VALUES(claim_owner),
                    claim_expires_at = VALUES(claim_expires_at),
                    attempt_count = 1,
                    queued_by = VALUES(queued_by),
                    queued_from = VALUES(queued_from),
                    created_at = CURRENT_TIMESTAMP(3),
                    updated_at = CURRENT_TIMESTAMP(3),
                    next_attempt_at = CURRENT_TIMESTAMP(3),
                    dispatched_at = CURRENT_TIMESTAMP(3),
                    completed_at = NULL,
                    last_code = 'dispatching',
                    last_message = 'Direct dispatch durably claimed; awaiting the correlated core acknowledgement.'",
                new
                {
                    validation.Guid,
                    validation.BotName,
                    QueueId = queueId,
                    PayloadJson = payloadJson,
                    validation.Request.SpecTab,
                    validation.ProfileId,
                    validation.ProfileName,
                    validation.ActiveRole,
                    validation.ActiveRoleName,
                    RotationMode = validation.Request.RotationMode,
                    RotationProfile = validation.Request.RotationProfile,
                    validation.RotationName,
                    validation.RotationFingerprint,
                    validation.Request.ResetTalents,
                    ExpectedRevision = validation.Request.ExpectedRevision!.Value,
                    validation.ObservedSessionAt,
                    RequestId = requestId,
                    ClaimOwner = _instanceId,
                    QueuedBy = actor,
                    QueuedFrom = source
                }, tx, cancellationToken: cancellationToken));

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            CircuitTrace.Hit(validation.Guid, "loadout: direct claim tx failed, rolling back");
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch { CircuitTrace.Hit(validation.Guid, "loadout: direct claim rollback itself failed"); }
            throw;
        }

        QueueRow row = await LoadRowAsync(validation.Guid, CancellationToken.None)
            ?? throw QueueError(503, "direct_journal_failed",
                "The direct build journal could not be read after it was claimed; nothing was sent to the core.");
        if (!string.Equals(row.QueueId, queueId, StringComparison.OrdinalIgnoreCase)
            || row.Status != Dispatching
            || !string.Equals(row.ClaimOwner, _instanceId, StringComparison.Ordinal))
        {
            CircuitTrace.Hit(validation.Guid, "loadout: direct claim readback mismatch, aborting before core write");
            throw QueueError(409, "direct_journal_changed",
                "The direct build journal changed before the core write. Nothing was sent; refresh before trying again.");
        }

        return row;
    }

    private async Task DispatchOneAsync(QueueRow row, CancellationToken stoppingToken)
    {
        SemaphoreSlim gate = _botGates.GetOrAdd(row.Guid, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(stoppingToken);
        try
        {
            await DispatchOneCoreAsync(row, stoppingToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task DispatchOneCoreAsync(QueueRow row, CancellationToken stoppingToken)
    {
        if (row.IsExpired)
        {
            CircuitTrace.Hit(row.Guid, "loadout: queued build expired, failing");
            await MarkTerminalAsync(row, Failed, "queue_expired",
                "The queued build expired after 15 minutes without reaching a safe state. Review the bot and queue it again.",
                TerminalCas.Waiting, stoppingToken);
            return;
        }

        BotState? runtime = _bridge.GetBotState(row.Guid);
        bool connected = _bridge.Connections.ContainsKey(row.Guid);
        if (!connected || runtime == null)
        {
            CircuitTrace.Hit(row.Guid, "loadout: dispatch deferred, bot offline");
            await DeferAsync(row, "bot_offline", "Waiting for the bot to reconnect.", stoppingToken);
            return;
        }
        if (!runtime.HasReceivedState
            || DateTime.UtcNow - runtime.ConnectedAt < ConnectionHydrationDelay
            || DateTime.UtcNow - runtime.LastStateReceivedUtc >= MaximumStateAge)
        {
            CircuitTrace.Hit(row.Guid, "loadout: dispatch deferred, runtime hydrating or stale");
            await DeferAsync(row, "runtime_hydrating",
                "Waiting for a fresh post-login state and rotation hydration.", stoppingToken);
            return;
        }
        if (runtime.IsDead)
        {
            CircuitTrace.Hit(row.Guid, "loadout: dispatch deferred, bot dead");
            await DeferAsync(row, "bot_dead", "Waiting for the bot to be alive.", stoppingToken);
            return;
        }
        if (runtime.InCombat)
        {
            CircuitTrace.Hit(row.Guid, "loadout: dispatch deferred, bot in combat");
            await DeferAsync(row, "bot_in_combat", "Waiting for the bot to leave combat.", stoppingToken);
            return;
        }

        BotCombatLoadoutRequest request;
        try
        {
            request = JsonSerializer.Deserialize<BotCombatLoadoutRequest>(row.PayloadJson, PayloadJsonOptions)
                ?? throw new JsonException("The queue payload was empty.");
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(row.Guid, "loadout: queue payload invalid, failing");
            await MarkTerminalAsync(
                row, Failed, "queue_payload_invalid", ex.Message,
                TerminalCas.Waiting, stoppingToken);
            return;
        }

        if (!SameSession(row.ObservedSessionAt, runtime.ConnectedAt))
        {
            CircuitTrace.Hit(row.Guid, "loadout: session changed since queue, failing");
            // Never carry a destructive reset intent across a reconnect. The bot
            // may have been edited elsewhere, and combatConfigRevision is scoped
            // to the live bridge session. Require an explicit review/requeue.
            await MarkTerminalAsync(row, Failed, "session_changed",
                "The bot reconnected before this build could apply. Review its live build and queue the change again.",
                TerminalCas.Waiting, stoppingToken);
            return;
        }
        else if (request.ExpectedRevision != runtime.CombatConfigRevision)
        {
            CircuitTrace.Hit(row.Guid, "loadout: revision stale before dispatch", runtime.CombatConfigRevision);
            await MarkTerminalAsync(row, Failed, "stale_revision",
                $"The live combat revision changed from {request.ExpectedRevision} to {runtime.CombatConfigRevision} before dispatch.",
                TerminalCas.Waiting, stoppingToken);
            return;
        }

        try
        {
            BotCombatLoadoutQueueValidation validation = await _loadouts.ValidateForQueueAsync(
                row.Guid, request, stoppingToken);
            if (!SameSession(row.ObservedSessionAt, validation.ObservedSessionAt))
            {
                CircuitTrace.Hit(row.Guid, "loadout: session changed during final validation, failing");
                await MarkTerminalAsync(row, Failed, "session_changed",
                    "The bot reconnected during final validation. Review its live build and queue the change again.",
                    TerminalCas.Waiting, stoppingToken);
                return;
            }
            if (!string.Equals(
                    row.RotationFingerprint,
                    validation.RotationFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                CircuitTrace.Hit(row.Guid, "loadout: rotation fingerprint changed while waiting, failing");
                await MarkTerminalAsync(row, Failed, "rotation_changed",
                    "The selected custom rotation changed while this build was waiting. Review it and queue the build again.",
                    TerminalCas.Waiting, stoppingToken);
                return;
            }
        }
        catch (BotCombatLoadoutException ex)
        {
            CircuitTrace.HitNote(row.Guid, "loadout: final validation raised", ex.Code);
            if (TransientCodes.Contains(ex.Code))
            {
                CircuitTrace.Hit(row.Guid, "loadout: transient validation code, deferring");
                await DeferAsync(row, ex.Code, ex.Message, stoppingToken);
            }
            else
            {
                CircuitTrace.Hit(row.Guid, "loadout: permanent validation code, failing");
                await MarkTerminalAsync(
                    row, Failed, ex.Code, ex.Message,
                    TerminalCas.Waiting, stoppingToken);
            }
            return;
        }

        string requestId = Guid.NewGuid().ToString("N");
        if (!await TryClaimAsync(row, requestId, stoppingToken))
        {
            CircuitTrace.Hit(row.Guid, "loadout: waiting-row claim CAS lost, skipping");
            return;
        }

        row.Status = Dispatching;
        row.RequestId = requestId;
        row.ClaimOwner = _instanceId;
        row.AttemptCount++;
        await NotifyAsync(row.Guid, Dispatching,
            "The bot is safe; the queued build is being dispatched.", ToView(row), stoppingToken);

        try
        {
            BotCombatLoadoutApplyResult result = await _loadouts.ApplyAsync(
                row.Guid,
                request,
                stoppingToken,
                operationRequestId: requestId,
                expectedRotationFingerprint: row.RotationFingerprint,
                expectedSessionAt: row.ObservedSessionAt);
            await MarkTerminalAsync(
                row, Applied, result.Status, result.Message,
                TerminalCas.OwnedDispatch, stoppingToken);
        }
        catch (BotCombatLoadoutException ex) when (TransientCodes.Contains(ex.Code))
        {
            CircuitTrace.HitNote(row.Guid, "loadout: dispatch transient, returning to waiting", ex.Code);
            await ReturnToWaitingAsync(row, ex.Code, ex.Message, stoppingToken);
        }
        catch (BotCombatLoadoutException ex) when (ex.Code == "stale_revision")
        {
            CircuitTrace.Hit(row.Guid, "loadout: dispatch failed, revision stale at core");
            await MarkTerminalAsync(
                row, Failed, ex.Code, ex.Message,
                TerminalCas.OwnedDispatch, stoppingToken);
        }
        catch (BotCombatLoadoutException ex) when (
            ex.Code is "ack_timeout" or "outcome_unknown" or "bridge_send_failed" or "rotation_persistence_failed"
                or "ack_state_mismatch" or "rollback_failed" or "bot_offline")
        {
            CircuitTrace.HitNote(row.Guid, "loadout: dispatch uncertain after claim", ex.Code);
            await MarkTerminalAsync(row, Uncertain, ex.Code,
                ex.Message + " Automatic retry is disabled.",
                TerminalCas.OwnedDispatch, CancellationToken.None);
        }
        catch (BotCombatLoadoutException ex)
        {
            CircuitTrace.HitNote(row.Guid, "loadout: dispatch failed", ex.Code);
            await MarkTerminalAsync(
                row, Failed, ex.Code, ex.Message,
                TerminalCas.OwnedDispatch, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            CircuitTrace.Hit(row.Guid, "loadout: dispatch interrupted after claim");
            await MarkTerminalAsync(row, Uncertain, "dispatch_interrupted",
                "Dispatch was interrupted after it was claimed. Refresh live state; automatic retry is disabled.",
                TerminalCas.OwnedDispatch, CancellationToken.None);
            if (stoppingToken.IsCancellationRequested)
            {
                CircuitTrace.Hit(row.Guid, "loadout: shutdown cancellation absorbed");
                return;
            }
            throw;
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(row.Guid, "loadout: dispatch unexpected error, marking uncertain");
            _logger.LogError(ex,
                "[COMBAT-LOADOUT-QUEUE] unexpected dispatch failure for {Bot} ({Guid}); marking uncertain",
                row.BotName, row.Guid);
            await MarkTerminalAsync(row, Uncertain, "dispatch_uncertain",
                "An unexpected dispatch error occurred. Refresh live state; automatic retry is disabled.",
                TerminalCas.OwnedDispatch, CancellationToken.None);
        }
    }

    private async Task RecoverInterruptedDispatchesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = _db.Admin();
            await conn.OpenAsync(cancellationToken);
            int changed = await conn.ExecuteAsync(new CommandDefinition(@"
                UPDATE bot_combat_loadout_queue
                   SET status = 'uncertain', completed_at = CURRENT_TIMESTAMP(3),
                       last_code = 'dispatch_interrupted',
                       last_message = 'SuperUI restarted during dispatch. Automatic retry is disabled.'
                 WHERE status = 'dispatching'
                   AND (claim_expires_at IS NULL OR claim_expires_at <= CURRENT_TIMESTAMP(3))",
                cancellationToken: cancellationToken));
            if (changed > 0)
            {
                CircuitTrace.Hit(0, "loadout: interrupted dispatches recovered as uncertain", changed);
                _logger.LogWarning(
                    "[COMBAT-LOADOUT-QUEUE] recovered {Count} interrupted dispatch(es) as uncertain; none were retried",
                    changed);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CircuitTrace.Hit(0, "loadout: interrupted-row recovery failed, dispatcher fails closed");
            _logger.LogError(ex,
                "[COMBAT-LOADOUT-QUEUE] could not recover interrupted rows; dispatcher will fail closed until the queue table is available");
        }
    }

    private async Task RecoverStaleDispatchesAsync(CancellationToken cancellationToken)
    {
        await using var conn = _db.Admin();
        await conn.OpenAsync(cancellationToken);
        var rows = (await conn.QueryAsync<QueueRow>(new CommandDefinition(
            QueueSelect + @"
             WHERE status = 'dispatching'
               AND (claim_expires_at IS NULL OR claim_expires_at <= CURRENT_TIMESTAMP(3))
             ORDER BY dispatched_at
             LIMIT 32",
            cancellationToken: cancellationToken))).ToArray();

        foreach (QueueRow row in rows)
        {
            await MarkTerminalAsync(row, Uncertain, "dispatch_stale",
                "The claimed dispatch did not reach a durable result in time. Refresh live state; automatic retry is disabled.",
                TerminalCas.ExpiredDispatch, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<QueueRow>> LoadDueRowsAsync(CancellationToken cancellationToken)
    {
        await using var conn = _db.Admin();
        await conn.OpenAsync(cancellationToken);
        var rows = await conn.QueryAsync<QueueRow>(new CommandDefinition(
            QueueSelect + " WHERE status = 'waiting' AND next_attempt_at <= CURRENT_TIMESTAMP(3) ORDER BY next_attempt_at LIMIT 32",
            cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    private async Task<QueueRow?> LoadRowAsync(int guid, CancellationToken cancellationToken)
    {
        if (guid <= 0)
        {
            CircuitTrace.Hit(0, "loadout: queue lookup rejected, invalid guid");
            throw QueueError(400, "guid_invalid", "A positive bot guid is required.");
        }
        try
        {
            await using var conn = _db.Admin();
            await conn.OpenAsync(cancellationToken);
            return await conn.QueryFirstOrDefaultAsync<QueueRow>(new CommandDefinition(
                QueueSelect + " WHERE bot_guid = @guid",
                new { guid }, cancellationToken: cancellationToken));
        }
        catch (BotCombatLoadoutQueueException)
        {
            CircuitTrace.Hit(guid, "loadout: queue lookup error passthrough");
            throw;
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(guid, "loadout: queue storage unavailable");
            _logger.LogError(ex, "[COMBAT-LOADOUT-QUEUE] queue storage unavailable for guid={Guid}", guid);
            throw QueueError(503, "queue_unavailable",
                "The durable combat-loadout queue is unavailable. No request was queued.");
        }
    }

    private async Task<bool> TryClaimAsync(
        QueueRow row,
        string requestId,
        CancellationToken cancellationToken)
    {
        await using var conn = _db.Admin();
        await conn.OpenAsync(cancellationToken);
        int changed = await conn.ExecuteAsync(new CommandDefinition(@"
            UPDATE bot_combat_loadout_queue
               SET status = 'dispatching', request_id = @requestId,
                   claim_owner = @claimOwner,
                   claim_expires_at = DATE_ADD(CURRENT_TIMESTAMP(3), INTERVAL 45 SECOND),
                   attempt_count = attempt_count + 1,
                   dispatched_at = CURRENT_TIMESTAMP(3),
                   last_code = 'dispatching',
                   last_message = 'Dispatch claimed; awaiting the correlated core acknowledgement.'
             WHERE bot_guid = @guid AND queue_id = @queueId AND status = 'waiting'",
            new { requestId, claimOwner = _instanceId, guid = row.Guid, queueId = row.QueueId },
            cancellationToken: cancellationToken));
        return changed == 1;
    }

    private async Task DeferAsync(
        QueueRow row,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        await using var conn = _db.Admin();
        await conn.OpenAsync(cancellationToken);
        int changed = await conn.ExecuteAsync(new CommandDefinition(@"
            UPDATE bot_combat_loadout_queue
               SET next_attempt_at = DATE_ADD(CURRENT_TIMESTAMP(3), INTERVAL 2 SECOND),
                   last_code = @code,
                   last_message = @message
             WHERE bot_guid = @guid AND queue_id = @queueId AND status = 'waiting'",
            new
            {
                code,
                message,
                guid = row.Guid,
                queueId = row.QueueId
            }, cancellationToken: cancellationToken));
        if (changed == 1 && (!string.Equals(row.LastCode, code, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(row.LastMessage, message, StringComparison.Ordinal)))
        {
            CircuitTrace.HitNote(row.Guid, "loadout: defer reason changed, notifying", code);
            row.LastCode = code;
            row.LastMessage = message;
            row.NextAttemptAt = DateTime.Now + RetryDelay;
            await NotifyAsync(row.Guid, Waiting, message, ToView(row), cancellationToken);
        }
    }

    private async Task ReturnToWaitingAsync(
        QueueRow row,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        await using var conn = _db.Admin();
        await conn.OpenAsync(cancellationToken);
        int changed = await conn.ExecuteAsync(new CommandDefinition(@"
            UPDATE bot_combat_loadout_queue
               SET status = 'waiting', request_id = NULL,
                   claim_owner = NULL, claim_expires_at = NULL,
                   next_attempt_at = DATE_ADD(CURRENT_TIMESTAMP(3), INTERVAL 2 SECOND),
                   last_code = @code,
                   last_message = @message
             WHERE bot_guid = @guid AND queue_id = @queueId
               AND status = 'dispatching' AND claim_owner = @claimOwner",
            new
            {
                code,
                message,
                claimOwner = _instanceId,
                guid = row.Guid,
                queueId = row.QueueId
            }, cancellationToken: cancellationToken));
        if (changed != 1)
        {
            CircuitTrace.Hit(row.Guid, "loadout: return-to-waiting CAS lost");
            return;
        }
        row.Status = Waiting;
        row.RequestId = null;
        row.LastCode = code;
        row.LastMessage = message;
        row.NextAttemptAt = DateTime.Now + RetryDelay;
        await NotifyAsync(row.Guid, Waiting, message, ToView(row), cancellationToken);
    }

    private async Task<bool> MarkTerminalAsync(
        QueueRow row,
        string status,
        string code,
        string message,
        TerminalCas terminalCas,
        CancellationToken cancellationToken)
    {
        string predicate = terminalCas switch
        {
            TerminalCas.Waiting => CircuitTrace.Pass("AND status = 'waiting'",
                row.Guid, "loadout: terminal CAS on waiting row"),
            TerminalCas.OwnedDispatch => CircuitTrace.Pass("AND status = 'dispatching' AND claim_owner = @expectedClaimOwner",
                row.Guid, "loadout: terminal CAS on owned dispatch"),
            TerminalCas.ExpiredDispatch => CircuitTrace.Pass(@"AND status = 'dispatching'
                AND (claim_expires_at IS NULL OR claim_expires_at <= CURRENT_TIMESTAMP(3))
                AND ((claim_owner = @expectedClaimOwner)
                     OR (claim_owner IS NULL AND @expectedClaimOwner IS NULL))",
                row.Guid, "loadout: terminal CAS on expired dispatch"),
            _ => throw CircuitTrace.Pass(new ArgumentOutOfRangeException(nameof(terminalCas)),
                row.Guid, "loadout: terminal CAS enum out of range")
        };
        string? expectedClaimOwner = terminalCas == TerminalCas.OwnedDispatch
            ? _instanceId
            : row.ClaimOwner;

        await using var conn = _db.Admin();
        await conn.OpenAsync(cancellationToken);
        int changed = await conn.ExecuteAsync(new CommandDefinition(@"
            UPDATE bot_combat_loadout_queue
               SET status = @status,
                    completed_at = CURRENT_TIMESTAMP(3),
                   claim_expires_at = NULL,
                    last_code = @code,
                    last_message = @message
             WHERE bot_guid = @guid AND queue_id = @queueId
               " + predicate,
            new
            {
                status,
                code,
                message,
                guid = row.Guid,
                queueId = row.QueueId,
                expectedClaimOwner
            },
            cancellationToken: cancellationToken));
        if (changed != 1)
        {
            CircuitTrace.Hit(row.Guid, "loadout: terminal mark CAS lost, journal unchanged");
            return false;
        }
        row.Status = status;
        row.LastCode = code;
        row.LastMessage = message;
        row.CompletedAt = DateTime.UtcNow;
        BotCombatLoadoutQueueView? view = IsTerminalHidden(status) ? null : ToView(row);
        await NotifyAsync(row.Guid, status, message, view, cancellationToken);
        _logger.LogInformation(
            "[COMBAT-LOADOUT-QUEUE] {QueueId} for {Bot} ({Guid}) finished {Status}/{Code}: {Message}",
            row.QueueId, row.BotName, row.Guid, status, code, message);
        return true;
    }

    private async Task NotifyAsync(
        int guid,
        string status,
        string message,
        BotCombatLoadoutQueueView? queue,
        CancellationToken cancellationToken)
    {
        try
        {
            await _hub.Clients.All.SendAsync("BotCombatLoadoutQueueChanged", new
            {
                guid,
                status,
                message,
                queue
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CircuitTrace.Hit(guid, "loadout: queue notify cancelled at shutdown");
            // Database state is authoritative; shutdown must not roll back or
            // reclassify a transition merely because its UI broadcast stopped.
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(guid, "loadout: queue notify broadcast failed");
            _logger.LogWarning(ex,
                "[COMBAT-LOADOUT-QUEUE] SignalR notification failed for guid={Guid}, status={Status}",
                guid, status);
        }
    }

    private static BotCombatLoadoutQueueView ToView(QueueRow row)
        => new()
        {
            QueueId = row.QueueId,
            Status = row.Status,
            SpecTab = row.SpecTab,
            ProfileId = row.ProfileId,
            ProfileName = row.ProfileName,
            ActiveRole = row.ActiveRole,
            ActiveRoleName = row.ActiveRoleName,
            RotationMode = row.RotationMode,
            RotationProfile = row.RotationProfile,
            RotationName = row.RotationName,
            ResetTalents = row.ResetTalents,
            ExpectedRevision = row.ExpectedRevision,
            QueuedAtUtc = DatabaseLocalAsUtc(row.CreatedAt),
            UpdatedAtUtc = DatabaseLocalAsUtc(row.UpdatedAt),
            NextAttemptAtUtc = DatabaseLocalAsUtc(row.NextAttemptAt),
            AttemptCount = row.AttemptCount,
            LastCode = row.LastCode,
            LastMessage = row.LastMessage,
            CanCancel = row.Status is Waiting or Failed or Uncertain,
            CanReplace = row.Status is Waiting or Failed
        };

    private static bool IsTerminalHidden(string status)
        => status is Applied or Cancelled;

    private static bool SameSession(DateTime? observed, DateTime current)
        => observed.HasValue && Math.Abs((AsUtc(observed.Value) - AsUtc(current)).TotalMilliseconds) < 10;

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime DatabaseLocalAsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();

    private static string? CleanAuditValue(string? value, string? fallback, int maxLength)
    {
        string cleaned = string.IsNullOrWhiteSpace(value) ? fallback ?? "" : value.Trim();
        if (cleaned.Length > maxLength)
            cleaned = cleaned[..maxLength];   // cb:fold pure audit-string helper, no bot context
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static BotCombatLoadoutQueueException QueueError(int statusCode, string code, string message)
        => new(statusCode, code, message);

    private sealed class QueueRow
    {
        public int Guid { get; set; }
        public string BotName { get; set; } = "";
        public string QueueId { get; set; } = "";
        public string Status { get; set; } = Waiting;
        public string PayloadJson { get; set; } = "";
        public int SpecTab { get; set; }
        public string ProfileId { get; set; } = "";
        public string ProfileName { get; set; } = "";
        public int ActiveRole { get; set; }
        public string ActiveRoleName { get; set; } = "";
        public string RotationMode { get; set; } = "spec_default";
        public string? RotationProfile { get; set; }
        public string RotationName { get; set; } = "";
        public string? RotationFingerprint { get; set; }
        public bool ResetTalents { get; set; }
        public uint ExpectedRevision { get; set; }
        public DateTime? ObservedSessionAt { get; set; }
        public string? RequestId { get; set; }
        public string? ClaimOwner { get; set; }
        public DateTime? ClaimExpiresAt { get; set; }
        public int AttemptCount { get; set; }
        public string QueuedBy { get; set; } = "web";
        public string? QueuedFrom { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime NextAttemptAt { get; set; }
        public DateTime? DispatchedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? LastCode { get; set; }
        public string? LastMessage { get; set; }
        public bool IsExpired { get; set; }
    }

    private sealed class QueueIdentity
    {
        public string QueueId { get; set; } = "";
        public string Status { get; set; } = "";
    }
}

public sealed class BotCombatLoadoutQueueView
{
    public string QueueId { get; set; } = "";
    public string Status { get; set; } = "waiting";
    public int SpecTab { get; set; }
    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public int ActiveRole { get; set; }
    public string ActiveRoleName { get; set; } = "";
    public string RotationMode { get; set; } = "spec_default";
    public string? RotationProfile { get; set; }
    public string RotationName { get; set; } = "";
    public bool ResetTalents { get; set; }
    public uint ExpectedRevision { get; set; }
    public DateTime QueuedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastCode { get; set; }
    public string? LastMessage { get; set; }
    public bool CanCancel { get; set; }
    public bool CanReplace { get; set; }
}

public sealed class BotCombatLoadoutQueueMutationResult
{
    public string Status { get; set; } = "waiting";
    public string Message { get; set; } = "";
    public BotCombatLoadoutQueueView? Queue { get; set; }
}

public sealed class BotCombatLoadoutQueueException : Exception
{
    public BotCombatLoadoutQueueException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}
