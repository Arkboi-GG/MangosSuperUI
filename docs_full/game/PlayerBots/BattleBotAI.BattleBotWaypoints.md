<!-- provenance: boundary-bleed -->
# BattleBotAI.BattleBotWaypoints

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleBotAI.BattleBotWaypoints

## Purpose & Responsibilities

This unit implements the waypoint navigation system for `BattleBotAI` creatures participating in World of Warcraft battlegrounds: Warsong Gulch (WSG), Arathi Basin (AB), and Alterac Valley (AV). It defines static path data (coordinates and callback functions) for specific routes within these maps and provides the logic for bots to traverse these paths, react to objectives (flags, banners, graveyards), and select new destinations based on game state.

The core responsibility is to translate high-level strategic goals (e.g., "capture the flag," "defend the base") into low-level movement commands (`MovePoint`) while handling edge cases like dismounting, graveyard jumps, and combat interruptions. This unit contains the method implementations for path selection and traversal, as well as the global definitions for all path vectors and waypoint callbacks.

## Member-by-Member Behavior

### Waypoint Traversal & Control Flow

**`MoveToNextPoint`** and **`MoveToNextPointSpecial`** drive the bot's movement along a selected path.
*   **`MoveToNextPoint`**: Advances the bot to the next coordinate in `m_currentPath`. It adds a small random offset (`frand(-1, 1)`) to X and Y coordinates to prevent bots from stacking perfectly. It checks for termination conditions: reaching the end of the path, entering combat (unless `BattleBotAI.Main.ShouldIgnoreCombat` returns true), or dying. If terminated, it calls `ClearPath()`. It uses `MOVE_PATHFINDING | MOVE_EXCLUDE_STEEP_SLOPES` for navigation.
*   **`MoveToNextPointSpecial`**: Similar to `MoveToNextPoint` but uses `MOVE_NONE` flags, likely for specific terrain features where standard pathfinding might fail or behave undesirably (e.g., climbing towers in AV). It is referenced in specific waypoints in `vPath_AV_TowerPoint_Bottom_to_Tower_Point_Flag` and `vPath_AV_Icewing_Bunker_Crossroad_to_Icewing_Bunker_Flag`.

**`MovementInform`** is the callback triggered by the motion master when a waypoint is reached.
*   It checks if the current waypoint has an associated function pointer (`pFunc`).
*   If yes, it executes that function (e.g., `WSG_AtAllianceFlag`).
*   If no, it defaults to calling `MoveToNextPoint()` to proceed to the next step.
*   Finally, it calls `CombatBotBaseAI.ActivateNearbyAreaTrigger` to handle area-specific effects.

**`ClearPath`** resets the navigation state, setting `m_currentPath` to null and resetting indices. This is called when a path ends, combat starts, or the bot needs to change strategy. It is invoked by various members in `BattleBotAI.Main` such as `AttackStart`, `DrinkAndEat`, `OnJustDied`, `OnLeaveBattleGround`, and `UpdateAI`.

### Path Selection Strategies

These methods determine *which* path the bot should take next. They are called by `BattleBotAI.Main.UpdateWaypointMovement`.

