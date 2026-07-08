# go_scripts

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# go_scripts

`go_scripts.cpp` implements scripted behaviors for specific **Game Objects** (static world entities like chests, bells, or decorative items) in the WoW server emulator. It provides two categories of functionality:

1.  **Interaction Handlers (`GOHello` functions):** Standalone functions triggered when a player interacts with specific Game Objects (e.g., clicking a figurine or repairing bot). These handle immediate logic like casting spells, checking skills, or summoning creatures.
2.  **AI Controllers (`GameObjectAI` subclasses):** Classes that manage the ongoing lifecycle of dynamic Game Objects, such as playing hourly bell sounds, looping music during events, or handling timed despawns for fireworks and firecrackers.

The unit registers these scripts with the core `ScriptMgr` via `AddSC_go_scripts`, linking them to specific Game Object entries or names defined in the database.

## Member-by-Member Behavior

### Interaction Handlers

These functions are registered as `pGOHello` callbacks. They execute when a player uses the associated Game Object.

*   **`GOHello_go_cat_figurine`**: Triggers the "Ghost Saber" trap. It casts spell `5968` (`SPELL_SUMMON_GHOST_SABER`) on the interacting player. The spell is cast with `trigger=true`, meaning it does not consume a reagent or trigger global cooldowns in the standard way, acting purely as a scripted effect. It returns `false`, indicating the interaction did not consume the object's use count (allowing repeated triggers if applicable).

*   **`GOHello_go_field_repair_bot_74A`**: Implements a skill-check gate for teaching a spell. It verifies that the player:
    1.  Has the Engineering skill (`SKILL_ENGINEERING`).
    2.  Has a base skill value of at least 300.
    3.  Does *not* already know spell `22704`.
    
    If all conditions are met, it casts spell `22864` on the player (which teaches spell `22704`). It returns `true`, consuming the interaction.

*   **`GOHello_go_resonite_cask`**: Summons a creature upon interaction. It first checks if the Game Object type is `GAMEOBJECT_TYPE_GOOBER`. If so, it summons NPC `11920` (`NPC_GOGGEROC`) at the object's location with a temporary despawn timer of 300,000 ms (5 minutes) after combat ends. It returns `false`.

*   **`GOHello_go_silithyste`**: Handles the pickup of Silithyst ore piles.
    1.  **Buff Check:** It prevents re-application of the buff by checking if the player already has aura `29519` (Effect Index 0). If present, it returns `true` immediately.
    2.  **Spell Cast:** It casts spell `29519` on the player.
    3.  **Logging:** It logs the action to `LOG_BG` with detail level, recording the player's name, GUID, Account ID, and remote IP address.
    4.  **Despawn Logic:** It distinguishes between two Game Object entries:
        *   Entry `181597`: Sets loot state to `GO_JUST_DEACTIVATED` and adds the object to the removal list (immediate despawn).
        *   Other entries: Only sets the loot state to `GO_JUST_DEACTIVATED` (likely allowing natural despawn or reuse depending on core logic).
    Returns `true`.

### AI Controllers

These classes inherit from `GameObjectAI` and manage time-based behaviors using `EventMap`.

#### `go_bells`
Manages the hourly bell tolling mechanic in various capital cities and zones.

*   **Constructor (`go_bells`)**: Determines the sound ID (`_soundId`) based on the Game Object's entry and zone.
    *   **Horde Bells (`GO_HORDE_BELL`):** Uses Undead bell sound in Tirisfal, Undercity, Hillsbrad, or Duskwood. Uses Tribal drum sound elsewhere.
    *   **Alliance Bells (`GO_ALLIANCE_BELL`):** Checks if it is a Lighthouse object (via `isLightHouseObject`). If so, uses Foghorn sound. Otherwise, uses Dwarf/Gnome horn in Ironforge/Dun Morogh, Night Elf bell in Teldrassil/Darnassus/Ashenvale, or Human bell elsewhere.
    *   Logs an error if an invalid entry is encountered.

*   **`isLightHouseObject`**: Helper method to identify lighthouses.
    *   For `AREA_THERAMORE`, it performs a distance check against coordinates `(-3667.0, -4754.0, 1.8)` because the area ID is shared with other bells.
    *   For `AREA_ALCAZ_ISLAND` and `AREA_WESTFALL_LIGHTHOUSE`, it returns `true` directly.

