using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using MangosSuperUI.BotLogic.Tracking;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services;

/// <summary>
/// Coordinates the web-facing combat build as one operation. SuperUI validates
/// and prepares the request, but the live core remains the only component that
/// may reset/replay talents and replace the active combat rotation.
/// </summary>
public sealed class BotCombatLoadoutService
{
    // The core commits live state and enqueues SaveToDB persistence before its ACK,
    // but a separately-opened web DB connection can briefly observe the old
    // playerbot/character_spell rows. Keep this well below the HTTP request budget
    // and never resend the mutation while waiting for that read model to catch up.
    private static readonly TimeSpan ReadModelConvergenceTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReadModelPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaximumRuntimeStateAge = TimeSpan.FromSeconds(15);

    private readonly ConnectionFactory _db;
    private readonly BotTalentVisibilityService _talents;
    private readonly RotationService _rotations;
    private readonly BotBridgeService _bridge;
    private readonly ILogger<BotCombatLoadoutService> _logger;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _botLocks = new();

    public BotCombatLoadoutService(
        ConnectionFactory db,
        BotTalentVisibilityService talents,
        RotationService rotations,
        BotBridgeService bridge,
        ILogger<BotCombatLoadoutService> logger)
    {
        _db = db;
        _talents = talents;
        _rotations = rotations;
        _bridge = bridge;
        _logger = logger;
    }

    public async Task<BotCombatLoadoutView> GetAsync(
        int guid,
        CancellationToken cancellationToken = default)
    {
        ManagedBotRow bot = await LoadManagedBotAsync(guid, cancellationToken);
        IReadOnlyList<BotTalentProfileOption> options = LoadProfileOptions(bot.ClassId);
        BotTalentVisibility talents = await _talents.GetAsync(guid, cancellationToken);

        BotState? runtime = _bridge.GetBotState(guid);
        bool online = _bridge.Connections.ContainsKey(guid);
        bool hasAssignment = _rotations.TryGetAssignment(bot.Name, out string assignedRotation);
        string rotationSource = NormalizeRuntimeRotationSource(online, runtime?.RotationSource);

        string blocker = !online ? "Bot is offline."
            : runtime?.HasReceivedState != true ? "Bot runtime is still hydrating."
            : DateTime.UtcNow - runtime.LastUpdate > MaximumRuntimeStateAge ? "Bot runtime state is stale."
            : runtime?.IsDead == true ? "Bot is dead."
            : runtime?.InCombat == true ? "Bot is in combat."
            : "";

        return new BotCombatLoadoutView
        {
            Guid = bot.Guid,
            Name = bot.Name,
            ClassId = bot.ClassId,
            ClassName = talents.ClassName,
            Level = bot.Level,
            Online = online,
            InCombat = runtime?.InCombat ?? false,
            IsDead = runtime?.IsDead ?? false,
            SpecTab = bot.SpecTab,
            ActiveRoleId = bot.ActiveRole,
            ActiveRole = talents.ActiveRole ?? new BotActiveRoleView
            {
                Id = bot.ActiveRole,
                Name = BotTalentVisibilityService.RoleName(bot.ActiveRole)
            },
            Profile = talents.Profile,
            Points = talents.Points,
            Trees = talents.Trees,
            NextPlannedPurchase = talents.NextPlannedPurchase,
            Compatibility = talents.Compatibility,
            ErrorCode = talents.ErrorCode,
            Error = talents.Error,
            TalentProfileState = online
                ? runtime?.TalentProfileState ?? "unchecked"
                : talents.Compatibility.Status,
            CombatConfigRevision = runtime?.CombatConfigRevision ?? 0,
            Talents = talents,
            ProfileOptions = options,
            AvailableProfiles = options.Select(ToAvailableProfile).ToArray(),
            CustomRotations = _rotations.GetProfileSummaries(),
            AvailableRotations = _rotations.GetProfileSummaries()
                .Select(p => new BotCombatRotationOption
                {
                    Id = p.Name,
                    Name = p.Name,
                    Description = p.Description,
                    InstructionCount = p.InstructionCount
                })
                .ToArray(),
            Rotation = new BotCombatRotationView
            {
                DesiredMode = hasAssignment ? "custom" : "spec_default",
                DesiredProfile = hasAssignment ? assignedRotation : "",
                EffectiveSource = rotationSource,
                EffectiveProfile = online ? runtime?.RotationProfile ?? "" : "",
                Source = rotationSource,
                Profile = online ? runtime?.RotationProfile ?? "" : "",
                PersistedProfile = hasAssignment ? assignedRotation : "",
                InstructionCount = online ? runtime?.RotationInstructionCount ?? 0 : 0,
                CastableCount = online ? runtime?.RotationCastableCount ?? 0 : 0,
                TalentProfileState = online ? runtime?.TalentProfileState ?? "unchecked" : "offline",
                Revision = runtime?.CombatConfigRevision ?? 0
            },
            CanApply = blocker.Length == 0,
            CanQueue = online,
            ApplyBlocker = blocker.Length == 0 ? null : blocker,
            ApplyBlockedReason = blocker.Length == 0 ? null : blocker,
            AsOfUtc = DateTime.UtcNow
        };
    }

