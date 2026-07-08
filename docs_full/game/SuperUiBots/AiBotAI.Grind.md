<!-- provenance: boundary-bleed -->
# AiBotAI.Grind

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AiBotAI.Grind

## Purpose & Responsibilities

This translation unit (`AiBotAIGrind.cpp`) implements the **area-grind behavior** for the autonomous AI bot. It is responsible for selecting valid combat targets during a `TASK_GRIND` phase, managing idle patrol movement when no targets are available, and handling the transition from navigation (`MOVE_TO`) to active grinding when the bot arrives at a quest location.

The core responsibility is **target acquisition logic** that balances efficiency (nearest target) with survival (avoiding dense packs, skipping non-XP mobs, and respecting level constraints). It supports two distinct grind modes:
1.  **Objective Grind:** Killing specific creature entries required for a quest.
2.  **Indefinite Grind:** Killing any valid hostile creature for experience points (XP), with preferences for level-appropriate targets.

This unit is doctrine-agnostic; it provides the raw scanning and selection primitives used by both solo and team-based engagement doctrines (defined in other units like `AiBotDoctrineTeam`).

## Member-by-Member Behavior

### Target Selection and Scanning

**`SelectGrindTarget`**
This is the central method for determining the next combat target during a grind. It operates in three priority tiers:
1.  **Priority 1 (Aggroed Targets):** Checks the bot's threat list for any existing enemies. If a target is already attacking the bot and matches the current task's objective (or is any hostile in indefinite mode), it is selected immediately. This ensures the bot finishes fights it has already started.
2.  **Priority 2a (Objective Rescan):** If the task requires specific creature entries (`creatureEntry != 0`), the method performs an escalating radial scan (50, 100, 150, 200 yards) around the bot. It searches for the primary objective entry or any tied alternate entries (e.g., different wolf types dropping the same item). If no objective mobs are found within 200 yards, it falls back to a "filler" scan within 50 yards for any hostile creature to maintain momentum and reset quest timers.
3.  **Priority 2b (Indefinite XP Grind):** If no specific entry is required, the method scans for the nearest valid XP-yielding mob within `AIBOT_GRIND_SCAN_YARDS` (100 yards). It applies strict filters:
    *   Skips critters (no XP).
    *   Skips "grey" mobs (level too low for XP).
    *   Skips "red" mobs (level too high, risk of death).
    *   **Level Band Preference:** Prefers mobs within `[Level-2, Level+1]`. Only if no such mobs exist does it consider mobs in the `[Level+2, Level+3]` range.
    *   Selects the nearest mob within the preferred band, or the nearest fallback mob if the band is empty.

The method accepts an optional `pExcept` parameter to exclude a specific unit (used by team doctrines to pre-select the *next* target while currently fighting one).

**`ScanApproachTarget`**
Used when the bot is moving toward a grind location (`TASK_MOVE_TO` transitioning to grind). Instead of waiting to arrive, it scans for valid targets *en route*.
*   It builds a "valid-kill union" of creature entries from:
    1.  The current task's primary objective.
    2.  The current task's alternate entries.
    3.  Any incomplete quests in the bot's log that require kills of specific creatures.
*   It scans within a radius (up to 60 yards or the task radius) for the nearest alive, untapped, hostile creature matching any entry in this union.
*   Unlike `SelectGrindTarget`, it does **not** require Line of Sight (LOS), assuming the bot can path to the target.

**`CountNearbyHostiles`**
A helper method that calculates the "density" of hostiles around a candidate target.
*   It counts alive, hostile, untapped creatures within a specified radius of the candidate.
*   Crucially, it filters for units that are **hostile to the bot** (`IsHostileTo`). This prevents neutral creatures (which can be attacked but do not aggro neighbors) from inflating the count, ensuring the bot doesn't avoid pulling a mob simply because neutral bystanders are nearby.
*   This count is used by `SelectGrindTarget` to penalize targets in dense packs, encouraging the bot to pick isolated mobs to avoid overpulling.

### Movement and State Transition

**`DoGrindPatrol`**
Executes idle movement when the bot is in a grind state but has no target.
*   Checks if the bot is already moving, in combat, or has an active wander timer.
*   Generates a random point within the task's radius around the task center.
*   Snaps the point to valid terrain and issues a movement command (`MovePointRun`) with the `AIBOT_POINT_GRIND_PATROL` ID.
*   Sets a short wander timer (3–6 seconds) to prevent spamming movement commands.

