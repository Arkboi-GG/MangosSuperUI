# AiBotDoctrineTeam

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AiBotDoctrineTeam

**Purpose & Responsibilities**

`AiBotDoctrineTeam` implements the **TeamAuto** engagement doctrine for AI bots in World of Warcraft Mangos. It is the central decision-making engine for bots operating under an active `CombatDirective` within a group, specifically handling target selection, pull discipline, and combat maintenance to ensure coordinated group behavior.

Its primary responsibilities are:
1.  **Unified Target Resolution:** Acting as the single authority for target selection to prevent "flapping" (rapidly switching targets due to conflicting distance rules) and "splitting" (team members attacking different mobs after a kill).
2.  **Anchor-Follower Discipline:** Enforcing a strict hierarchy where one player (the Anchor) is responsible for pulling and initiating combat, while followers assist and maintain focus on the Anchor's target.
3.  **Kill-Boundary Continuity:** Implementing a "Chain" mechanism that pre-selects the next target while the current one is alive, ensuring zero downtime between kills and preventing the team from scattering when the current target dies.
4.  **Pull Restraint:** Preventing followers from independently pulling enemies during idle grinding states (`GroupGrind`) unless the Anchor is absent or dead, thereby maintaining group cohesion.

This unit replaces scattered logic previously distributed across `AiBotAIGrind`, `AiBotAICombat`, and `AiBotAIMain`, consolidating group-fight decisions into a single, stateful object. When the bot switches modes (e.g., to Solo), this object is destroyed, automatically resetting all associated state.

## Member-by-Member Behavior

### Target Acquisition & Pull Discipline

**AcquireTarget**
This method determines the initial target for a bot outside of active combat or when seeking a new engagement. It distinguishes between the **Anchor** (the designated puller) and **Followers**.
*   **Followers:** If a team focus exists (via `ResolveFocus`), the follower adopts it. If no focus exists, the follower checks if it is under an "entry-0" task (general grinding). In this state, if the Anchor is alive and nearby, the follower **holds** indefinitely, refusing to pull. This prevents multiple bots from pulling separate mobs simultaneously. If the Anchor is absent, dead, or the task is an objective (non-zero entry), the follower waits for a bounded duration (`AIBOT_ASSIST_PULL_HOLD_TICKS`) before falling back to independent scanning.
*   **Anchor:** If the bot is the Anchor, it bypasses the hold logic and proceeds to scan for targets using `ScanApproachTarget` (for movement tasks) or `SelectGrindTarget` (for grinding tasks).

**HoldPull**
Delegates to `AiBotAI.Combat/OverpullGuard`. This enforces a group density cap. Since followers do not pull, the Anchor's pull represents the entire team's engagement. If the target is surrounded by too many other enemies (high density), the Anchor will hold off pulling to avoid engaging an unmanageable cluster.