*   **`UpdateAI`**: Drives the bell ringing logic.
    1.  Updates the `EventMap`.
    2.  If the global game event `GAME_EVENT_HOURLY_BELLS` (ID 78) is active and the bell hasn't rung yet (`once` is true), it schedules an initial time event.
    3.  **`EVENT_TIME`**: Calculates the current hour (12-hour format). Determines how many times the bell should ring (1–12).
        *   *Optimization:* Dwarf horns and Lighthouse foghorns are forced to ring only once, regardless of the hour, to avoid excessive noise (noting that official servers may play them every 2 minutes, but this implementation simplifies it to once per hour cycle).
        *   Schedules `EVENT_RING_BELL` for each ring, spaced 4 seconds apart.
    4.  **`EVENT_RING_BELL`**: Plays the determined `_soundId` via `PlayDirectSound`.

*   **`GetAI_go_bells`**: Factory function returning a new `go_bells` instance.

#### `go_darkmoon_faire_music`
Plays ambient music during the Darkmoon Faire event.

*   **Constructor**: Schedules the first music event after 1 second.
*   **`UpdateAI#3`**:
    1.  Updates `EventMap`.
    2.  **`EVENT_DFM_START_MUSIC`**: Checks if either Darkmoon Faire game event (Elwynn `4` or Thunder Bluff `5`) is active. If so, plays music ID `8440`.
    3.  Reschedules itself every 5 seconds. This frequent re-triggering is noted in comments as a sniffed behavior to keep the music packet flowing to clients.
*   **`GetAI_go_darkmoon_faire_music`**: Factory function.

#### `go_firework_rocket`
Handles the immediate despawn of firework rocket effects.

*   **Constructor**: Schedules a despawn event at `Seconds(0)`. Comments note this is necessary because the core otherwise delays despawn significantly (~5 seconds), breaking the visual timing.
*   **`UpdateAI#4`**:
    1.  Exits early if not spawned.
    2.  Executes events.
    3.  **`EVENT_ROCKET_DESPAWN`**: Calls `Despawn()` on the object.
*   **`GetAI_go_firework_rocket`**: Factory function.

#### `go_lunar_festival_firecracker`
Manages Lunar Festival firecrackers, supporting both automatic and player-triggered detonation.

*   **Constructor**: If the entry is `180763` or `180764`, it schedules a random despawn between 30 and 60 seconds.
*   **`OnUse`**: When a player interacts, it overrides any existing timer by scheduling a despawn in 0–2 seconds (random). Returns `true` to consume the interaction.
*   **`UpdateAI#5`**:
    1.  Exits early if not spawned.
    2.  **`EVENT_FIRECRACKER_DESPAWN`**:
        *   Despawns the object.
        *   Calculates respawn time: Uses `GetRespawnDelay()` if set, otherwise falls back to `GetGOData()->GetRandomRespawnTime()`.
        *   Calls `UseDoorOrButton(respawnTime)` to schedule the respawn.
        *   Calls `UpdateObjectVisibility()` to ensure clients see the change.
*   **`GetAI_go_lunar_festival_firecracker`**: Factory function.

#### `go_containment_coffer`
A simple timed despawner for containment coffers.

*   **Constructor**: Initializes `m_despawnTimer` to 20,000 ms (20 seconds).
*   **`UpdateAI#2`**:
    1.  Decrements `m_despawnTimer` by `diff`.
    2.  If timer reaches zero or below, calls `Despawn()` and resets timer to 0.
    *   *Note:* Unlike other AIs, this does not use `EventMap` but manually manages the timer in `UpdateAI`.
*   **`GetAI_go_containment_coffer`**: Factory function.

### Registration

*   **`AddSC_go_scripts`**: Registers all the above scripts with the `ScriptMgr`.
    *   Links `GOHello` functions for: `go_cat_figurine`, `go_field_repair_bot_74A`, `go_resonite_cask`, `go_silithyste`.
    *   Links `GOGetAI` factory functions for: `go_bells`, `go_darkmoon_faire_music`, `go_lunar_festival_firecracker`, `go_firework_rocket`, `go_containment_coffer`.

## Cross-Unit Boundaries