**`ConvertMoveToGrindInPlace`**
Handles the transition from navigating to a location to actively grinding there.
*   Changes the task type to `TASK_GRIND`.
*   Updates the task's center coordinates (`x, y, z`) to the bot's **current position**. This effectively "re-centers" the grind area on where the bot actually arrived, rather than the original dispatched coordinate.
*   Ensures the radius is at least 10 yards (defaults to 40 if smaller).
*   Clears any stored pathing data via `AiBotAI.Movement/ClearStoredPath`.
*   Logs the conversion for debugging.

### Helper Functions

**`AiBotGrayLevel`**
A static utility function that calculates the minimum creature level that yields XP for a player of a given level, based on Vanilla WoW 1.12 formulas. It is used by `SelectGrindTarget` to filter out grey mobs.

## Cross-Unit Boundaries

*   **Called by `AiBotAI.Combat/OverpullGuard`:**
    `CountNearbyHostiles` is called by the combat unit to determine if a potential target is surrounded by too many enemies. If the count exceeds the solo/group cap, the bot refuses to engage to prevent death spirals.

*   **Called by `AiBotAI.Main/UpdateAI`:**
    `SelectGrindTarget` and `DoGrindPatrol` are invoked by the main update loop. `UpdateAI` checks if the bot is in a grind state and needs a target or idle movement. `ScanApproachTarget` is also called here during movement phases to allow early engagement.

*   **Called by `AiBotDoctrineSolo/AcquireTarget` and `AiBotDoctrineTeam/AcquireTarget`:**
    Both solo and team doctrines rely on `SelectGrindTarget` and `ScanApproachTarget` to find targets. The team doctrine may pass the `pExcept` parameter to `SelectGrindTarget` to facilitate target switching chains.

*   **Calls into `AiBotAI.Movement/MovePointRun` and `AiBotAI.Movement/ClearStoredPath`:**
    `DoGrindPatrol` and `ConvertMoveToGrindInPlace` interact with the movement unit to issue pathing commands and clear previous paths.

*   **Calls into `CombatBotBaseAI/IsValidHostileTarget`:**
    All scanning methods use this base class method to verify that a unit is a valid combat target (e.g., not immune, not dead, not a player in certain contexts).

*   **Calls into `AiBotTaskData/MatchesObjectiveEntry`:**
    `SelectGrindTarget` uses this method to check if a creature's entry matches the current quest objective or its alternates.

## Data Model

This unit does not directly access database tables. It operates entirely on in-memory game objects (`Unit`, `Creature`, `Player`) and the bot's internal state (`m_currentTask`, `m_combatDirective`). Quest data is retrieved from the server's object manager (`ObjectMgr`) via `GetQuestTemplate`, but this is a runtime cache lookup, not a direct SQL query within this unit.

## Notable Implementation Details

1.  **Neutral Faction Handling in Density Checks:**
    In `CountNearbyHostiles`, the code explicitly checks `me->IsHostileTo(pUnit)`. This is a critical fix to prevent "neutral inflation." Without this, a mob standing next to neutral sheep would appear to be in a dense pack, causing the bot to avoid it. Since neutrals don't aggro neighbors, they shouldn't count toward pull risk.

2.  **Level Band Preference for Indefinite Grind:**
    `SelectGrindTarget` implements a two-tier selection for indefinite grinding. It first finds the nearest mob in the "comfortable" band (`[L-2, L+1]`). Only if that set is empty does it consider the "harder" band (`[L+2, L+3]`). This prevents the bot from unnecessarily engaging higher-level mobs when easier ones are available, reducing death rates.

3.  **Alternate Entry Support (Wolf-Meat Fix):**
    Both `SelectGrindTarget` and `ScanApproachTarget` support `altCreatureEntries`. This allows quests that require items dropped by multiple creature types (e.g., "Wolf Meat" from Young Wolf or Timber Wolf) to be treated as equivalent objectives. The code merges these entries into a single search set.

4.  **LOS Relaxation for Approach Scans:**
    `ScanApproachTarget` does not require Line of Sight. This allows the bot to engage targets that are behind walls or terrain features, relying on the pathfinding system (`MovePointRun`) to navigate around obstacles. This contrasts with some older logic that required visual confirmation.

5.  **Filler Kill Fallback:**
    In `SelectGrindTarget`, if the primary objective mobs are not found within 200 yards, the bot scans for *any* hostile within 50 yards. This "filler" kill resets the quest timer and keeps the bot active, even though it doesn't advance the specific kill count. The kill credit logic in `UpdateAI` (another unit) ensures this doesn't falsely complete the quest.

