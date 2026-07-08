<!-- provenance: failed-members -->
# GridNotifiersImpl

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GridNotifiersImpl

## Purpose & Responsibilities

`GridNotifiersImpl.h` provides the concrete implementations for the visitor-pattern functors and worker functions used by the MaNGOS grid notification system. In the MaNGOS architecture, the world is divided into grids, and entities (players, creatures, game objects, etc.) are stored in maps within those grids. When an entity moves or changes state, the server must notify other entities in the vicinity. Rather than hardcoding iteration logic into every subsystem, MaNGOS uses a "notifier" pattern: a functor is passed to a grid iterator, which calls the functor's `Visit` method for each relevant map type (e.g., `CreatureMapType`, `PlayerMapType`).

This unit implements four primary categories of behavior:
1.  **Spatial Awareness & AI Triggers:** Functions that determine when creatures detect players or other creatures moving into their line of sight, triggering AI events like `MoveInLineOfSight`.
2.  **Spell Area-of-Effect (AoE) Processing:** Logic for updating dynamic spell objects (like Consecration or Frost Nova) to apply effects to units within their radius, handling visibility, immunity, PvP flags, and threat generation.
3.  **Generic Searchers:** Template-based functors (`WorldObjectSearcher`, `UnitSearcher`, etc.) that iterate over grid maps to find specific objects matching a user-defined predicate (`Check`). These support finding the first match, the last match, or collecting all matches into a list.
4.  **Localized Packet Broadcasting:** Functors that send network packets to players, caching the packet data per locale to avoid redundant serialization.

This file contains no database interactions. It operates entirely on in-memory world state objects.

## Member-by-Member Behavior

### Spatial Awareness and Relocation Workers

These functions handle the logic triggered when entities move relative to each other. They are typically invoked by the grid traversal system when a player or creature enters a new cell or moves significantly within a cell.

*   **`CallAIMoveLOS`**: The core logic for determining if a `Creature` (`c`) should react to a `Unit` (`moving`) entering its field of view.
    *   It first checks if the creature is capable of reacting: it must not have lost control, not be in evade mode, and have an AI instance.
    *   It calls `moving->IsVisibleForOrDetect(c, c, true, false, &alert)` to determine visibility. This method likely checks line-of-sight, stealth, and detection mechanics.
    *   If visible, it triggers `c->AI()->MoveInLineOfSight(moving)`.
    *   If not visible, but the moving unit is a stealthed player and the `alert` flag was set (indicating the creature *noticed* the movement despite not seeing the player clearly, or perhaps a stealth break condition), it triggers `c->AI()->OnMoveInStealth(moving)`.
*   **`PlayerCreatureRelocationWorker`**: A wrapper that calls `CallAIMoveLOS(c, pl)`. It handles the specific case where a `Player` moves relative to a `Creature`.
*   **`CreatureCreatureRelocationWorker`**: A wrapper that calls `CallAIMoveLOS` in both directions: `c1` detecting `c2`, and `c2` detecting `c1`. This ensures mutual awareness when two creatures move near each other.

### Notifier Visitors (Relocation)

These are `Visit` methods for notifier classes that are instantiated when a specific entity relocates. They iterate over the relevant maps in the grid to find potential targets for the relocation workers.

*   **`MaNGOS::PlayerRelocationNotifier::Visit(CreatureMapType&)`**: Called when a player moves.
    *   Returns immediately if the player is dead or taxi-flying (taxi flying usually bypasses normal ground-based AI detection).
    *   Iterates over all creatures in the map. For each alive creature, it calls `PlayerCreatureRelocationWorker`.
*   **`MaNGOS::CreatureRelocationNotifier::Visit(PlayerMapType&)`**: Called when a creature moves.
    *   Returns immediately if the creature is dead.
    *   Iterates over all players. For each alive, non-taxi-flying player, it calls `PlayerCreatureRelocationWorker`.
*   **`MaNGOS::CreatureRelocationNotifier::Visit(CreatureMapType&)`**: Called when a creature moves.
    *   Returns immediately if the creature is dead.
    *   Iterates over all other creatures. Skips itself. For each alive creature, it calls `CreatureCreatureRelocationWorker`.

### Dynamic Object (Spell AoE) Updater

