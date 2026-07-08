# npc_j_eevee

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# npc_j_eevee

**Purpose & Responsibilities**
`npc_j_eevee` implements the scripted artificial intelligence for the NPC "Jeevee" in two distinct locations within the game world: **Dire Maul** (specifically the Dreadsteed ritual event) and **Scholomance**. The unit provides two separate AI structs, `npc_j_eevee_dreadsteedAI` and `npc_j_eevee_scholomanceAI`, which are selected at runtime by the factory function `GetAI_npc_j_eee` based on the creature's current map ID.

Both AIs manage complex, timed waypoint sequences involving movement, dialogue, spell casting, and emotes. They operate primarily in a non-combat state, executing pre-defined animations and speech patterns. If the creature becomes hostile during these sequences, both AIs fall back to standard melee combat behavior via `DoMeleeAttackIfReady`. Upon completion of their respective scripts, the creatures despawn using `DisappearAndDie`.

**Data Model**
This unit does not interact with any database tables. All logic is driven by hardcoded coordinates, timers, and spell IDs defined in static arrays and enums within `npc_j_eevee.cpp`.

**Cross-Unit Boundaries**
*   **ScriptedAI**: Both AI structs inherit from `ScriptedAI`, utilizing its base functionality for AI updates and utility methods like `DoCastSpellIfCan` and `DoScriptText`.
*   **Creature/Unit**: The AIs extensively use `Creature` and `Unit` methods to control movement (`MovePoint`, `SetWalk`, `SetFacingTo`), state (`DisappearAndDie`, `SelectHostileTarget`), and interactions (`HandleEmote`, `CastSpell`).
*   **Map/Player**: The AIs retrieve the associated player from the map using `Map::GetPlayer` to target dialogue or award quest credit.
*   **ScriptMgr**: Used to broadcast dialogue lines (`DoScriptText`) to nearby players.
*   **dreadsteed_ritual**: The `npc_j_eevee_dreadsteedAI` is tightly coupled with the `dreadsteed_ritual` script. `dreadsteed_ritual::EventStart` calls `SetPlayerGuid` and `ShoutFreedom` on this AI to synchronize the ritual's start with Jeevee's actions.

**Notable Implementation Details**
*   **State Persistence in Scholomance AI**: The `npc_j_eevee_scholomanceAI::Reset` method is intentionally empty. This allows the AI to resume its waypoint sequence from where it left off if interrupted, rather than restarting from the beginning. This is crucial for the Scholomance event flow.
*   **Attack Emote Repetition**: In the Scholomance AI, specific waypoints (2, 7, and 12) trigger a repeating unarmed attack emote. This is handled by a secondary timer (`attackRepeatTimer`) within `UpdateAI`, which fires `HandleEmote(EMOTE_ONESHOT_ATTACKUNARMED)` every 1000ms while the creature is waiting at those points.
*   **Quest Credit Logic**: In the Scholomance AI, upon reaching the final dialogue point (waypoint 12), the AI awards quest credit for item 14500 to the associated player using `Player::KilledMonsterCredit`. This simulates a kill credit without an actual monster death.
*   **Movement Mode Switching**: The Scholomance AI switches between walking and running modes. It starts walking, switches to running at waypoint 8 (`SetWalk(false)`), and returns to walking at waypoint 11 (`SetWalk(true)`).
*   **Teleportation**: Both AIs use `SPELL_J_EEVEE_TELEPORT` (ID 7791) to instantly move the creature to its final position or remove it from view. In the Dreadsteed AI, this happens at the last waypoint. In the Scholomance AI, it happens at waypoint 13, immediately after the final attack emote sequence.

## Member Reference

**npc_j_eevee_dreadsteedAI** (ctor): Initializes the AI by setting the creature to walk mode and calling `Reset`. Inherits from `ScriptedAI`.

**Reset**: Resets the internal state variables: `waitTimer` to 3500ms, `currentPoint` to 0, and `waypointReached` to true.

