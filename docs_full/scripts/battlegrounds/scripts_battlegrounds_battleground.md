<!-- provenance: verbose -->
# scripts_battlegrounds_battleground

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# scripts_battlegrounds_battleground

## Purpose & Responsibilities

This unit implements the AI and interaction logic for two specific Non-Player Character (NPC) types found within World of Warcraft battlegrounds: **Spirit Guides** (`npc_spirit_guide`) and **Banners** (`npc_etendard`).

1.  **Spirit Guides**: These NPCs serve as resurrection points. They automatically channel a resurrection spell on themselves every 30 seconds to affect nearby dead players. They handle player opt-in via gossip and manage cleanup when the guide despawns, forcing waiting players to respawn at the next available graveyard.
2.  **Banners**: These NPCs represent faction banners (e.g., in Alterac Valley). Upon spawning, they apply visual effects and faction-specific buffs, set themselves to idle, and become immune to NPC attacks.

The unit does not interact with any database tables directly; all configuration is hardcoded or derived from the core engine's `creature_template` data.

## Member-by-Member Behavior

### Spirit Guide Logic

#### Initialization and State
*   **`npc_spirit_guideAI` (ctor)**: Initializes the AI, sets the resurrection timer `uiTimerRez` to 0, and calls `Reset()`. Inherits from `ScriptedAI`.
*   **`Reset`**: Empty override satisfying the interface.
*   **`GetData`**: Returns the current value of `uiTimerRez`.

#### Core AI Loop
*   **`UpdateAI#2`**: Manages the 30-second resurrection cycle. If `uiTimerRez` expires, it interrupts non-melee spells, casts `SPELL_SPIRIT_HEAL` (22012) and `SPELL_SPIRIT_HEAL_CHANNEL` (22011) on itself, and resets the timer to 30,000 ms. Otherwise, it decrements the timer by `uiDiff`.

#### Interaction and Cleanup
*   **`CorpseRemoved`**: Triggered when the guide despawns. If on a battleground, it iterates all players on the map. For any dead player within 20 yards who has the `SPELL_WAITING_TO_RESURRECT` aura, it calls `RepopAtGraveyard()` to force them to the next graveyard.
*   **`GossipHello_npc_spirit_guide`**: Casts `SPELL_WAITING_TO_RESURRECT` (2584) on the interacting player, marking them as willing to resurrect.
*   **`AttackedBy`**, **`AttackStart`**, **`DamageTaken`**: Override combat behaviors to make the guide passive and invulnerable. `DamageTaken` sets incoming damage to 0.

#### Factory Function
*   **`GetAI_npc_spirit_guide`**: Factory function returning a new `npc_spirit_guideAI` instance.

### Banner Logic

#### Initialization and State
*   **`npc_etendardAI` (ctor)**: Initializes the AI, sets `m_bSpawned` to `false`, and reads the first spell ID from the creature's template into `m_bAutoRepeatSpell`. Inherits from `NullCreatureAI`.
*   **`UpdateAI`**: Executes a one-time spawn sequence if `m_bSpawned` is false: sets `UNIT_FLAG_IMMUNE_TO_NPC`, sets movement to `IDLE_MOTION_TYPE`, casts `SPELL_SPAWN_EFFECT` (23235) and the faction buff (`m_bAutoRepeatSpell`), then sets `m_bSpawned` to true.

#### Factory Function
*   **`GetAI_npc_etendard`**: Factory function returning a new `npc_etendardAI` instance.

### Script Registration
*   **`AddSC_battleground`**: Registers `npc_spirit_guide` (with AI and gossip handlers) and `npc_etendard` (with AI handler) with the script manager.

## Cross-Unit Boundaries