This section handles the periodic update of persistent area-of-effect spells (DynamicObjects).

*   **`MaNGOS::DynamicObjectUpdater::VisitHelper(Unit* target)`**: The core logic applied to each potential target unit within the spell's radius.
    *   **Visibility & Range Checks:** Returns early if the target cannot see the spell source (`i_check`), is outside the spell's radius, or is immune to AoEs (for creatures).
    *   **GM Exclusion:** If the target is a player, it skips them if they are a Game Master or have GM invisibility enabled (unless they are the caster themselves).
    *   **Target Validity:** Checks if the target is a valid attack target (for harmful spells) or helpful target (for beneficial spells).
    *   **Line of Sight:** For player-cast spells, it enforces Line of Sight between the caster and the target to prevent "floor targeting" exploits. Creatures are exempt from this LoS check ("Let creatures cheat").
    *   **Refresh Check:** If the dynamic object doesn't need refreshing for this target, it returns.
    *   **PvP Flagging (Patch 1.7.0+):** For negative (harmful) spells, it enforces PvP rules. A non-PvP-flagged player cannot damage a PvP-flagged player unless they are in a duel or both are in FFA PvP.
    *   **Threat Generation:** If the spell is harmful and doesn't have attributes suppressing threat, it triggers combat:
        *   Calls `target->AI()->AttackedBy(pUnit)`.
        *   Adds threat to the target's threat list.
        *   Sets combat state for both aggressor and victim.
    *   **Immunity Check:** Skips if the target is immune to the spell or its specific effect.
    *   **Aura Application:**
        *   It attempts to retrieve an existing `SpellAuraHolder` for this spell ID and caster on the target.
        *   **If Holder Exists:**
            *   Marks the holder as in use.
            *   If the specific effect index isn't already applied, it creates a new `PersistentAreaAura`, adds it to the holder, and applies modifiers.
            *   If the effect exists, it updates the duration if the new duration is longer (and the spell isn't channeled).
            *   Marks the holder as not in use.
        *   **If No Holder Exists:**
            *   Creates a new `SpellAuraHolder`.
            *   Creates a `PersistentAreaAura` and adds it.
            *   Attempts to add the holder to the target. If this fails (e.g., debuff slots full), the holder is discarded.
    *   **Channeling Sync:** If the spell is channeled, it synchronizes the aura holder's duration and timers with the caster's current channeled spell to ensure ticks align.
    *   **Tracking:** Adds the target to the dynamic object's affected list.

*   **`MaNGOS::DynamicObjectUpdater::Visit(CreatureMapType&)`** and **`Visit(PlayerMapType&)`**: Simple iterators that call `VisitHelper` for each creature or player in the respective maps.

### Generic Searchers

These template classes implement the `Visitor` pattern for searching grid maps. They take a `Check` predicate (a functor or lambda) that returns `true` if an object matches the search criteria.

*   **`WorldObjectSearcher<Check>::Visit(...)`**: Variants for `GameObjectMapType`, `PlayerMapType`, `CreatureMapType`, `CorpseMapType`, and `DynamicObjectMapType`.
    *   Iterates through the map.
    *   If `i_object` is already found, it returns immediately (optimization).
    *   If the `Check` predicate passes for an object, it stores the object in `i_object` and returns.
    *   *Purpose:* Find the **first** matching world object.

*   **`WorldObjectListSearcher<Check>::Visit(...)`**: Variants for `PlayerMapType`, `CreatureMapType`, `CorpseMapType`, `GameObjectMapType`, and `DynamicObjectMapType`.
    *   Iterates through the map.
    *   If the `Check` predicate passes, it pushes the object onto `i_objects` (a vector/list).
    *   *Purpose:* Collect **all** matching world objects.

*   **`GameObjectSearcher<Check>::Visit(GameObjectMapType&)`**: Finds the first matching GameObject.
*   **`GameObjectLastSearcher<Check>::Visit(GameObjectMapType&)`**: Iterates through all GameObjects, updating `i_object` each time a match is found. Thus, `i_object` holds the **last** matching GameObject encountered in the iteration order.
*   **`GameObjectListSearcher<Check>::Visit(GameObjectMapType&)`**: Collects all matching GameObjects.

*   **`UnitSearcher<Check>::Visit(...)`**: Variants for `CreatureMapType` and `PlayerMapType`. Finds the first matching Unit.
*   **`UnitLastSearcher<Check>::Visit(...)`**: Variants for `CreatureMapType` and `PlayerMapType`. Finds the last matching Unit.
*   **`UnitListSearcher<Check>::Visit(...)`**: Variants for `PlayerMapType` and `CreatureMapType`. Collects all matching Units.

*   **`CreatureSearcher<Check>::Visit(CreatureMapType&)`**: Finds the first matching Creature.
*   **`CreatureLastSearcher<Check>::Visit(CreatureMapType&)`**: Finds the last matching Creature.
*   **`CreatureListSearcher<Check>::Visit(CreatureMapType&)`**: Collects all matching Creatures.

*   **`PlayerSearcher<Check>::Visit(PlayerMapType&)`**: Finds the first matching Player.
*   **`PlayerLastSearcher<Check>::Visit(PlayerMapType&)`**: Finds the last matching Player.
*   **`PlayerListSearcher<Check>::Visit(PlayerMapType&)`**: Collects all matching Players.

### Localized Packet Broadcasters

These functors are used to send network messages to multiple players, optimizing for localization by caching serialized packets per language index.

*   **`LocalizedPacketDo<Builder>::operator()(Player* p)`**:
    *   Determines the player's locale index.
    *   Checks if a packet for this locale is already cached in `i_data_cache`.
    *   If not, it invokes the `i_builder` functor to construct the `WorldPacket` for that locale and caches it.
    *   Sends the cached packet to the player using `SendDirectMessage`.
*   **`LocalizedPacketListDo<Builder>::operator()(Player* p)`**:
    *   Similar to above, but handles a list of packets (`WorldPacketList`).
    *   If the list for the locale is empty, it invokes `i_builder` to populate it.
    *   Sends all packets in the list to the player.

## Cross-Unit Boundaries

The MAP indicates no explicit "Calls out" or "Called by" entries for these members. However, the source code reveals significant dependencies on other units via headers included in `GridNotifiersImpl.h`:

*   **`CreatureAI`**: `CallAIMoveLOS` calls `c->AI()->MoveInLineOfSight()` and `OnMoveInStealth()`. It also calls `AttackedBy()` in `VisitHelper`. This implies tight coupling with the AI subsystem.
*   **`Unit` / `Player` / `Creature`**: Extensive use of methods like `IsVisibleForOrDetect`, `HasStealthAura`, `IsAlive`, `IsTaxiFlying`, `IsImmuneToAoe`, `IsInEvadeMode`, `IsGameMaster`, `GetVisibility`, `IsValidAttackTarget`, `IsImmuneToSpell`, `AddThreat`, `SetInCombatWithAggressor`, etc.
*   **`Spell` / `SpellMgr` / `SpellAuras`**: `VisitHelper` interacts heavily with spell logic: `GetSpellEntry`, `CreateSpellAuraHolder`, `PersistentAreaAura`, `AddSpellAuraHolder`, etc.
*   **`WorldPacket`**: Used in the localized packet broadcasters.
*   **`GridRefManager`**: Used in `VisibleNotifier::Visit`.

Since the MAP lists these as empty, this documentation focuses on the internal logic of this unit. The "collaboration" is implicit: this unit provides the *implementation* of the visitor interface expected by the grid traversal system (likely in `GridNotifiers.h` or similar), and it delegates complex game logic (AI, Spell effects, Network sending) to the respective domain classes.

## Data Model

This unit does not interact with any database tables. All operations are performed on in-memory objects representing the game world state.

## Notable Implementation Details

1.  **Asymmetric LoS Checking for Spells:** In `DynamicObjectUpdater::VisitHelper`, there is a comment: *"Must check LoS with the target to prevent casting through objects by targeting the floor. Let creatures cheat."* Consequently, `i_dynobject.IsWithinLOSInMap(target)` is only enforced if `i_dynobject.GetCasterGuid().IsPlayer()`. Creature-cast AoEs do not require Line of Sight to targets. This is a deliberate design choice to simplify creature AI or match client behavior for NPCs.

2.  **PvP Flag Enforcement:** The code explicitly checks for client build `CLIENT_BUILD_1_6_1`. For builds greater than this, it enforces that non-PvP-flagged players cannot harm PvP-flagged players with negative AoE spells, unless they are in a duel or both are in FFA PvP. This reflects a specific patch note from WoW 1.7.0.

3.  **Aura Holder Sharing:** In `VisitHelper`, if a `SpellAuraHolder` already exists for the spell ID and caster on the target, it reuses it. This allows multiple overlapping dynamic objects of the same spell/caster to share the same aura holder, preventing duplicate buffs/debuffs. It carefully manages the `SetInUse` flag to prevent race conditions or premature deletion.

4.  **Channeling Synchronization:** For channeled spells, the code explicitly synchronizes the `SpellAuraHolder`'s duration and timers with the caster's current channeled spell (`spell->GetCastedTime()`). This ensures that periodic ticks (damage/healing) remain aligned with the server's tick rate for the channel, preventing desync issues.

5.  **Early Exit Optimizations:** Many searcher visitors (e.g., `WorldObjectSearcher`) check `if (i_object) return;` at the start. This allows the grid traversal system to stop iterating once the first match is found, saving CPU cycles. Conversely, `ListSearcher` variants always iterate fully to collect all matches.

6.  **GM Invisibility Handling:** In `VisitHelper`, GMs are excluded from being targeted by AoEs unless they are the caster. This prevents accidental damage to developers or admins during testing or live operation.

7.  **Taxi Flying Exclusion:** Both `PlayerRelocationNotifier` and `CreatureRelocationNotifier` skip processing if the moving entity is taxi-flying. This is likely because taxi flights are treated as a special state where normal ground-based AI detection and proximity checks are irrelevant or disabled.

## Member Reference

*   **CallAIMoveLOS**: Core logic for triggering AI reactions (`MoveInLineOfSight` or `OnMoveInStealth`) when a unit moves into a creature's view, checking visibility, stealth, and control states.
*   **PlayerCreatureRelocationWorker**: Wrapper calling `CallAIMoveLOS` for a player moving relative to a creature.
*   **CreatureCreatureRelocationWorker**: Wrapper calling `CallAIMoveLOS` bidirectionally for two creatures moving relative to each other.
*   **Visit#24** (`MaNGOS::PlayerRelocationNotifier::Visit(CreatureMapType&)`): Iterates creatures to trigger `PlayerCreatureRelocationWorker` when a player moves, skipping dead/taxi-flying players.
*   **Visit#25** (`MaNGOS::CreatureRelocationNotifier::Visit(PlayerMapType&)`): Iterates players to trigger `PlayerCreatureRelocationWorker` when a creature moves, skipping dead/taxi-flying players.
*   **Visit#22** (`MaNGOS::CreatureRelocationNotifier::Visit(CreatureMapType&)`): Iterates other creatures to trigger `CreatureCreatureRelocationWorker` when a creature moves, skipping dead creatures and self.
*   **Visit#21** (`MaNGOS::DynamicObjectUpdater::VisitHelper(Unit* target)`): Applies AoE spell effects to a target, handling visibility, range, immunity, PvP flags, threat, and aura creation/synchronization.
*   **Visit#20** (`MaNGOS::DynamicObjectUpdater::Visit(CreatureMapType&)`): Iterates creatures, calling `VisitHelper` for each.
*   **Visit#17** (`MaNGOS::DynamicObjectUpdater::Visit(PlayerMapType&)`): Iterates players, calling `VisitHelper` for each.
*   **Visit#16** (`MaNGOS::WorldObjectSearcher<Check>::Visit(GameObjectMapType&)`): Finds the first GameObject matching the `Check` predicate.
*   **Visit#19** (`MaNGOS::WorldObjectSearcher<Check>::Visit(PlayerMapType&)`): Finds the first Player matching the `Check` predicate.
*   **Visit#18** (`MaNGOS::WorldObjectSearcher<Check>::Visit(CreatureMapType&)`): Finds the first Creature matching the `Check` predicate.
*   **Visit#6** (`MaNGOS::WorldObjectSearcher<Check>::Visit(CorpseMapType&)`): Finds the first Corpse matching the `Check` predicate.
*   **Visit#4** (`MaNGOS::WorldObjectSearcher<Check>::Visit(DynamicObjectMapType&)`): Finds the first DynamicObject matching the `Check` predicate.
*   **Visit#5** (`MaNGOS::WorldObjectListSearcher<Check>::Visit(PlayerMapType&)`): Collects all Players matching the `Check` predicate.
*   **Visit#14** (`MaNGOS::WorldObjectListSearcher<Check>::Visit(CreatureMapType&)`): Collects all Creatures matching the `Check` predicate.
*   **Visit#15** (`MaNGOS::WorldObjectListSearcher<Check>::Visit(CorpseMapType&)`): Collects all Corpses matching the `Check` predicate.
*   **Visit#10** (`MaNGOS::WorldObjectListSearcher<Check>::Visit(GameObjectMapType&)`): Collects all GameObjects matching the `Check` predicate.
*   **Visit#11** (`MaNGOS::WorldObjectListSearcher<Check>::Visit(DynamicObjectMapType&)`): Collects all DynamicObjects matching the `Check` predicate.
*   **Visit#13** (`MaNGOS::GameObjectSearcher<Check>::Visit(GameObjectMapType&)`): Finds the first GameObject matching the `Check` predicate.
*   **Visit#12** (`MaNGOS::GameObjectLastSearcher<Check>::Visit(GameObjectMapType&)`): Finds the last GameObject matching the `Check` predicate.
*   **Visit#3** (`MaNGOS::GameObjectListSearcher<Check>::Visit(GameObjectMapType&)`): Collects all GameObjects matching the `Check` predicate.
*   **Visit** (`MaNGOS::VisibleNotifier::Visit(GridRefManager<T>& m)`): Updates visibility for objects in a grid ref manager, erasing them from client GUIDs.
*   **Visit#2** (`MaNGOS::ObjectUpdater::Visit(CreatureMapType &m)`): Updates real-time state for all creatures in the map.
*   **Visit#9** (`MaNGOS::UnitSearcher<Check>::Visit(CreatureMapType&)`): Finds the first Creature (as Unit) matching the `Check` predicate.
*   **Visit#7** (`MaNGOS::UnitSearcher<Check>::Visit(PlayerMapType&)`): Finds the first Player (as Unit) matching the `Check` predicate.
*   **Visit#8** (`MaNGOS::UnitLastSearcher<Check>::Visit(CreatureMapType&)`): Finds the last Creature (as Unit) matching the `Check` predicate.
*   **Visit#23** (`MaNGOS::UnitLastSearcher<Check>::Visit(PlayerMapType&)`): Finds the last Player (as Unit) matching the `Check` predicate.
*   **operator()** (`MaNGOS::LocalizedPacketDo<Builder>::operator()(Player* p)`): Sends a localized, cached `WorldPacket` to a player.
*   **operator()#2** (`MaNGOS::LocalizedPacketListDo<Builder>::operator()(Player* p)`): Sends a list of localized, cached `WorldPackets` to a player.

---

<!-- machine-true, projected from graph.json -->

## Map — GridNotifiersImpl

*Source:* GridNotifiersImpl.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CallAIMoveLOS | function | — | — | — |
| PlayerCreatureRelocationWorker | function | — | — | — |
| CreatureCreatureRelocationWorker | function | — | — | — |
| Visit#24 | function | — | — | — |
| Visit#25 | function | — | — | — |
| Visit#22 | function | — | — | — |
| Visit#21 | function | — | — | — |
| Visit#23 | function | — | — | — |
| Visit#20 | function | — | — | — |
| Visit#17 | function | — | — | — |
| Visit#16 | function | — | — | — |
| Visit#19 | function | — | — | — |
| Visit#18 | function | — | — | — |
| Visit#6 | function | — | — | — |
| Visit#4 | function | — | — | — |
| Visit#5 | function | — | — | — |
| Visit#14 | function | — | — | — |
| Visit#15 | function | — | — | — |
| Visit#10 | function | — | — | — |
| Visit#11 | function | — | — | — |
| Visit#13 | function | — | — | — |
| Visit#12 | function | — | — | — |
| Visit#3 | function | — | — | — |
| Visit | function | — | — | — |
| Visit#2 | function | — | — | — |
| Visit#9 | function | — | — | — |
| Visit#7 | function | — | — | — |
| Visit#8 | function | — | — | — |
| operator() | function | — | — | — |
| operator()#2 | function | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