*   **Spell Casting**: All interaction handlers and some AIs call `SpellCaster/CastSpell#2` (via `Player::CastSpell` or `GameObject::CastSpell`) to apply buffs, teach spells, or trigger visual effects.
*   **Skill/Spell Checks**: `GOHello_go_field_repair_bot_74A` relies on `Player.Main` methods (`GetSkillValueBase`, `HasSkill`, `HasSpell`) to validate player eligibility.
*   **Creature Summoning**: `GOHello_go_resonite_cask` uses `WorldObject.Object/SummonCreature#2` to spawn Goggeroc.
*   **Logging**: `GOHello_go_silithyste` uses `Log.Main/Out` to record player actions. It accesses `Player.Main` and `WorldSession.Main` to retrieve Name, GUID, Account ID, and IP.
*   **Game Events**: `go_bells::UpdateAI` and `go_darkmoon_faire_music::UpdateAI#3` query `GameEventMgr.Main/IsActiveEvent` to determine if their behaviors should activate based on global game states.
*   **Audio Playback**: `go_bells` uses `WorldObject.Object/PlayDirectSound`, while `go_darkmoon_faire_music` uses `WorldObject.Object/PlayDirectMusic`.
*   **Object Lifecycle**: Fireworks and firecrackers use `GameObject/Despawn`, `GameObject/isSpawned`, and `GameObject/UseDoorOrButton` to manage visibility and respawn timers. `go_silithyste` uses `GameObject/SetLootState` and `WorldObject.Object/AddObjectToRemoveList`.
*   **Randomization**: `go_lunar_festival_firecracker` uses `shared_Util/urand` for timing variations.

## Data Model

This unit does not directly query or modify database tables. It relies on data pre-loaded by the core engine:
*   **Game Object Entries**: Defined in `gameobject` table (entries like `181597`, `175885`, etc.).
*   **Spells**: Defined in `spell_template` or similar (IDs like `5968`, `22704`, `29519`).
*   **Creatures**: Defined in `creature_template` (ID `11920`).
*   **Game Events**: Defined in `game_event` table (IDs `78`, `4`, `5`).
*   **Areas/Zones**: Defined in `area_table` and `zones` tables.

## Notable Implementation Details

1.  **Bell Sound Logic**: The `go_bells` AI calculates rings based on the system clock (`localtime`). It forces Dwarf and Lighthouse sounds to ring only once per cycle, deviating from a strict "hourly toll" to reduce spam, acknowledging a discrepancy with official server behavior (which reportedly plays them every 2 minutes).
2.  **Lighthouse Detection**: Since Area IDs are not unique enough for Theramore, `go_bells` uses a hardcoded coordinate distance check (`GetDistance`) to distinguish the lighthouse bell from other Alliance bells in the same area.
3.  **Firework Timing Hack**: `go_firework_rocket` schedules its despawn at `Seconds(0)` in the constructor. The comment explicitly states this is a workaround for a core delay ("takes forever... in vmangos") to ensure rockets vanish immediately after firing.
4.  **Silithyst Buff Prevention**: `GOHello_go_silithyste` checks for an existing aura before casting the buff to prevent "recasting," which would cancel the previous buff and potentially spawn duplicate visual effects ("another mound").
5.  **Manual Timer vs EventMap**: `go_containment_coffer` manually decrements a timer in `UpdateAI#2`, whereas all other AI classes in this file use the `EventMap` system. This is a simpler pattern suitable for single-timer objects.
6.  **Music Packet Flooding**: `go_darkmoon_faire_music` reschedules its music event every 5 seconds. The comment notes this is a "sniffed value" required to keep the `SMSG_PLAY_MUSIC` packet flowing to clients, implying the client may stop playing music if it doesn't receive periodic updates.

## Member Reference