*   **`StartNewPathFromBeginning`**: Finds all valid paths for the current battleground type (`vPaths_WS`, `vPaths_AB`, or `vPaths_AV`). It filters paths where the bot is currently near the start (forward) or end (reverse) of the path. It respects `vPaths_NoReverseAllowed` to prevent bots from walking backwards on certain routes. It randomly selects one available path and starts moving.
*   **`StartNewPathFromAnywhere`**: Finds the single closest waypoint across *all* available paths for the current battleground and sets the bot to move towards it. This is a fallback or emergency routing mechanism.
*   **`StartNewPathToPosition`**: Given a target position and a set of paths, it finds the path that ends closest to that target. It then finds the nearest point on that path to the bot's current location (within 50 yards) and starts traversing towards the target end. It handles reverse traversal if the target is closer to the path's start.
*   **`StartNewPathToObjective`**: High-level strategic routing.
    *   **Arathi Basin (AV)**:
        *   **Horde**: Prioritizes attacking the Alliance boss (Vanndar Stormpike) if major bases are secured. Then checks for Snowfall Graveyard if close. Then defends assaulted Horde objectives. Finally attacks controlled/assaulted Alliance objectives.
        *   **Alliance**: Prioritizes attacking the Horde boss (Drek'Thar) if major bases are secured. Checks Snowfall Graveyard if close. Has a 25% chance to defend assaulted Alliance objectives. Otherwise, attacks the closest assaulted/controlled Horde objective or Captain Galvangar.
    *   **Warsong Gulch (WSG)**:
        *   If carrying a flag, moves to own base.
        *   If enemy flag is down, moves to enemy base (if within 20-300 yards).

### Objective-Specific Callbacks

These functions are attached to specific waypoints in the path definitions. They execute when the bot arrives at that coordinate.

*   **`WSG_AtAllianceFlag`** / **`WSG_AtHordeFlag`**:
    *   Locates the nearest flag GameObject.
    *   If the bot is on the opposing team and within interaction distance, it interacts with the flag (capturing/picking up) via `HandleGameObjectUseOpcode`.
    *   If the bot is on the owning team but has the enemy flag aura, it moves to the flag position (likely to return it or defend).
    *   Otherwise, it proceeds to the next point.
*   **`WSG_AtAllianceGraveyard`** / **`WSG_AtHordeGraveyard`**:
    *   If the bot is on the correct team, not mounted, and a random check passes (`urand(0, 1)`), it performs a "graveyard jump" animation sequence via `BattleBotAI.Main.DoGraveyardJump`.
    *   Otherwise, it proceeds to the next point.
*   **`AtFlag`** (used by **`AB_AtFlag`** and **`AV_AtFlag`**):
    *   Checks if a friendly player is currently capturing a banner nearby. If so, it aborts the path and restarts from the beginning (likely to regroup or wait).
    *   Iterates through a list of banner IDs (`vFlagIds`). If a neutral banner is found nearby:
        *   Dismounts if mounted.
        *   Removes shapeshift forms if in a disallowed mount form.
        *   Casts `SPELL_CAPTURE_BANNER` on the object.
    *   If no banner is interactable, it proceeds to the next point.
*   **`AtCaveExit`**:
    *   Stops movement.
    *   Attempts to mount via `BattleBotAI.Main.UseMount`.
    *   If successful, clears the path (waiting for further instructions).
    *   If not, proceeds to the next point.

## Cross-Unit Boundaries

*   **Calls `Creature.MotionMaster/MovePoint`**: Used by `MoveToNextPoint`, `MoveToNextPointSpecial`, `WSG_AtAllianceFlag`, `WSG_AtHordeFlag` to initiate physical movement to coordinates.
*   **Calls `GameObject/isSpawned`**: Used by `WSG_AtAllianceFlag`, `WSG_AtHordeFlag`, `AtFlag` to verify the objective exists before interacting.
*   **Calls `GameObjectUse/GameObjectUse` / `WorldSession.SpellHandler/HandleGameObjectUseOpcode`**: Used by `WSG_AtAllianceFlag` and `WSG_AtHordeFlag` to simulate player interaction with flags.
*   **Calls `Player.Main/GetSession`**: Used by `WSG_AtAllianceFlag` and `WSG_AtHordeFlag` to access the session for sending opcodes.
*   **Calls `Player.Main/GetTeam`**: Used by `WSG_AtAllianceFlag`, `WSG_AtHordeFlag`, `WSG_AtAllianceGraveyard`, `WSG_AtHordeGraveyard`, `StartNewPathToObjective` to determine faction-specific behavior.
*   **Calls `Unit.Main/GetMotionMaster`**: Used by `MoveToNextPoint`, `MoveToNextPointSpecial`, `WSG_AtAllianceFlag`, `WSG_AtHordeFlag` to access the motion controller.
*   **Calls `Unit.Main/HasAura#2`**: Used by `WSG_AtAllianceFlag`, `WSG_AtHordeFlag`, `StartNewPathToObjective` to check for flag-carrying auras.
*   **Calls `WorldObject.Object/FindNearestGameObject`**: Used by `WSG_AtAllianceFlag`, `WSG_AtHordeFlag`, `AtFlag` to locate objectives dynamically.
*   **Calls `WorldObject.Object/GetPositionX/Y/Z`**: Used by `WSG_AtAllianceFlag`, `WSG_AtHordeFlag` to get target coordinates.
*   **Calls `WorldObject.Object/IsWithinDistInMap`**: Used by `WSG_AtAllianceFlag`, `WSG_AtHordeFlag` to check interaction range.
*   **Calls `BattleBotAI.Main/DoGraveyardJump`**: Called by `WSG_AtAllianceGraveyard` and `WSG_AtHordeGraveyard` to trigger the jump animation.
*   **Calls `shared_Util/urand`**: Used by `WSG_AtAllianceGraveyard` and `WSG_AtHordeGraveyard` for random behavior variation.
*   **Calls `Unit.Main/IsMounted`**: Used by `WSG_AtAllianceGraveyard`, `WSG_AtHordeGraveyard`, `AtFlag` to check mount status.
*   **Calls `SpellCaster/CastSpell#2`**: Used by `AtFlag` to cast the capture spell.
*   **Calls `SpellCaster/GetCurrentSpell`**: Used by `AtFlag` to check if a friend is already capturing.
*   **Calls `Unit.Main/IsInDisallowedMountForm`**: Used by `AtFlag` to ensure the bot can dismount properly.
*   **Calls `Unit.Main/RemoveSpellsCausingAura`**: Used by `AtFlag` to remove mount or shapeshift auras.
*   **Calls `WorldObject.Object/FindNearestFriendlyPlayer`**: Used by `AtFlag` to detect ongoing captures by allies.
*   **Calls `WorldObject.Object/GetReactionTo`**: Used by `AtFlag` to ensure the banner is neutral/hostile enough to capture.
*   **Calls `BattleBotAI.Main/UseMount`**: Used by `AtCaveExit` to equip a mount upon leaving caves.
*   **Calls `Unit.Main/StopMoving`**: Used by `AtCaveExit` to halt movement before mounting.
*   **Calls `shared_Util/frand`**: Used by `MoveToNextPoint` and `MoveToNextPointSpecial` to add randomness to waypoints.
*   **Calls `Unit.Main/IsAlive`**: Used by `MoveToNextPoint` and `MoveToNextPointSpecial` to stop pathing if dead.
*   **Calls `Unit.Main/IsInCombat`**: Used by `MoveToNextPoint` and `MoveToNextPointSpecial` to interrupt pathing during combat.
*   **Calls `BattleBotAI.Main/ShouldIgnoreCombat`**: Used by `MoveToNextPoint` and `MoveToNextPointSpecial` to determine if combat should interrupt pathing.
*   **Calls `CombatBotBaseAI/ActivateNearbyAreaTrigger`**: Called by `MovementInform` to process area triggers.
*   **Calls `BattleGround/GetTypeID`**: Used by `StartNewPathFromBeginning`, `StartNewPathFromAnywhere`, `StartNewPathToObjective` to select the correct path set.
*   **Calls `Player.Main/GetBattleGround`**: Used by `StartNewPathFromBeginning`, `StartNewPathFromAnywhere`, `StartNewPathToObjective` to access battleground state.
*   **Calls `WorldObject.Object/GetDistance#4`**: Used by `StartNewPathFromBeginning`, `StartNewPathFromAnywhere` to find nearby path starts/ends.
*   **Calls `BattleGround/IsActiveEvent`**: Used by `StartNewPathToObjective` to check objective states (assaulted, controlled).
*   **Calls `BattleGroundWS/IsAllianceFlagPickedup` / `IsHordeFlagPickedup`**: Used by `StartNewPathToObjective` for WSG logic.
*   **Calls `game_Battlegrounds_BattleGround/GetSingleCreatureGuid` / `GetSingleGameObjectGuid`**: Used by `StartNewPathToObjective` to locate bosses and objectives.
*   **Calls `Map.Main/GetCreature` / `GetGameObject`**: Used by `StartNewPathToObjective` to retrieve world objects.
*   **Calls `shared_Util/roll_chance_u`**: Used by `StartNewPathToObjective` for random defense decisions in AV.
*   **Calls `WorldObject.Object/GetDistance` / `GetDistance#3`**: Used by `StartNewPathToObjective` to calculate distances to objectives.
*   **Calls `WorldObject.Object/GetMap`**: Used by `StartNewPathToObjective` to search for entities.
*   **Calls `WorldObject.Object/GetPosition#3`**: Used by `StartNewPathToObjective` to get target positions.
*   **Calls `WorldObject.Object/IsWithinDist`**: Used by `StartNewPathToObjective` to check proximity to objectives.

## Data Model

This unit does not interact with any database tables. All path data and configuration are hardcoded in C++ source files.

## Notable Implementation Details

*   **Hardcoded Paths**: The entire navigation graph for WSG, AB, and AV is hardcoded in `BattleBotWaypoints.cpp`. This makes the bots highly predictable but also ensures they can navigate complex terrain (tunnels, towers) that dynamic pathfinding might struggle with.
*   **Reverse Traversal**: Most paths can be traversed in reverse, except those listed in `vPaths_NoReverseAllowed`. This allows bots to retreat or reposition efficiently.
*   **Randomness**: `MoveToNextPoint` adds `frand(-1, 1)` to X/Y coordinates. This prevents bots from forming perfect lines and helps avoid collision issues.
*   **Graveyard Jumps**: In WSG, bots have a 50% chance (`urand(0, 1)`) to perform a scripted "jump" animation when arriving at a graveyard waypoint. This is purely cosmetic/behavioral flavor.
*   **Banner Capture Logic**: In AB and AV, bots will not capture a banner if a friendly player is already casting the capture spell nearby. This prevents wasted effort and potential desyncs.
*   **Mount Handling**: Bots automatically dismount when capturing banners (`AtFlag`) and attempt to mount when exiting caves (`AtCaveExit`).
*   **Strategic AI in AV**: The `StartNewPathToObjective` function contains distinct strategies for Horde and Alliance. Horde bots prioritize securing the boss room (Vanndar) once bases are taken, while Alliance bots prioritize Drek'Thar. Alliance bots also have a randomized defense component.
*   **Flag Carrier Logic in WSG**: Bots carrying a flag (`HasAura`) will ignore enemy flags and focus on returning to their base. Bots not carrying a flag will attempt to capture the enemy flag if it is down and within range.

## Member Reference

**WSG_AtAllianceFlag**: Function that handles bot behavior at the Alliance flag in Warsong Gulch. It checks if the bot is Horde (to capture) or Alliance with the enemy flag (to return/defend). It interacts with the flag GameObject or moves to its position.

**WSG_AtHordeFlag**: Function that handles bot behavior at the Horde flag in Warsong Gulch. It checks if the bot is Alliance (to capture) or Horde with the enemy flag (to return/defend). It interacts with the flag GameObject or moves to its position.

**WSG_AtAllianceGraveyard**: Function that handles bot behavior at the Alliance graveyard in Warsong Gulch. If the bot is Alliance, not mounted, and a random check passes, it triggers a graveyard jump animation via `BattleBotAI.Main.DoGraveyardJump`. Otherwise, it proceeds to the next waypoint.

**WSG_AtHordeGraveyard**: Function that handles bot behavior at the Horde graveyard in Warsong Gulch. If the bot is Horde, not mounted, and a random check passes, it triggers a graveyard jump animation via `BattleBotAI.Main.DoGraveyardJump`. Otherwise, it proceeds to the next waypoint.

**AtFlag**: Generic function for capturing banners in Arathi Basin and Alterac Valley. It checks for friendly players already capturing, then attempts to capture a neutral banner nearby by dismounting, removing shapeshifts, and casting the capture spell.

**AB_AtFlag**: Wrapper function that calls `AtFlag` with the list of Arathi Basin banner IDs.

**AV_AtFlag**: Wrapper function that calls `AtFlag` with the list of Alterac Valley banner IDs.

**AtCaveExit**: Function that handles bot behavior when exiting a cave in Alterac Valley. It stops movement and attempts to mount via `BattleBotAI.Main.UseMount`. If successful, it clears the current path.

**MoveToNextPointSpecial**: Function that moves the bot to the next waypoint in the current path using `MOVE_NONE` flags, typically for difficult terrain. It adds random offsets and checks for path completion or combat.

**MovementInform**: Method called by the motion master when a waypoint is reached. It executes any associated callback function or proceeds to the next point. It also activates nearby area triggers via `CombatBotBaseAI.ActivateNearbyAreaTrigger`.

**MoveToNextPoint**: Method that moves the bot to the next waypoint in the current path using standard pathfinding. It adds random offsets, checks for path completion, combat, or death, and clears the path if necessary.

**StartNewPathFromBeginning**: Method that selects a random valid path from the current battleground's path list, starting from the beginning or end (if reverse is allowed), based on the bot's current proximity.

**AvailablePath**: Constructor for the internal `AvailablePath` struct used in `StartNewPathFromBeginning` to store path pointers and reverse flags.

**StartNewPathFromAnywhere**: Method that finds the closest waypoint across all paths in the current battleground and sets the bot to move towards it.

**StartNewPathToPosition**: Method that finds the best path to reach a specific target position, considering both forward and reverse traversal, and starts moving along that path.

**StartNewPathToObjective**: Method that determines the next strategic objective based on the battleground type and current game state (e.g., flag status, base control, boss availability) and starts a path towards it.

**ClearPath**: Method that resets the bot's navigation state, clearing the current path and indices.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleBotAI.BattleBotWaypoints

*Source:* BattleBotWaypoints.cpp, BattleBotWaypoints.h, BattleBotAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WSG_AtAllianceFlag | function | Creature.MotionMaster/MovePoint, GameObject/isSpawned, GameObjectUse/GameObjectUse, Object/GetObjectGuid, Player.Main/GetSession, Player.Main/GetTeam, Unit.Main/GetMotionMaster, Unit.Main/HasAura#2, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/IsWithinDistInMap, WorldSession.SpellHandler/HandleGameObjectUseOpcode | — | — |
| WSG_AtHordeFlag | function | Creature.MotionMaster/MovePoint, GameObject/isSpawned, GameObjectUse/GameObjectUse, Object/GetObjectGuid, Player.Main/GetSession, Player.Main/GetTeam, Unit.Main/GetMotionMaster, Unit.Main/HasAura#2, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinDistInMap, WorldSession.SpellHandler/HandleGameObjectUseOpcode | — | — |
| WSG_AtAllianceGraveyard | function | BattleBotAI.Main/DoGraveyardJump, Player.Main/GetTeam, shared_Util/urand, Unit.Main/IsMounted | — | — |
| WSG_AtHordeGraveyard | function | BattleBotAI.Main/DoGraveyardJump, Player.Main/GetTeam, shared_Util/urand, Unit.Main/IsMounted | — | — |
| AtFlag | function | GameObject/isSpawned, SpellCaster/CastSpell#2, SpellCaster/GetCurrentSpell, Unit.Main/IsInDisallowedMountForm, Unit.Main/IsMounted, Unit.Main/RemoveSpellsCausingAura, WorldObject.Object/FindNearestFriendlyPlayer, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetReactionTo | — | — |
| AB_AtFlag | function | — | — | — |
| AV_AtFlag | function | — | — | — |
| AtCaveExit | function | BattleBotAI.Main/UseMount, Unit.Main/StopMoving | — | — |
| MoveToNextPointSpecial | function | BattleBotAI.Main/ShouldIgnoreCombat, Creature.MotionMaster/MovePoint, shared_Util/frand, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/IsInCombat | — | — |
| MovementInform | method | CombatBotBaseAI/ActivateNearbyAreaTrigger | — | — |
| MoveToNextPoint | method | BattleBotAI.Main/ShouldIgnoreCombat, Creature.MotionMaster/MovePoint, shared_Util/frand, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/IsInCombat | — | — |
| StartNewPathFromBeginning | method | BattleGround/GetTypeID, Player.Main/GetBattleGround, WorldObject.Object/GetDistance#4 | BattleBotAI.Main/UpdateWaypointMovement | — |
| AvailablePath | ctor | — | — | — |
| StartNewPathFromAnywhere | method | BattleGround/GetTypeID, Player.Main/GetBattleGround, WorldObject.Object/GetDistance#4 | BattleBotAI.Main/UpdateWaypointMovement | — |
| StartNewPathToPosition | method | WorldObject.Object/GetDistance#4 | — | — |
| StartNewPathToObjective | method | BattleGround/GetTypeID, BattleGround/IsActiveEvent, BattleGroundWS/IsAllianceFlagPickedup, BattleGroundWS/IsHordeFlagPickedup, game_Battlegrounds_BattleGround/GetSingleCreatureGuid, game_Battlegrounds_BattleGround/GetSingleGameObjectGuid, Map.Main/GetCreature, Map.Main/GetGameObject, Player.Main/GetBattleGround, Player.Main/GetTeam, shared_Util/roll_chance_u, Unit.Main/HasAura#2, WorldObject.Object/GetDistance, WorldObject.Object/GetDistance#3, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#3, WorldObject.Object/IsWithinDist | BattleBotAI.Main/UpdateWaypointMovement | — |
| ClearPath | method | — | BattleBotAI.Main/AttackStart, BattleBotAI.Main/DrinkAndEat, BattleBotAI.Main/OnJustDied, BattleBotAI.Main/OnLeaveBattleGround, BattleBotAI.Main/UpdateAI | — |

---

<!-- verify: boundary-bleed | foreign: BattleBotAI -->