    public async Task<BotCombatLoadoutApplyResult> ApplyAsync(
        int guid,
        BotCombatLoadoutRequest request,
        CancellationToken cancellationToken = default,
        string? operationRequestId = null,
        string? expectedRotationFingerprint = null,
        DateTime? expectedSessionAt = null)
    {
        var gate = _botLocks.GetOrAdd(guid, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!_bridge.Connections.TryGetValue(guid, out BotConnection? session))
            {
                CircuitTrace.Hit(guid, "loadout: apply rejected, bot offline");
                throw Error(409, "bot_offline", "The bot must be online to change its combat loadout.");
            }
            await _rotations.WaitForHelloHydrationAsync(session, cancellationToken);
            PreparedCombatLoadout plan = await PrepareRequestAsync(
                guid,
                request,
                requireReadyRuntime: true,
                cancellationToken);
            if (!ReferenceEquals(plan.Connection, session))
            {
                CircuitTrace.Hit(guid, "loadout: apply rejected, session changed during prepare");
                throw Error(409, "session_changed",
                    "The bot reconnected while its saved rotation was being restored. Refresh before applying this build.");
            }
            ManagedBotRow bot = plan.Bot;
            BotTalentProfileOption profile = plan.Profile;
            BotState runtime = plan.Runtime!;
            string mode = plan.Mode;
            RotationService.PreparedRotation? prepared = plan.PreparedRotation;
            if (expectedSessionAt.HasValue
                && Math.Abs((expectedSessionAt.Value - runtime.ConnectedAt).TotalMilliseconds) >= 10)
            {
                CircuitTrace.Hit(guid, "loadout: apply rejected, queued session mismatch");
                throw Error(409, "session_changed",
                    "The bot reconnected after this build was queued. Review its live state and queue the change again.");
            }
            string? actualRotationFingerprint = mode == "custom"
                ? Fingerprint(prepared!.WireData)
                : null;
            if (expectedRotationFingerprint != null
                && !string.Equals(
                    expectedRotationFingerprint,
                    actualRotationFingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                CircuitTrace.Hit(guid, "loadout: apply rejected, rotation fingerprint changed since queue");
                throw Error(409, "rotation_changed",
                    "The selected custom rotation changed after it was queued. Review it and queue the build again.");
            }

            string requestId = string.IsNullOrWhiteSpace(operationRequestId)
                ? Guid.NewGuid().ToString("N")
                : operationRequestId.Trim().ToLowerInvariant();
            if (!Guid.TryParseExact(requestId, "N", out _))
            {
                CircuitTrace.Hit(guid, "loadout: apply rejected, invalid request id format");
                throw Error(400, "request_id_invalid",
                    "Combat-loadout operation request ids must be GUIDs in N format.");
            }
            var command = new CombatLoadoutBridgeCommand
            {
                RequestId = requestId,
                // Never re-read the mutable BotState after optimistic validation;
                // doing so would silently rebase over an intervening writer.
                ExpectedRevision = request.ExpectedRevision!.Value,
                SpecTab = request.SpecTab,
                ActiveRole = request.ActiveRole,
                ResetTalents = request.ResetTalents,
                RotationMode = mode == "custom" ? "CUSTOM" : "SPEC",
                RotationProfile = prepared?.Name ?? "",
                RotationData = prepared?.WireData ?? ""
            };

            CombatLoadoutAck ack;
            // Keep reconnect hydration outside the ACK-to-assignment-commit
            // window. A new exact connection may publish while APPLY is in
            // flight, but its replay waits here and therefore reads either the
            // committed new assignment or the unchanged old one after failure.
            using (await _rotations.AcquireAssignmentGateAsync(guid, cancellationToken))
            {
                try
                {
                    ack = await _bridge.ApplyCombatLoadoutAsync(
                        guid, command, plan.Connection!, cancellationToken);
                }
                catch (CombatLoadoutOutcomeUnknownException ex)
                {
                    CircuitTrace.Hit(guid, "loadout: core outcome unknown after send");
                    throw Error(504, "outcome_unknown",
                        ex.Message + " Refresh live talents and rotation before making another change.");
                }
                catch (BotNotConnectedException ex)
                {
                    CircuitTrace.Hit(guid, "loadout: bot disconnected during apply");
                    throw Error(409, "bot_offline", ex.Message);
                }
                catch (CombatLoadoutAckTimeoutException ex)
                {
                    CircuitTrace.Hit(guid, "loadout: core ack timed out");
                    throw Error(504, "ack_timeout", ex.Message);
                }
                catch (IOException ex)
                {
                    CircuitTrace.Hit(guid, "loadout: bridge send failed, io error");
                    throw Error(409, "bridge_send_failed", $"The bot connection failed while sending the build: {ex.Message}");
                }
                catch (SocketException ex)
                {
                    CircuitTrace.Hit(guid, "loadout: bridge send failed, socket error");
                    throw Error(409, "bridge_send_failed", $"The bot connection failed while sending the build: {ex.Message}");
                }
                catch (ObjectDisposedException ex)
                {
                    CircuitTrace.Hit(guid, "loadout: bridge connection disposed mid-send");
                    throw Error(409, "bridge_send_failed", $"The bot connection closed while sending the build: {ex.Message}");
                }

                if (!ack.Success)
                {
                    CircuitTrace.Hit(guid, "loadout: core rejected the build");
                    throw CoreRejected(ack);
                }
                if (ack.SpecTab != request.SpecTab || ack.ActiveRole != request.ActiveRole)
                {
                    CircuitTrace.Hit(guid, "loadout: ack final build mismatch");
                    throw Error(409, "ack_state_mismatch",
                        $"The core acknowledged a different final build (spec {ack.SpecTab}, role {ack.ActiveRole}).");
                }

                try
                {
                    if (mode == "custom")
                    {
                        CircuitTrace.Hit(guid, "loadout: committing custom rotation assignment");
                        _rotations.CommitAssignmentWithoutPush(bot.Name, prepared!.Name);
                    }
                    else
                    {
                        CircuitTrace.Hit(guid, "loadout: clearing rotation assignment to spec default");
                        _rotations.ClearAssignmentWithoutPush(bot.Name);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CircuitTrace.Hit(guid, "loadout: rotation assignment persistence failed post-apply");
                    _logger.LogCritical(ex,
                        "[COMBAT-LOADOUT] core applied request {RequestId} to {Bot}, but rotation assignment persistence failed",
                        requestId, bot.Name);
                    throw Error(500, "rotation_persistence_failed",
                        "The core applied the build, but SuperUI could not persist its rotation assignment. Do not repeat the reset; refresh runtime state first.");
                }
            }

            ReadModelConvergence convergence = await WaitForReadModelAsync(
                guid,
                request,
                profile,
                ack,
                CancellationToken.None);

            if (!convergence.Converged)
            {
                CircuitTrace.Hit(guid, "loadout: read model still pending after apply");
                _logger.LogWarning(convergence.LastError,
                    "[COMBAT-LOADOUT] core applied request {RequestId} to {Bot}, but the persisted read model did not converge within {TimeoutMs} ms; last observation: {Observation}",
                    requestId,
                    bot.Name,
                    ReadModelConvergenceTimeout.TotalMilliseconds,
                    convergence.LastObservation);
            }

            return new BotCombatLoadoutApplyResult
            {
                RequestId = requestId,
                Ack = ack,
                Status = convergence.Converged ? "applied" : "applied_read_model_pending",
                Message = convergence.Converged
                    ? "Combat loadout applied."
                    : "The core applied the combat loadout, but the persisted talent view is still converging. Do not repeat the reset; refresh shortly.",
                ReadModelConverged = convergence.Converged,
                Current = convergence.Converged ? convergence.Current : null
            };
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Validate and normalize a request before it is persisted in the one-deep
    /// per-bot queue. Runtime safety blockers are deliberately allowed here;
    /// the exact same validation runs again immediately before dispatch.
    /// </summary>
    public async Task<BotCombatLoadoutQueueValidation> ValidateForQueueAsync(
        int guid,
        BotCombatLoadoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var gate = _botLocks.GetOrAdd(guid, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!_bridge.Connections.TryGetValue(guid, out BotConnection? session))
            {
                CircuitTrace.Hit(guid, "loadout: queue validation rejected, bot offline");
                throw Error(409, "bot_offline",
                    "The bot must be online so the queued build can be bound to its current live combat session.");
            }
            await _rotations.WaitForHelloHydrationAsync(session, cancellationToken);
            PreparedCombatLoadout plan = await PrepareRequestAsync(
                guid,
                request,
                requireReadyRuntime: false,
                cancellationToken);
            if (plan.Runtime == null || plan.Connection == null)
            {
                CircuitTrace.Hit(guid, "loadout: queue validation rejected, runtime missing");
                throw Error(409, "bot_offline",
                    "The bot must be online so the queued build can be bound to its current live combat session.");
            }
            if (!ReferenceEquals(plan.Connection, session))
            {
                CircuitTrace.Hit(guid, "loadout: queue validation rejected, session changed");
                throw Error(409, "session_changed",
                    "The bot reconnected while its saved rotation was being restored. Refresh before queueing this build.");
            }

            return new BotCombatLoadoutQueueValidation
            {
                Guid = plan.Bot.Guid,
                BotName = plan.Bot.Name,
                ClassId = plan.Bot.ClassId,
                ProfileId = plan.Profile.Id,
                ProfileName = plan.Profile.Name,
                ActiveRole = request.ActiveRole,
                ActiveRoleName = BotTalentVisibilityService.RoleName(request.ActiveRole),
                ObservedSessionAt = plan.Runtime.ConnectedAt,
                RotationName = plan.Mode == "custom"
                    ? plan.PreparedRotation!.Name
                    : $"{plan.Profile.Name} built-in",
                RotationFingerprint = plan.Mode == "custom"
                    ? Fingerprint(plan.PreparedRotation!.WireData)
                    : null,
                Request = new BotCombatLoadoutRequest
                {
                    ExpectedRevision = request.ExpectedRevision,
                    SpecTab = request.SpecTab,
                    ActiveRole = request.ActiveRole,
                    RotationMode = plan.Mode,
                    RotationProfile = plan.Mode == "custom" ? plan.PreparedRotation!.Name : null,
                    ResetTalents = request.ResetTalents,
                    ConfirmReset = request.ConfirmReset
                }
            };
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<PreparedCombatLoadout> PrepareRequestAsync(
        int guid,
        BotCombatLoadoutRequest request,
        bool requireReadyRuntime,
        CancellationToken cancellationToken)
    {
        ManagedBotRow bot = await LoadManagedBotAsync(guid, cancellationToken);
        BotTalentProfileOption profile = LoadProfileOptions(bot.ClassId)
            .SingleOrDefault(p => p.SpecTab == request.SpecTab)
            ?? throw Error(422, "profile_not_for_class",
                $"Specialization slot {request.SpecTab} is not available for class {bot.ClassId}.");

        if (request.ActiveRole is < 1 or > 4)
        {
            CircuitTrace.Hit(guid, "loadout: prepare rejected, active role out of range");
            throw Error(400, "active_role_invalid", "Active role must be between 1 and 4.");
        }
        if (!profile.AllowedRoles.Contains(request.ActiveRole))
        {
            CircuitTrace.Hit(guid, "loadout: prepare rejected, role not allowed by profile");
            throw Error(422, "role_not_allowed",
                $"Role {request.ActiveRole} ({BotTalentVisibilityService.RoleName(request.ActiveRole)}) is not allowed by {profile.Id}.");
        }

        string mode = NormalizeRotationMode(request.RotationMode);
        bool specChanged = bot.SpecTab != request.SpecTab;
        if (specChanged && !request.ResetTalents)
        {
            CircuitTrace.Hit(guid, "loadout: prepare rejected, spec change needs talent reset");
            throw Error(409, "talent_reset_required",
                $"Changing from specialization slot {bot.SpecTab} to {request.SpecTab} requires a talent rebuild.");
        }
        if (request.ResetTalents && !request.ConfirmReset)
        {
            CircuitTrace.Hit(guid, "loadout: prepare rejected, reset not confirmed");
            throw Error(409, "reset_confirmation_required",
                "Talent rebuild was requested but its destructive confirmation was not supplied.");
        }
        if (!request.ExpectedRevision.HasValue)
        {
            CircuitTrace.Hit(guid, "loadout: prepare rejected, expected revision missing");
            throw Error(400, "expected_revision_required",
                "The combat loadout revision shown by the page is required.");
        }

        bool online = _bridge.Connections.TryGetValue(guid, out BotConnection? connection);
        BotState? runtime = connection?.State;
        if (requireReadyRuntime && (!online || runtime == null))
        {
            CircuitTrace.Hit(guid, "loadout: prepare rejected, bot offline");
            throw Error(409, "bot_offline", "The bot must be online to change its combat loadout.");
        }
        if (requireReadyRuntime && !runtime!.HasReceivedState)
        {
            CircuitTrace.Hit(guid, "loadout: prepare rejected, runtime hydrating");
            throw Error(409, "runtime_hydrating",
                "The bot's first live state has not arrived yet. Refresh shortly before applying the build.");
        }
        if (requireReadyRuntime && DateTime.UtcNow - runtime!.LastUpdate > MaximumRuntimeStateAge)
        {
            CircuitTrace.Hit(guid, "loadout: prepare rejected, runtime stale");
            throw Error(409, "runtime_stale",
                "The bot's live state is stale. Refresh shortly before applying the build.");
        }
        if (runtime != null && request.ExpectedRevision.Value != runtime.CombatConfigRevision)
        {
            CircuitTrace.Hit(guid, "loadout: prepare rejected, revision stale", runtime.CombatConfigRevision);
            throw Error(409, "stale_revision",
                $"The bot's combat loadout changed from revision {request.ExpectedRevision.Value} to {runtime.CombatConfigRevision}. Refresh before applying.");
        }
        if (requireReadyRuntime && runtime!.IsDead)
        {
            CircuitTrace.Hit(guid, "loadout: prepare rejected, bot dead");
            throw Error(409, "bot_dead", "The bot must be alive before its combat loadout can be changed.");
        }
        if (requireReadyRuntime && runtime!.InCombat)
        {
            CircuitTrace.Hit(guid, "loadout: prepare rejected, bot in combat");
            throw Error(409, "bot_in_combat", "The bot must leave combat before its combat loadout can be changed.");
        }

        RotationService.PreparedRotation? prepared = null;
        if (mode == "custom")
        {
            CircuitTrace.Hit(guid, "loadout: preparing custom rotation");
            try
            {
                prepared = _rotations.PrepareForBot(
                    request.RotationProfile ?? "",
                    bot.ClassId,
                    request.SpecTab,
                    request.ActiveRole);
            }
            catch (RotationService.RotationValidationException ex)
            {
                CircuitTrace.HitNote(guid, "loadout: custom rotation validation failed", ex.Code);
                throw Error(422, ex.Code, ex.Message);
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.RotationProfile))
        {
            CircuitTrace.Hit(guid, "loadout: prepare rejected, rotation profile given with spec_default");
            throw Error(400, "rotation_profile_not_allowed",
                "rotationProfile must be empty when rotationMode is spec_default.");
        }

        return new PreparedCombatLoadout(bot, profile, runtime, connection, mode, prepared);
    }

    private IReadOnlyList<BotTalentProfileOption> LoadProfileOptions(int classId)
    {
        try
        {
            var profiles = _talents.GetProfileOptions(classId);
            if (profiles.Count != 3)
            {
                CircuitTrace.Hit(0, "loadout: talent catalog wrong profile count", profiles.Count);
                throw Error(409, "talent_catalog_unavailable",
                    $"Class {classId} resolved {profiles.Count} talent profiles; expected 3.");
            }
            return profiles;
        }
        catch (BotCombatLoadoutException)
        {
            CircuitTrace.Hit(0, "loadout: talent catalog error passthrough");
            throw;
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(0, "loadout: talent catalog unavailable");
            _logger.LogError(ex, "Combat loadout: talent catalog failed for class {ClassId}", classId);
            throw Error(409, "talent_catalog_unavailable",
                "The validated build-5875 talent catalog is unavailable.");
        }
    }

    /// <summary>
    /// Wait for a newly-opened database read to agree with the successful core
    /// ACK. This is a read-only stabilization loop: the destructive bridge command
    /// has already completed and is deliberately never retried here. A bounded
    /// miss is returned as a successful-but-pending projection, not a 504 that
    /// could encourage a caller to repeat the talent reset.
    /// </summary>
    private async Task<ReadModelConvergence> WaitForReadModelAsync(
        int guid,
        BotCombatLoadoutRequest request,
        BotTalentProfileOption expectedProfile,
        CombatLoadoutAck ack,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + ReadModelConvergenceTimeout;
        BotCombatLoadoutView? last = null;
        Exception? lastError = null;
        string lastObservation = "no read completed";

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                CircuitTrace.Hit(guid, "loadout: convergence budget exhausted before read");
                break;
            }

            try
            {
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                readTimeout.CancelAfter(remaining);
                last = await GetAsync(guid, readTimeout.Token);
                lastError = null;
                lastObservation = DescribeReadModel(last);
                if (ReadModelMatches(last, request, expectedProfile, ack))
                {
                    CircuitTrace.Hit(guid, "loadout: read model converged with ack");
                    return new ReadModelConvergence(true, last, lastObservation, null);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CircuitTrace.Hit(guid, "loadout: convergence read cancelled by caller");
                throw;
            }
            catch (OperationCanceledException ex)
            {
                CircuitTrace.Hit(guid, "loadout: convergence budget expired mid-read");
                // The private convergence budget expired. The core ACK is still a
                // success; surface an explicit pending projection below.
                lastError = ex;
                break;
            }
            catch (Exception ex)
            {
                CircuitTrace.Hit(guid, "loadout: convergence read failed, retrying read only");
                // A post-ACK read failure must not turn a committed destructive
                // operation into an apparent apply failure. Retry only the read.
                lastError = ex;
                lastObservation = $"read failed: {ex.GetType().Name}";
            }

            remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                CircuitTrace.Hit(guid, "loadout: convergence budget exhausted after read");
                break;
            }
            await Task.Delay(
                remaining < ReadModelPollInterval ? remaining : ReadModelPollInterval,
                cancellationToken);
        }

        return new ReadModelConvergence(false, last, lastObservation, lastError);
    }

    private static bool ReadModelMatches(
        BotCombatLoadoutView current,
        BotCombatLoadoutRequest request,
        BotTalentProfileOption expectedProfile,
        CombatLoadoutAck ack)
    {
        if (current.Error != null
            || current.SpecTab != ack.SpecTab
            || current.ActiveRoleId != ack.ActiveRole
            || current.ActiveRole.Id != ack.ActiveRole
            || current.Talents.SpecTab != ack.SpecTab
            || current.Talents.ActiveRole?.Id != ack.ActiveRole
            || !string.Equals(current.Profile?.Id, expectedProfile.Id, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(current.Talents.Profile?.Id, expectedProfile.Id, StringComparison.OrdinalIgnoreCase)
            || current.CombatConfigRevision != ack.Revision
            || current.Rotation.Revision != ack.Revision)
        {   // cb:fold pure read-model predicate without guid, convergence outcome probed at caller
            return false;
        }

        string expectedSource = NormalizeRuntimeRotationSource(true, ack.RotationSource);
        if (!string.Equals(current.Rotation.Source, expectedSource, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(current.Rotation.Profile, ack.RotationProfile, StringComparison.OrdinalIgnoreCase))
        {   // cb:fold pure read-model predicate without guid, convergence outcome probed at caller
            return false;
        }

        // A reset changes character_spell as well as playerbot metadata. Matching
        // only spec/role can therefore accept a split read containing the old
        // talent rows. The ACK's learned count plus the exact level-plan flag make
        // the reset response wait for both halves of the projection.
        if (request.ResetTalents
            && (current.Points?.Spent != ack.LearnedPoints
                || current.Talents.Points?.Spent != ack.LearnedPoints
                || !current.Compatibility.MatchesLevelPlan
                || !current.Talents.Compatibility.MatchesLevelPlan))
        {   // cb:fold pure read-model predicate without guid, convergence outcome probed at caller
            return false;
        }

        return true;
    }

    private static string DescribeReadModel(BotCombatLoadoutView current)
        => $"spec={current.SpecTab}/{current.Talents.SpecTab}, "
           + $"role={current.ActiveRoleId}/{current.Talents.ActiveRole?.Id}, "
           + $"profile={current.Profile?.Id ?? "(none)"}, "
           + $"points={current.Points?.Spent.ToString() ?? "(none)"}, "
           + $"matchesPlan={current.Compatibility.MatchesLevelPlan}, "
           + $"revision={current.CombatConfigRevision}";

    private async Task<ManagedBotRow> LoadManagedBotAsync(int guid, CancellationToken cancellationToken)
    {
        if (guid <= 0)
        {
            CircuitTrace.Hit(0, "loadout: bot lookup rejected, invalid guid");
            throw Error(400, "guid_invalid", "A positive bot guid is required.");
        }

        using var conn = _db.Characters();
        await conn.OpenAsync(cancellationToken);
        var bot = await conn.QueryFirstOrDefaultAsync<ManagedBotRow>(new CommandDefinition(@"
            SELECT c.guid AS Guid, c.name AS Name, c.`class` AS ClassId, c.level AS Level,
                   pb.spec_tab AS SpecTab, pb.active_role AS ActiveRole
            FROM characters c
            INNER JOIN playerbot pb ON pb.char_guid = c.guid
            WHERE c.guid = @Guid",
            new { Guid = guid }, cancellationToken: cancellationToken));

        return bot ?? throw Error(404, "bot_not_managed",
            $"Character {guid} is not a managed SuperUI bot.");
    }

    private static string NormalizeRotationMode(string? value)
    {
        string mode = (value ?? "").Trim().ToLowerInvariant();
        return mode switch
        {
            "spec_default" => CircuitTrace.Pass(mode, 0, "loadout: rotation mode spec_default"),
            "custom" => CircuitTrace.Pass(mode, 0, "loadout: rotation mode custom"),
            _ => throw CircuitTrace.Pass(Error(400, "rotation_mode_invalid",
                "rotationMode must be 'spec_default' or 'custom'."), 0, "loadout: rotation mode invalid")
        };
    }

    private static string NormalizeRuntimeRotationSource(bool online, string? value)
    {
        if (!online)
        {
            CircuitTrace.Hit(0, "loadout: rotation source resolved offline");
            return "offline";
        }
        string source = (value ?? "").Trim().ToLowerInvariant();
        return source switch
        {
            "custom" => CircuitTrace.Pass("custom", 0, "loadout: rotation source custom"),
            "spec" or "builtin" or "builtin_spec" => CircuitTrace.Pass("builtin_spec", 0, "loadout: rotation source builtin spec"),
            _ => CircuitTrace.Pass("legacy", 0, "loadout: rotation source legacy")
        };
    }

    private static string Fingerprint(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static BotCombatAvailableProfile ToAvailableProfile(BotTalentProfileOption profile)
        => new()
        {
            Id = profile.Id,
            SpecTab = profile.SpecTab,
            Name = profile.Name,
            Spec = profile.Spec,
            RolePolicy = profile.RolePolicy,
            GearPolicy = profile.GearPolicy,
            TreePoints = profile.TreePoints,
            AllowedRoles = profile.AllowedRoles
                .Select(role => new BotActiveRoleView
                {
                    Id = role,
                    Name = BotTalentVisibilityService.RoleName(role)
                })
                .ToArray()
        };

    private static BotCombatLoadoutException CoreRejected(CombatLoadoutAck ack)
    {
        string code = string.IsNullOrWhiteSpace(ack.Code) ? "apply_failed" : ack.Code.Trim().ToLowerInvariant();
        int status = code.Contains("timeout", StringComparison.Ordinal) ? 504
            : code.Contains("revision", StringComparison.Ordinal)
              || code.Contains("combat", StringComparison.Ordinal)
              || code.Contains("dead", StringComparison.Ordinal)
              || code.Contains("offline", StringComparison.Ordinal)
              || code.Contains("busy", StringComparison.Ordinal)
              || code.Contains("casting", StringComparison.Ordinal)
              || code.Contains("taxi", StringComparison.Ordinal)
              || code.Contains("teleport", StringComparison.Ordinal)
              || code.Contains("possess", StringComparison.Ordinal)
                ? 409
                : 422;
        return Error(status, code,
            $"The core rejected the combat loadout ({ack.Status}/{ack.Code}). No automatic retry was attempted.");
    }

    private static BotCombatLoadoutException Error(int statusCode, string code, string message)
        => new(statusCode, code, message);

    private sealed class ManagedBotRow
    {
        public int Guid { get; set; }
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Level { get; set; }
        public int SpecTab { get; set; } = 255;
        public int ActiveRole { get; set; }
    }

    private sealed record ReadModelConvergence(
        bool Converged,
        BotCombatLoadoutView? Current,
        string LastObservation,
        Exception? LastError);

    private sealed record PreparedCombatLoadout(
        ManagedBotRow Bot,
        BotTalentProfileOption Profile,
        BotState? Runtime,
        BotConnection? Connection,
        string Mode,
        RotationService.PreparedRotation? PreparedRotation);
}

public sealed class BotCombatLoadoutRequest
{
    /// <summary>
    /// Queue-only optimistic token. Direct applies ignore it; replacing a queued
    /// item must echo the queueId returned by the latest GET.
    /// </summary>
    public string? ExpectedQueueId { get; set; }
    public uint? ExpectedRevision { get; set; }
    public int SpecTab { get; set; } = -1;
    public int ActiveRole { get; set; }
    public string RotationMode { get; set; } = "spec_default";
    public string? RotationProfile { get; set; }
    public bool ResetTalents { get; set; }
    public bool ConfirmReset { get; set; }
}

public sealed class BotCombatLoadoutQueueValidation
{
    public int Guid { get; set; }
    public string BotName { get; set; } = "";
    public int ClassId { get; set; }
    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public int ActiveRole { get; set; }
    public string ActiveRoleName { get; set; } = "";
    public string RotationName { get; set; } = "";
    public string? RotationFingerprint { get; set; }
    public DateTime ObservedSessionAt { get; set; }
    public BotCombatLoadoutRequest Request { get; set; } = new();
}

public sealed class BotCombatLoadoutView
{
    public int Guid { get; set; }
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public string ClassName { get; set; } = "";
    public int Level { get; set; }
    public bool Online { get; set; }
    public bool InCombat { get; set; }
    public bool IsDead { get; set; }
    public int SpecTab { get; set; } = 255;
    public int ActiveRoleId { get; set; }
    public BotActiveRoleView ActiveRole { get; set; } = new();
    public BotTalentProfileView? Profile { get; set; }
    public BotTalentPointSummary? Points { get; set; }
    public IReadOnlyList<BotTalentTreeView> Trees { get; set; } = Array.Empty<BotTalentTreeView>();
    public BotNextTalentView? NextPlannedPurchase { get; set; }
    public BotTalentCompatibilityView Compatibility { get; set; } = new();
    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
    public string TalentProfileState { get; set; } = "unchecked";
    public uint CombatConfigRevision { get; set; }
    public BotTalentVisibility Talents { get; set; } = new();
    public IReadOnlyList<BotTalentProfileOption> ProfileOptions { get; set; } = Array.Empty<BotTalentProfileOption>();
    public IReadOnlyList<BotCombatAvailableProfile> AvailableProfiles { get; set; }
        = Array.Empty<BotCombatAvailableProfile>();
    public IReadOnlyList<RotationService.RotationProfileSummary> CustomRotations { get; set; }
        = Array.Empty<RotationService.RotationProfileSummary>();
    public IReadOnlyList<BotCombatRotationOption> AvailableRotations { get; set; }
        = Array.Empty<BotCombatRotationOption>();
    public BotCombatRotationView Rotation { get; set; } = new();
    public bool CanApply { get; set; }
    public bool CanQueue { get; set; }
    public string? ApplyBlocker { get; set; }
    public string? ApplyBlockedReason { get; set; }
    public BotCombatLoadoutQueueView? QueuedChange { get; set; }
    public DateTime AsOfUtc { get; set; }
}

public sealed class BotCombatAvailableProfile
{
    public string Id { get; set; } = "";
    public int SpecTab { get; set; }
    public string Name { get; set; } = "";
    public string Spec { get; set; } = "";
    public string RolePolicy { get; set; } = "";
    public string GearPolicy { get; set; } = "";
    public int[] TreePoints { get; set; } = Array.Empty<int>();
    public IReadOnlyList<BotActiveRoleView> AllowedRoles { get; set; } = Array.Empty<BotActiveRoleView>();
}

public sealed class BotCombatRotationOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int InstructionCount { get; set; }
}

public sealed class BotCombatRotationView
{
    public string DesiredMode { get; set; } = "spec_default";
    public string DesiredProfile { get; set; } = "";
    public string EffectiveSource { get; set; } = "unavailable";
    public string EffectiveProfile { get; set; } = "";
    // Compact aliases consumed by the Bots cockpit and future MSUIClient model.
    public string Source { get; set; } = "unavailable";
    public string Profile { get; set; } = "";
    public string PersistedProfile { get; set; } = "";
    public int InstructionCount { get; set; }
    public int CastableCount { get; set; }
    public string TalentProfileState { get; set; } = "unchecked";
    public uint Revision { get; set; }
}

public sealed class BotCombatLoadoutApplyResult
{
    public bool Success { get; set; } = true;
    public string Status { get; set; } = "applied";
    public string Message { get; set; } = "Combat loadout applied.";
    public string RequestId { get; set; } = "";
    public CombatLoadoutAck Ack { get; set; } = new();
    /// <summary>
    /// False means the core committed and acknowledged the operation, but a fresh
    /// database projection did not converge inside the bounded read-only wait.
    /// Callers must refresh; they must not repeat a destructive reset automatically.
    /// </summary>
    public bool ReadModelConverged { get; set; } = true;
    public BotCombatLoadoutView? Current { get; set; }
}

public sealed class BotCombatLoadoutException : Exception
{
    public BotCombatLoadoutException(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public int StatusCode { get; }
    public string Code { get; }
}