*   **GOHello_go_cat_figurine**: Casts spell `5968` on the player; returns `false`.
*   **GOHello_go_field_repair_bot_74A**: Checks Engineering skill ≥300 and absence of spell `22704`; casts `22864` if valid; returns `true`.
*   **GOHello_go_resonite_cask**: If type is GOOBER, summons NPC `11920` with 5-min despawn; returns `false`.
*   **GOHello_go_silithyste**: Prevents re-buff if aura `29519` exists; casts `29519`; logs action; despawns entry `181597` immediately, others just deactivate; returns `true`.
*   **go_bells**: Sets `_soundId` based on GO entry and zone; handles lighthouse detection; logs errors for invalid entries.
*   **isLightHouseObject**: Returns `true` if in Alcaz Island/Westfall Lighthouse areas, or within 1 unit of specific coords in Theramore.
*   **UpdateAI**: Manages hourly bell rings via `EventMap`; calculates rings from system time; plays sound via `PlayDirectSound`.
*   **GetAI_go_bells**: Returns new `go_bells` instance.
*   **go_darkmoon_faire_music**: Schedules first music event at 1s.
*   **UpdateAI#3**: Plays music `8440` if Darkmoon Faire event is active; reschedules every 5s.
*   **GetAI_go_darkmoon_faire_music**: Returns new `go_darkmoon_faire_music` instance.
*   **go_firework_rocket**: Schedules despawn at 0s to bypass core delay.
*   **UpdateAI#4**: Despawns object if spawned.
*   **GetAI_go_firework_rocket**: Returns new `go_firework_rocket` instance.
*   **go_lunar_festival_firecracker**: Schedules random despawn (30-60s) for entries `180763`/`180764`.
*   **OnUse**: Schedules immediate despawn (0-2s) on player interaction.
*   **UpdateAI#5**: Despawns object; calculates respawn time; calls `UseDoorOrButton` and `UpdateObjectVisibility`.
*   **GetAI_go_lunar_festival_firecracker**: Returns new `go_lunar_festival_firecracker` instance.
*   **go_containment_coffer**: Sets `m_despawnTimer` to 20000ms.
*   **UpdateAI#2**: Decrements timer; despawns object when timer ≤ 0.
*   **GetAI_go_containment_coffer**: Returns new `go_containment_coffer` instance.
*   **AddSC_go_scripts**: Registers all GO scripts with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — go_scripts

*Source:* go_scripts.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GOHello_go_cat_figurine | function | SpellCaster/CastSpell#2 | — | — |
| GOHello_go_field_repair_bot_74A | function | Player.Main/GetSkillValueBase, Player.Main/HasSkill, Player.Main/HasSpell, SpellCaster/CastSpell#2 | — | — |
| GOHello_go_resonite_cask | function | GameObject/GetGoType, WorldObject.Object/SummonCreature#2 | — | — |
| GOHello_go_silithyste | function | GameObject/SetLootState, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Player.Main/GetName, Player.Main/GetSession, SpellCaster/CastSpell#2, Unit.Main/HasAura, WorldObject.Object/AddObjectToRemoveList, WorldSession.Main/GetAccountId, WorldSession.Main/GetRemoteAddress | — | — |
| go_bells | ctor | GameObjectAI/GameObjectAI, Log.Main/Out, Object/GetEntry, WorldObject.Object/GetZoneId | — | — |
| isLightHouseObject | method | WorldObject.Object/GetAreaId, WorldObject.Object/GetDistance#4 | — | — |
| UpdateAI | method | EventMap/ExecuteEvent, EventMap/ScheduleEvent#2, EventMap/Update, GameEventMgr.Main/IsActiveEvent, WorldObject.Object/PlayDirectSound | — | — |
| GetAI_go_bells | function | — | — | — |
| go_darkmoon_faire_music | ctor | EventMap/ScheduleEvent#2, GameObjectAI/GameObjectAI | — | — |
| UpdateAI#3 | method | EventMap/ExecuteEvent, EventMap/ScheduleEvent#2, EventMap/Update, GameEventMgr.Main/IsActiveEvent, WorldObject.Object/PlayDirectMusic | — | — |
| GetAI_go_darkmoon_faire_music | function | — | — | — |
| go_firework_rocket | ctor | EventMap/ScheduleEvent#2, GameObjectAI/GameObjectAI | — | — |
| UpdateAI#4 | method | EventMap/ExecuteEvent, EventMap/Update, GameObject/Despawn, GameObject/isSpawned | — | — |
| GetAI_go_firework_rocket | function | — | — | — |
| go_lunar_festival_firecracker | ctor | EventMap/ScheduleEvent#2, GameObjectAI/GameObjectAI, Object/GetEntry, shared_Util/urand | — | — |
| OnUse | method | EventMap/ScheduleEvent#2, shared_Util/urand | — | — |
| UpdateAI#5 | method | EventMap/ExecuteEvent, EventMap/Update, GameObject/Despawn, GameObject/GetGOData, GameObject/GetRespawnDelay, GameObject/isSpawned, GameObject/UseDoorOrButton, GameObjectData/GetRandomRespawnTime, WorldObject.Object/UpdateObjectVisibility | — | — |
| GetAI_go_lunar_festival_firecracker | function | — | — | — |
| go_containment_coffer | ctor | GameObjectAI/GameObjectAI | — | — |
| UpdateAI#2 | method | GameObject/Despawn | — | — |
| GetAI_go_containment_coffer | function | — | — | — |
| AddSC_go_scripts | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
