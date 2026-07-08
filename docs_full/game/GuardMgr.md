# GuardMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuardMgr

`GuardMgr` is a singleton manager responsible for handling the "Guard Post" mechanic in specific zones. When a civilian `Creature` is attacked, this system determines whether a guard should be summoned to defend them. It manages a global cooldown and charge system per area, selects the appropriate guard NPC based on the faction (Alliance/Horde) and the game patch version, plays a contextual speech line based on the civilian's race or location, and summons the guard to attack the enemy.

## Purpose & Responsibilities

The primary responsibility of `GuardMgr` is to abstract the logic for summoning guards in response to attacks on civilians. It replaces or supplements the default `Creature::CallNearestGuard` behavior for specific, hardcoded areas.

Key responsibilities include:
1.  **Area Registration:** Maintaining a map (`m_mAreaGuardInfo`) of Area IDs to specific Alliance and Horde guard NPC IDs. This map is populated in the constructor with data that varies depending on the server's configured WoW patch level (pre- or post-patch 1.07).
2.  **Resource Management:** Tracking "charges" and "cooldowns" for each registered area. Each area starts with a maximum number of charges (`GUARD_POST_MAX_CHARGES`, defined as 10). Summoning a guard consumes one charge and triggers a short cooldown (`GUARD_POST_USE_COOLDOWN`, 10 seconds). Charges regenerate over a longer period (`GUARD_POST_RECHARGE_TIME`, 60 seconds), capped at the maximum.
3.  **Guard Selection:** Determining which specific NPC ID to summon based on the team of the civilian being defended (which is derived from the attacker's team).
4.  **Audio Feedback:** Selecting the correct speech text ID (`GetTextId`) based on the civilian's display model (race) or specific area exceptions (e.g., Razor Hill), ensuring the civilian shouts for help in a way consistent with their race.
5.  **Summoning Execution:** Actually spawning the guard creature near the civilian and commanding it to attack the enemy unit.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`GuardMgr()`**
The constructor initializes the internal state of the manager.
1.  It sets the global recharge timer `m_uiRechargeTimer` to `GUARD_POST_RECHARGE_TIME` (60,000 ms).
2.  It populates `m_mAreaGuardInfo`, an `std::unordered_map<uint32, AreaGuardInfo>`. This map links Area IDs (defined in the `GuardAreas` enum) to `AreaGuardInfo` structs containing the NPC IDs for Alliance and Horde guards.
3.  **Patch-Specific Logic:** The constructor checks `sWorld.GetWowPatch()`. If the patch is 1.07 or higher, it inserts specific "Elite" guard variants for Sepulcher, Menethil, and Hamerfall. Otherwise, it uses the standard guard variants. This ensures historical accuracy for different expansion eras.
4.  Areas like Booty Bay, Ratchet, and Everlook have the same NPC ID for both Alliance and Horde, indicating neutral or shared guard types in those locations.

**`~GuardMgr()`**
The destructor is empty. As a singleton managing simple value types and a map, no explicit cleanup is required.

### State Updates

**`Update(uint32 diff)`**
This method is called periodically by the `World` update loop. It handles two distinct timing mechanisms:
1.  **Global Recharge Timer:** It decrements `m_uiRechargeTimer`. When this timer expires, it sets a flag `bIncreaseCharges` and resets the timer. This global timer acts as a tick for regenerating charges across all areas.
2.  **Per-Area Cooldowns and Charges:** It iterates through all entries in `m_mAreaGuardInfo`:
    *   **Cooldowns:** If an area has an active `cooldown` (set after a guard is summoned), it decrements it by `diff`. If the cooldown reaches zero, it is reset to 0, allowing the area to be used again (provided charges exist).
    *   **Charge Regeneration:** If the global `bIncreaseCharges` flag was set (meaning 60 seconds have passed globally), it increments the `charges` for every area that hasn't reached `GUARD_POST_MAX_CHARGES`. This means all areas recharge simultaneously once every minute, rather than individually.

### Helper Logic

**`GetTextId(uint32 factionTemplateId, uint32 areaId, uint32 displayId)`**
Determines the speech text ID for the civilian calling for help.
1.  **Area Exception:** If the area is `AREA_RAZOR_HILL`, it returns `TEXT_GUARD_ORC_2` ("Grunts! Attack!"), overriding other logic.
2.  **Model-Based Lookup:** It looks up the `CreatureDisplayInfoEntry` for the civilian's `displayId`. Based on the `ModelId` (e.g., `MODEL_HUMAN_MALE`, `MODEL_ORC_FEMALE`), it returns the corresponding racial text (Human, Orc, Dwarf, Night Elf, Undead, Tauren, Gnome, Troll). This ensures the voice line matches the visual race of the NPC.
3.  **Faction Fallback:** If the model lookup fails or doesn't match a known race, it falls back to checking the `factionTemplateId`. It contains extensive switch cases mapping various faction templates (Stormwind, Ironforge, Orgrimmar, Undercity, etc.) to their primary racial text.
4.  **Default:** Returns `TEXT_NONE` if no match is found.

**`GetTeam(Creature* pCivilian, Unit* pEnemy)`**
Determines which team's guard should be summoned.
1.  It attempts to find the player controlling the enemy unit using `Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself`.
2.  If a player is found, it returns the **opposite** team of that player. For example, if the attacker is Alliance, it returns `HORDE`, implying the guard summoned will be Horde? **Correction:** Looking at `SummonGuard`, `GetTeam` is passed to `GetCreatureIdForTeam`. If the attacker is Alliance, `GetTeam` returns `HORDE`. `GetCreatureIdForTeam(HORDE)` returns `creatureIdHorde`. This implies that if an Alliance player attacks a civilian, a *Horde* guard is summoned? This seems counter-intuitive for a "defense" mechanic unless the civilian is explicitly Horde-aligned or the logic assumes the civilian belongs to the faction opposite the attacker. However, looking at the fallback: `return pCivilian->GetTeam();`. If no player is involved, it uses the civilian's team.
    *   *Clarification:* In many MMO contexts, "Guards" belong to the faction of the city. If an Alliance player attacks a civilian in Stormwind (Alliance), the Alliance guards should come. Let's re-read `GetTeam`.
    *   `switch (pPlayer->GetTeam())`: Case `HORDE` returns `ALLIANCE`. Case `ALLIANCE` returns `HORDE`.
    *   So if Attacker is Horde, Team returned is Alliance. `GetCreatureIdForTeam(ALLIANCE)` returns `creatureIdAlliance`.
    *   Therefore: If a Horde player attacks, an Alliance guard is summoned. This makes sense for defending Alliance civilians. The logic assumes the civilian is of the faction opposite the attacker. If the attacker is a mob (no player), it defaults to the civilian's own team.

### Core Action

**`SummonGuard(Creature* pCivilian, Unit* pEnemy)`**
The main entry point for summoning a guard.
1.  **Validation:** Returns `false` if inputs are null.
2.  **Area Check:** Gets the `areaId` of the civilian. Checks if this area exists in `m_mAreaGuardInfo`.
    *   If the area is **not** in the map, it delegates to `Creature.Main/CallNearestGuard(pEnemy)`. This preserves the default vanilla behavior for areas not explicitly managed by `GuardMgr`.
3.  **Resource Check:** If the area is managed, it checks `guardInfo.cooldown` and `guardInfo.charges`. If on cooldown or out of charges, it returns `false` (no guard summoned).
4.  **Consume Resources:** Decrements `charges` and sets `cooldown` to `GUARD_POST_USE_COOLDOWN` (10s).
5.  **Speech:** Calls `GetTextId` to determine the speech line and uses `ScriptMgr/DoScriptText` to broadcast it from the civilian to the enemy.
6.  **Summon:**
    *   Determines the correct NPC ID using `GetTeam` and `AreaGuardInfo/GetCreatureIdForTeam`.
    *   Calculates a spawn point near the civilian using `WorldObject.Object/GetNearPoint` (5 yards away).
    *   Summons the creature using `WorldObject.Object/SummonCreature` with a temporary despawn timer of 2 minutes (`TEMPSUMMON_TIMED_OR_DEAD_DESPAWN`).
    *   If the summon succeeds and the guard has an AI, it commands the guard to `AttackStart` the enemy.
7.  Returns `true` if the process initiated successfully (even if the summon itself failed, though the code structure suggests it returns true after attempting the summon logic path, specifically returning true after the `CallNearestGuard` fallback or after the summon attempt block. Note: The return `true` is outside the `if (creatureId)` block, so it returns true even if `SummonCreature` fails or `creatureId` is 0, provided the area was in the map and resources were consumed. This is a potential minor logic quirk: resources are consumed even if the guard fails to spawn).

## Cross-Unit Boundaries

*   **`World/GetWowPatch`**: Called by `GuardMgr::GuardMgr` to determine which NPC variants to load. This allows the server to adapt guard behavior based on the configured expansion era.
*   **`World/Update`**: Calls `GuardMgr::Update`. This integrates the guard manager's timing logic into the server's main heartbeat.
*   **`BasicAI/SummonGuard`** and **`Creature.Main/OnEnterCombat`**: Call `GuardMgr::SummonGuard`. These are the triggers that initiate the guard summoning process when a civilian enters combat or a specific AI condition is met.
*   **`Creature.Main/CallNearestGuard`**: Called by `GuardMgr::SummonGuard` as a fallback. If `GuardMgr` does not have specific data for an area, it defers to the standard creature behavior to find nearby guards.
*   **`Player.Main/GetTeam`**, **`Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself`**, **`Unit.Main/GetTeam`**: Called by `GuardMgr::GetTeam`. These are used to identify the faction of the attacker to determine which faction's guards should respond.
*   **`AreaGuardInfo/GetCreatureIdForTeam`**: Called by `GuardMgr::SummonGuard`. Retrieves the specific NPC ID from the stored area configuration.
*   **`Creature.Main/AI`**, **`CreatureAI/AttackStart`**: Called by `GuardMgr::SummonGuard`. Used to access the newly spawned guard's AI and command it to engage the target.
*   **`ScriptMgr/DoScriptText`**: Called by `GuardMgr::SummonGuard`. Handles the broadcasting of the civilian's distress call.
*   **`Unit.Main/GetDisplayId`**, **`Unit.Main/GetFactionTemplateId`**: Called by `GuardMgr::SummonGuard` (passed to `GetTextId`). Provides data needed to select the appropriate speech line.
*   **`WorldObject.Object/GetAreaId`**, **`WorldObject.Object/GetNearPoint`**, **`WorldObject.Object/SummonCreature`**: Called by `GuardMgr::SummonGuard`. Handle spatial calculations and the actual entity creation.

## Data Model

`GuardMgr` does not interact with any database tables. All configuration data (Area IDs, NPC IDs, Text IDs) is hardcoded in the source files (`GuardMgr.cpp` and `GuardMgr.h`). The state (cooldowns, charges) is held in memory within the singleton instance and is lost upon server restart.

## Notable Implementation Details

1.  **Hardcoded Configuration:** The entire mapping of areas to guards is hardcoded. Adding a new guard post requires modifying the source code, recompiling, and restarting the server. There is no database-driven configuration.
2.  **Global Charge Recharge:** The `Update` method uses a single global timer (`m_uiRechargeTimer`) to trigger charge regeneration for *all* areas simultaneously. This means if one area is used heavily, it doesn't recharge faster than others; all areas gain a charge once every 60 seconds (up to the max). This simplifies the update logic but may not reflect realistic independent guard post behaviors.
3.  **Patch Sensitivity:** The constructor explicitly branches on `WOW_PATCH_107`. This is a critical detail for maintainers: changing the server's patch setting will change which NPCs are available in specific zones (Sepulcher, Menethil, Hamerfall).
4.  **Fallback Behavior:** For areas not listed in `m_mAreaGuardInfo`, `SummonGuard` calls `Creature.Main/CallNearestGuard`. This ensures that the system doesn't break for unlisted areas but instead relies on the existing, likely less sophisticated, vanilla guard-finding logic.
5.  **Resource Consumption on Failure:** In `SummonGuard`, the charge is decremented and cooldown set *before* the `SummonCreature` call. If `SummonCreature` fails (e.g., due to grid loading issues or invalid coordinates), the charge is still lost. This could lead to "guard posts" becoming unusable temporarily even if no guard appeared.
6.  **Text Selection Priority:** `GetTextId` prioritizes Area (Razor Hill) > Model/Race > Faction. This hierarchy ensures that specific lore-accurate lines (like Razor Hill's unique grunt call) take precedence, followed by visual consistency (race-based voices), and finally faction alignment.
7.  **Neutral Areas:** Areas like Booty Bay, Ratchet, and Everlook have identical NPC IDs for both Alliance and Horde. This suggests these are neutral trading posts where the same guard type responds regardless of the attacker's faction, or the guard is a neutral entity.
8.  **Singleton Pattern:** `GuardMgr` is instantiated as a singleton (`INSTANTIATE_SINGLETON_1`), accessible via `sGuardMgr`. This ensures a single source of truth for guard post states across the entire world.

## Member Reference

**~GuardMgr**
Destructor. Empty implementation. No cleanup required for the managed resources.

**GuardMgr**
Constructor. Initializes the recharge timer and populates the `m_mAreaGuardInfo` map with hardcoded area-to-guard mappings. Applies patch-specific NPC variants for patches >= 1.07.

**Update**
Periodic update method. Decrements the global recharge timer and per-area cooldowns. Regenerates charges for all areas when the global timer expires.

**GetTextId**
Helper method. Returns the appropriate speech text ID for a civilian based on area, display model (race), or faction template. Prioritizes area-specific overrides, then race-based models, then faction templates.

**GetTeam**
Helper method. Determines the team of the guard to summon. Returns the opposite team of the attacking player, or the civilian's own team if no player is involved.

**SummonGuard**
Core method. Checks if the civilian's area has a configured guard post. If so, checks cooldowns and charges. Consumes a charge, sets cooldown, plays speech, and summons the appropriate guard NPC to attack the enemy. Falls back to `Creature.Main/CallNearestGuard` if the area is not configured.

---

<!-- machine-true, projected from graph.json -->

## Map — GuardMgr

*Source:* GuardMgr.cpp, GuardMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~GuardMgr | dtor | — | — | — |
| GuardMgr | ctor | AreaGuardInfo/AreaGuardInfo, World/GetWowPatch | — | — |
| Update | method | — | World/Update | — |
| GetTextId | method | — | — | — |
| GetTeam | method | Player.Main/GetTeam, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetTeam | — | — |
| SummonGuard | method | AreaGuardInfo/GetCreatureIdForTeam, Creature.Main/AI, Creature.Main/CallNearestGuard, CreatureAI/AttackStart, ScriptMgr/DoScriptText, Unit.Main/GetDisplayId, Unit.Main/GetFactionTemplateId, WorldObject.Object/GetAreaId, WorldObject.Object/GetNearPoint, WorldObject.Object/SummonCreature#2 | BasicAI/SummonGuard, Creature.Main/OnEnterCombat | — |