6.  **Chain Queue Exclusion:**
    The `pExcept` parameter in `SelectGrindTarget` is vital for team play. It allows the team doctrine to ask, "Who should we fight *after* this current target?" by excluding the current victim from the scan. This enables smooth target transitions without re-engaging the same mob.

## Member Reference

**`CountNearbyHostiles`**
Counts alive, hostile, untapped creatures within a radius of a candidate, excluding the candidate itself. Filters for units hostile to the bot to avoid neutral inflation. Used for overpull prevention.

**`AiBotGrayLevel`**
Static helper calculating the minimum creature level that yields XP for a given player level, based on Vanilla WoW formulas.

**`SelectGrindTarget`**
Primary target selector for grind tasks. Prioritizes aggroed targets, then objective-specific mobs (with escalating radius), then indefinite XP mobs (with level band preference). Supports exclusion of a specific unit (`pExcept`) for chain-queuing.

**`DoGrindPatrol`**
Generates random idle movement within the grind radius when no target is available. Snaps to terrain and issues a movement command.

**`ScanApproachTarget`**
Scans for valid quest-related targets while moving to a location. Builds a union of valid entries from the current task and incomplete quests. Does not require LOS.

**`ConvertMoveToGrindInPlace`**
Transitions the bot from movement to grind mode by re-centering the task area on the bot's current position and clearing stored paths.

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotAI.Grind

*Source:* AiBotAIGrind.cpp, AiBotAIMain.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CountNearbyHostiles | method | AnyUnfriendlyUnitInObjectRangeCheck/AnyUnfriendlyUnitInObjectRangeCheck, CombatBotBaseAI/IsValidHostileTarget, Creature.Main/IsTappedBy, Object/HasFlag, Object/IsCreature, Unit.Main/IsAlive, Unit.Main/IsHostileTo | AiBotAI.Combat/OverpullGuard, AiBotAI.Main/UpdateAI | — |
| AiBotGrayLevel | function | — | — | — |
| SelectGrindTarget | method | AiBotAI.Combat/IsCombatIgnored, AiBotTaskData/MatchesObjectiveEntry, AnyUnfriendlyUnitInObjectRangeCheck/AnyUnfriendlyUnitInObjectRangeCheck, CombatBotBaseAI/IsValidHostileTarget, Creature.Main/GetCreatureInfo, Creature.Main/IsTappedBy, HostileReference/next, HostileRefManager/getFirst, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Object/HasFlag, Object/IsCreature, Player.Main/GetName, ThreatManager/getSourceUnit, Unit.Main/GetHostileRefManager, Unit.Main/GetLevel, Unit.Main/IsAlive, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/GetDistance#3, WorldObject.Object/IsWithinDist, WorldObject.Object/IsWithinLOSInMap | AiBotDoctrineSolo/AcquireTarget, AiBotDoctrineTeam/AcquireTarget | — |
| DoGrindPatrol | method | AiBotAI.Movement/MovePointRun, Creature.MotionMaster/GetCurrentMovementGeneratorType, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/IsInCombat, WorldObject.Object/GetRandomPoint, WorldObject.Object/IsMoving | AiBotAI.Main/UpdateAI | — |
| ScanApproachTarget | method | AiBotAI.Combat/IsCombatIgnored, AnyUnfriendlyUnitInObjectRangeCheck/AnyUnfriendlyUnitInObjectRangeCheck, CombatBotBaseAI/IsValidHostileTarget, Creature.Main/IsTappedBy, Object/GetEntry, Object/GetGUIDLow, Object/HasFlag, Object/IsCreature, ObjectMgr/GetQuestTemplate, Player.Main/GetQuestStatusMap, Unit.Main/IsAlive, WorldObject.Object/GetDistance#3 | AiBotAI.Main/UpdateAI, AiBotDoctrineSolo/AcquireTarget, AiBotDoctrineTeam/AcquireTarget | — |
| ConvertMoveToGrindInPlace | method | AiBotAI.Movement/ClearStoredPath, Log.Main/Out, Player.Main/GetName, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | AiBotAI.Main/MovementInform, AiBotAI.Main/UpdateAI, AiBotAI.Movement/MoveToDestination | — |

---

<!-- verify: boundary-bleed | foreign: AiBotAI -->