**MovementInform**: Handles arrival at waypoints. For points 1-3, it sets facing, marks the waypoint as reached, and casts `SPELL_J_EEVEE_SUMMONS_OBJECT`. For point 4, it sets facing, marks the waypoint as reached, speaks the final line (`SAY_J_EEVEE_DREADSTEED_4`) to the tracked player, and casts the teleport spell.

**UpdateAI**: Manages the main loop. If no victim is selected, it processes the waypoint sequence: waits for `waitTimer`, plays dialogue for the current point, moves to the next point, and updates the timer. If all points are completed, it despawns the creature. If a victim is selected, it performs melee attacks.

**SetPlayerGuid**: Stores the GUID of the player involved in the ritual. Called by `dreadsteed_ritual::EventStart`.

**ShoutFreedom**: Broadcasts the freedom shout (`SHOUT_J_EEVEE_FREEDOM`). Called by `dreadsteed_ritual::EventStart`.

**npc_j_eevee_scholomanceAI** (ctor): Initializes the AI. If the creature is a temporary summon, it retrieves the summoner's GUID and makes the player kneel. Sets initial timers and state, then calls `Reset` (which does nothing). Inherits from `ScriptedAI`.

**Reset#2**: Intentionally empty to allow the AI to resume its sequence from the current point if reset.

**MovementInform#2**: Handles arrival at waypoints. Sets facing and marks the waypoint as reached. For points 2, 7, and 12, it triggers an unarmed attack emote and resets the attack repeat timer. For point 13, it casts the teleport spell and marks the sequence as finished.

**UpdateAI#2**: Manages the main loop. If no victim is selected, it processes the waypoint sequence: waits for `waitTimer`, plays dialogue or changes walk mode for specific points, moves to the next point, and updates the timer. It also handles the repeating attack emotes for points 2, 7, and 12. At point 12, it awards quest credit. If all points are completed, it despawns the creature. If a victim is selected, it performs melee attacks.

**GetAI_npc_j_eevee**: Factory function that returns the appropriate AI struct based on the creature's map ID: `npc_j_eevee_dreadsteedAI` for Dire Maul, `npc_j_eevee_scholomanceAI` for Scholomance, or nullptr otherwise.

**AddSC_npc_j_eevee**: Registers the script with the game server's script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — npc_j_eevee

*Source:* npc_j_eevee.cpp, npc_j_eevee.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_j_eevee_dreadsteedAI | ctor | ScriptedAI/ScriptedAI, Unit.Main/SetWalk | — | — |
| Reset | method | — | — | — |
| MovementInform | method | CreatureAI/DoCastSpellIfCan, Map.Main/GetPlayer, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/SetFacingTo, WorldObject.Object/GetMap | — | — |
| UpdateAI | method | Creature.Main/DisappearAndDie, Creature.MotionMaster/MovePoint, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| SetPlayerGuid | method | — | dreadsteed_ritual/EventStart | — |
| ShoutFreedom | method | ScriptMgr/DoScriptText | dreadsteed_ritual/EventStart | — |
| npc_j_eevee_scholomanceAI | ctor | Creature.Main/IsTemporarySummon, Map.Main/GetPlayer, ObjectGuid/ObjectGuid#5, ScriptedAI/ScriptedAI, TemporarySummon/GetSummonerGuid, Unit.Main/HandleEmote, Unit.Main/SetWalk, WorldObject.Object/GetMap | — | — |
| Reset#2 | method | — | — | — |
| MovementInform#2 | method | CreatureAI/DoCastSpellIfCan, Unit.Main/HandleEmote, Unit.Main/SetFacingTo | — | — |
| UpdateAI#2 | method | Creature.Main/DisappearAndDie, Creature.MotionMaster/MovePoint, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetPlayer, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, Player.Main/KilledMonsterCredit, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk, WorldObject.Object/GetMap | — | — |
| GetAI_npc_j_eevee | function | WorldObject.Object/GetMapId | — | — |
| AddSC_npc_j_eevee | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