**MaintainTarget**
Called during active combat to decide whether to switch targets.
*   It first attempts to resolve a team focus via `ResolveFocus`.
*   If no team focus is available (e.g., the Anchor's target just died), a **Follower** will **keep** its current victim if it is still valid. This prevents the "split" where followers immediately drop a dying mob to find a new one, potentially scattering the team.
*   The **Anchor**, however, returns `nullptr` if its victim is dead, signaling the spine to perform a legacy re-pick (which triggers the Chain logic in `ResolveFocus`).

### Focus Resolution & Chain Logic

**ResolveFocus**
The core algorithm for determining the "correct" target for the team. It operates in rungs of priority:
1.  **Anchor Self-Commit:** If the bot is the Anchor, it commits to its current victim until death. While fighting, it calls `RefreshQueuedNext` to pre-select the next target.
2.  **Chain Preemption (Anchor Only):** If the Anchor's victim dies, it checks for a new target in this order:
    *   Any unit currently attacking the Anchor (preempts the queue).
    *   The pre-selected "queued next" target (from `RefreshQueuedNext`).
    *   `nullptr` (deferring to legacy pick).
    *   *Constraint:* This chain is suppressed if `TeamNeedsRecovery` returns true (any group member is below 40% HP/Mana).
3.  **Follower Anchor Victim:** If the bot is a follower, it adopts the Anchor's current victim if valid and in range.
4.  **Follower Anchor Attacker:** If the Anchor has no victim (just killed one), the follower assists whoever is attacking the Anchor. This ensures all followers converge on the same new threat.
5.  **Sticky Bridge:** If no live target is found, the follower continues attacking the last assisted target for a limited number of ticks (`AIBOT_ASSIST_STICKY_MAX_TICKS`) to bridge the gap between kills.

**ClearSticky**
Resets the sticky assist state (`m_lastAssistedVictimGuid` and `m_assistStickyTicks`), forcing the bot to look for a fresh target on the next resolution cycle.

### Configuration & State Queries

**UseStalemateBreaker**, **UseOverpullRetreat**, **UseTapRespect**
These methods return boolean flags configuring shared combat machinery:
*   `UseStalemateBreaker`: **True**. Enables logic to break out of stuck combat states.
*   `UseOverpullRetreat`: **True**. Enables retreat logic if the team pulls too many enemies.
*   `UseTapRespect`: **False**. Directive-active bots ignore tap restrictions, prioritizing team directives over individual loot rights.

**HoldingForTeam**
Returns `true` if the bot is currently suppressing its own pull/acquisition logic to wait for the Anchor. Used by external systems to understand why a bot might be idle despite enemies being present.

**Name**
Returns `"TeamAuto"`, identifying this doctrine instance.

**MakeTeamDoctrine**
A factory function that constructs and returns a `std::unique_ptr<IEngagementDoctrine>` containing a new `AiBotDoctrineTeam` instance. Called by `AiBotDoctrine/MakeDoctrine`.

## Cross-Unit Boundaries

*   **AiBotAI.Grind/ScanApproachTarget & SelectGrindTarget:**
    *   *Direction:* Called by `AcquireTarget` and `RefreshQueuedNext`.
    *   *Purpose:* To scan the environment for valid grind targets. `SelectGrindTarget` is called with a `pExcept` parameter in `RefreshQueuedNext` to exclude the current target, enabling the "Chain" pre-selection.
*   **AiBotAI.Main/GetBotPlayer:**
    *   *Direction:* Called by `AcquireTarget`, `MaintainTarget`, `ResolveFocus`, and `FindAnchorPlayer`.
    *   *Purpose:* Retrieves the `Player*` pointer for the bot to check its state, position, and group membership.
*   **CombatDirective/IsActive:**
    *   *Direction:* Called by `AcquireTarget` and `ResolveFocus`.
    *   *Purpose:* Checks if a combat directive is currently active, determining if the bot is in team mode.
*   **AiBotAI.Combat/OverpullGuard:**
    *   *Direction:* Called by `HoldPull`.
    *   *Purpose:* Evaluates enemy density to decide if a pull is safe.
*   **AiBotAI.Combat/IsValidAssistTarget:**
    *   *Direction:* Called by `MaintainTarget` and `ResolveFocus`.
    *   *Purpose:* Validates whether a specific unit is a legitimate target for assistance (alive, hostile, etc.).
*   **Group/GetFirstMember & GroupReference/next:**
    *   *Direction:* Called by `ResolveFocus` (via `FindAnchorPlayer` and `TeamNeedsRecovery`).
    *   *Purpose:* Iterates through the group members to locate the Anchor player or check health/mana levels of teammates.
*   **Player.Main/GetGroup:**
    *   *Direction:* Called by `ResolveFocus`.
    *   *Purpose:* Retrieves the `Group` object to access member lists.
*   **Player.Main/GetName:**
    *   *Direction:* Called by `AcquireTarget`, `ResolveFocus`, and `TraceAnchorFocus`.
    *   *Purpose:* Logs the bot's name for debugging and trace output.
*   **Unit.Main/GetVictim:**
    *   *Direction:* Called by `ResolveFocus`.
    *   *Purpose:* Retrieves the current target of the Anchor or the bot itself.
*   **Unit.Main/GetAttackerForHelper:**
    *   *Direction:* Called by `ResolveFocus`.
    *   *Purpose:* Identifies who is currently attacking the Anchor, used for kill-boundary convergence.
*   **Map.Main/GetCreature:**
    *   *Direction:* Called by `ResolveFocus` and `RefreshQueuedNext`.
    *   *Purpose:* Resolves a GUID to a `Creature*` object to validate if a queued or sticky target still exists in the world.
*   **WorldObject.Object/GetMap:**
    *   *Direction:* Called by `ResolveFocus`.
    *   *Purpose:* Gets the map object to query creatures.
*   **WorldObject.Object/IsWithinDist:**
    *   *Direction:* Called by `AcquireTarget`, `ResolveFocus`, and `RefreshQueuedNext`.
    *   *Purpose:* Checks spatial proximity to ensure targets and anchors are within valid engagement range.
*   **Object/GetGUIDLow, GetObjectGuid, IsInWorld:**
    *   *Direction:* Called by various methods.
    *   *Purpose:* Basic object identity and existence checks.
*   **Log.Main/Out:**
    *   *Direction:* Called by `AcquireTarget`, `ResolveFocus`, and `TraceAnchorFocus`.
    *   *Purpose:* Emits debug logs for hold states, focus changes, and chain activations.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory game objects (`Player`, `Creature`, `Group`) and internal state variables.

## Notable Implementation Details

1.  **The "One-Picker" Fix:**
    Previously, followers could independently pull during idle grinding (`GroupGrind`), leading to team fragmentation. `AcquireTarget` now explicitly blocks followers from pulling if the Anchor is alive and nearby during entry-0 tasks. This forces the Anchor to be the sole initiator of combat.

2.  **Kill-Boundary Convergence (Rung 2):**
    When the Anchor's target dies, there is a brief moment where the Anchor has no victim. Old logic caused followers to lose focus and pick random nearest targets. `ResolveFocus` now checks `GetAttackerForHelper` on the Anchor. If an enemy is attacking the Anchor, all followers immediately assist that enemy, ensuring the team stays focused on the same threat even during the transition.

3.  **Chain Queue (Pre-Election):**
    To eliminate downtime between kills, the Anchor pre-selects the next target while still fighting the current one (`RefreshQueuedNext`). This uses a throttled scan (every 4 ticks) to avoid performance overhead. When the current target dies, the Anchor immediately adopts the queued target (if valid) or an attacker, rather than starting a new scan.

4.  **Recovery Suppression:**
    The Chain queue is suppressed if any group member is below 40% HP or Mana (`TeamNeedsRecovery`). This aligns with the bot's "eat" (food/drink) threshold. If the team needs recovery, the bot pauses chaining to allow for healing/eating, preventing a situation where the bot tries to pull a new mob while the team is starving/healing.

5.  **State Encapsulation:**
    All transient state (sticky targets, hold ticks, chain queues) is stored as private members of `AiBotDoctrineTeam`. This ensures that when the doctrine is swapped out (e.g., leaving a group), the state is automatically cleaned up, avoiding residual "ghost" targets or holds.

6.  **Build Gate Dependency:**
    `RefreshQueuedNext` calls `bot.SelectGrindTarget(current)`, relying on an overload of `SelectGrindTarget` that accepts a `Unit*` to exclude. This overload must exist in `AiBotAIGrind`; otherwise, compilation will fail.

## Member Reference

**AcquireTarget**: Determines the initial target for the bot. Followers hold if the Anchor is alive and nearby during idle tasks; Anchors scan for targets. Resets hold counters if a focus is found.

**HoldPull**: Delegates to `AiBotAI.Combat/OverpullGuard` to enforce group density caps on pulls.

**MaintainTarget**: During combat, returns the team focus if available. Followers keep their current victim if no team focus exists (preventing split); Anchors return nullptr to trigger re-pick/chain.

**UseStalemateBreaker**: Returns `true`, enabling stalemate breaking logic.

**UseOverpullRetreat**: Returns `true`, enabling overpull retreat logic.

**UseTapRespect**: Returns `false`, disabling tap respect for directive-active bots.

**HoldingForTeam**: Returns `true` if the bot is currently suppressing its own actions to wait for the Anchor.

**Name**: Returns the string `"TeamAuto"`.

**ResolveFocus**: The core target resolution algorithm. Prioritizes: Anchor's current victim -> Anchor's attacker (kill-boundary) -> Sticky last target. Handles Anchor self-commitment and chain preemption.

**ClearSticky**: Resets the sticky assist GUID and tick counter.

**MakeTeamDoctrine**: Factory function creating a new `AiBotDoctrineTeam` instance wrapped in a `unique_ptr`.

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotDoctrineTeam

*Source:* AiBotDoctrineTeam.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AcquireTarget | method | AiBotAI.Grind/ScanApproachTarget, AiBotAI.Grind/SelectGrindTarget, AiBotAI.Main/GetBotPlayer, CombatDirective/IsActive, Log.Main/Out, Object/GetGUIDLow, Player.Main/GetName | — | — |
| HoldPull | method | AiBotAI.Combat/OverpullGuard | — | — |
| MaintainTarget | method | — | — | — |
| UseStalemateBreaker | method | — | — | — |
| UseOverpullRetreat | method | — | — | — |
| UseTapRespect | method | — | — | — |
| HoldingForTeam | method | — | — | — |
| Name | method | — | — | — |
| ResolveFocus | method | AiBotAI.Combat/IsValidAssistTarget, AiBotAI.Main/GetBotPlayer, CombatDirective/IsActive, Group/GetFirstMember, GroupReference/next, Log.Main/Out, Map.Main/GetCreature, Object/GetGUIDLow, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/IsEmpty, ObjectGuid/ObjectGuid, ObjectGuid/operator!=, Player.Main/GetGroup, Player.Main/GetName, Unit.Main/GetVictim, WorldObject.Object/GetMap, WorldObject.Object/GetName, WorldObject.Object/IsWithinDist | — | — |
| ClearSticky | method | ObjectGuid/Clear | — | — |
| MakeTeamDoctrine | function | — | AiBotDoctrine/MakeDoctrine | — |