### Spirit Guide Collaborations
*   **`npc_spirit_guideAI.UpdateAI#2`**: Calls `SpellCaster/InterruptNonMeleeSpells` and `SpellCaster/CastSpell#2` to manage the resurrection channel.
*   **`npc_spirit_guideAI.CorpseRemoved`**: Calls `WorldObject.Object/GetMap`, `Map.Main/IsBattleGround`, `Map.Main/GetPlayers`, `WorldObject.Object/IsWithinDistInMap`, `Unit.Main/HasAura#2`, `Unit.Main/IsAlive`, and `Player.Main/RepopAtGraveyard` to relocate waiting players.
*   **`GossipHello_npc_spirit_guide`**: Calls `SpellCaster/CastSpell#2` to apply the resurrection opt-in aura.

### Banner Collaborations
*   **`npc_etendardAI` (ctor)**: Calls `Creature.Main/GetCreatureInfo` to retrieve spell data and `NullCreatureAI/NullCreatureAI` for base initialization.
*   **`npc_etendardAI.UpdateAI`**: Calls `WorldObject.Object/SetFlag`, `Creature.Main/SetDefaultMovementType`, and `SpellCaster/CastSpell#2` to configure and buff the banner.

### Script Registration Collaboration
*   **`AddSC_battleground`**: Calls `Script/Script` and `ScriptMgr/RegisterSelf` to register scripts. Called by `ScriptLoader/AddScripts`.

## Data Model

This unit does not directly access any database tables. All data is hardcoded or retrieved from the core engine.

## Notable Implementation Details

1.  **Passive Immunity**: Spirit Guides are made invulnerable by setting `DamageTaken` to 0 and ignoring attack events.
2.  **Despawn Cleanup**: `CorpseRemoved` ensures players aren't stranded if a guide despawns by forcing a repop at the next graveyard.
3.  **One-Time Spawn**: Banners use `m_bSpawned` to ensure buffs and effects are applied only once.
4.  **Hardcoded Spells**: Spell IDs are hardcoded in enums, coupling the script to specific game data.

## Member Reference

*   **npc_spirit_guideAI**: Constructor initializes `uiTimerRez` to 0 and calls `Reset()`.
*   **Reset**: Empty override.
*   **GetData**: Returns `uiTimerRez`.
*   **UpdateAI#2**: Manages 30-second resurrection cycle; interrupts spells, casts heal/channel, resets timer.
*   **CorpseRemoved**: Iterates map players; forces `RepopAtGraveyard` for dead, nearby players with resurrection aura.
*   **AttackedBy**: Empty override.
*   **AttackStart**: Empty override.
*   **DamageTaken**: Sets damage to 0.
*   **GossipHello_npc_spirit_guide**: Casts `SPELL_WAITING_TO_RESURRECT` on player.
*   **GetAI_npc_spirit_guide**: Factory function for `npc_spirit_guideAI`.
*   **npc_etendardAI**: Constructor initializes `m_bSpawned` to false and reads spell ID from template.
*   **UpdateAI**: One-time spawn sequence: sets immune flag, idle movement, casts spawn/buff spells, sets `m_bSpawned` true.
*   **GetAI_npc_etendard**: Factory function for `npc_etendardAI`.
*   **AddSC_battleground**: Registers `npc_spirit_guide` and `npc_etendard` scripts.

---

<!-- machine-true, projected from graph.json -->

## Map — scripts_battlegrounds_battleground

*Source:* battleground.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_spirit_guideAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| GetData | method | — | — | — |
| UpdateAI#2 | method | SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells | — | — |
| CorpseRemoved | method | Map.Main/GetPlayers, Map.Main/IsBattleGround, Player.Main/RepopAtGraveyard, Unit.Main/HasAura#2, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDistInMap | — | — |
| AttackedBy | method | — | — | — |
| AttackStart | method | — | — | — |
| DamageTaken | method | — | — | — |
| GossipHello_npc_spirit_guide | function | SpellCaster/CastSpell#2 | — | — |
| GetAI_npc_spirit_guide | function | — | — | — |
| npc_etendardAI | ctor | Creature.Main/GetCreatureInfo, NullCreatureAI/NullCreatureAI | — | — |
| UpdateAI | method | Creature.Main/SetDefaultMovementType, SpellCaster/CastSpell#2, WorldObject.Object/SetFlag | — | — |
| GetAI_npc_etendard | function | — | — | — |
| AddSC_battleground | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
