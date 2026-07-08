# western_plaguelands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Architecture and Reference Documentation: `western_plaguelands.cpp`

## Purpose & Responsibilities

This unit implements scripted AI for three NPCs in the Western Plaguelands zone:
1.  **`npc_the_scourge_cauldron`**: A quest-triggering object that summons specific NPCs based on the player's incomplete quests and the cauldron's area, then destroys itself.
2.  **`npc_andorhal_tower`**: A stationary guard that grants quest kill credit to players in line of sight, provided a specific Game Object (Beacon Torch) is nearby.
3.  **`npc_highprotectorlorik`**: A combat-capable elite NPC with a complex spell rotation involving healing, defensive cooldowns, and counter-attacks.

No database tables are accessed directly; all logic relies on runtime state, hardcoded IDs, and API calls to other units.

## Member-by-Member Behavior

### The Scourge Cauldron (`npc_the_scourge_cauldronAI`)
*   **Initialization**: Inherits from `ScriptedAI`. Calls `Reset` to enable line-of-sight events.
*   **Trigger Logic (`MoveInLineOfSight#2`)**: When a player enters LOS, the AI checks the cauldron's `AreaId`. Based on the area (199–202), it verifies if the player has specific quests (5216, 5219, 5222, 5225, 5229, 5231, 5233, 5235) in `QUEST_STATUS_INCOMPLETE`. If matched, it summons a corresponding NPC (11075–11078) at the cauldron's position and immediately calls `DoDie`.
*   **Destruction (`DoDie`)**: Deals direct damage equal to current health to kill the cauldron instantly. It enforces a minimum 600-second respawn delay, overriding shorter database values to prevent rapid re-summoning.

### Andorhal Tower Guard (`npc_andorhal_towerAI`)
*   **Initialization**: Inherits from `Scripted_NoMovementAI`. Calls `Reset` to enable line-of-sight events.
*   **Credit Granting (`MoveInLineOfSight`)**: When a player enters LOS, it searches for a Game Object with entry `176093` (Beacon Torch) within 20.0 units. If found, it grants the player kill credit for the guard's NPC entry. No combat is required.

### High Protector Lorik (`npc_highprotectorlorikAI`)
*   **Initialization**: Inherits from `ScriptedAI`. Initializes timers for spells and a global cooldown tracker.
*   **Combat Loop (`UpdateAI`)**:
    *   Maintains `SPELL_RETRIBUTIONAURA` on self.
    *   Manages a manual `m_uiGlobalCooldown` (1000ms after most casts, 1ms during active casting to block others).
    *   **Divine Shield**: Casts if health ≤ 15% and GCD is free. 45s cooldown.
    *   **Arcane Blast**: Casts on victim every 10–12s if GCD is free.
    *   **Holy Light**: Heals self if health ≤ 60%, mana > 700, and GCD is free. 2–6s cooldown.
    *   **Shield Slam**: Casts on victim if the victim is casting a non-melee spell and GCD is free. 9s cooldown.
    *   Performs melee attacks if ready.

## Cross-Unit Boundaries

*   **ScriptedAI / Scripted_NoMovementAI**: Base classes providing the AI framework and event hooks.
*   **Creature.Main / Unit.Main**: Used for state manipulation (`GetHealth`, `SetRespawnDelay`, `DealDamage`, `GetAreaId`, `SelectHostileTarget`, `GetVictim`, `HasAura`, `GetPower`, `GetHealthPercent`).
*   **Player.Main**: Used to check `GetQuestStatus` and grant `KilledMonsterCredit`.
*   **WorldObject.Object**: Used to summon creatures (`SummonCreature#2`) and get area IDs.
*   **GridSearchers**: Used by `npc_andorhal_towerAI` to locate the Beacon Torch via `GetClosestGameObjectWithEntry`.
*   **SpellCaster**: Used for `CastSpell#2` and checking casting states (`IsNonMeleeSpellCasted`).
*   **shared_Util**: Provides `urand` for randomizing spell intervals.
*   **ScriptMgr**: `AddSC_western_plaguelands` registers the scripts via `Script::RegisterSelf`.

## Notable Implementation Details

*   **Self-Destruction Pattern**: `npc_the_scourge_cauldronAI::DoDie` uses `DealDamage` with value equal to `GetHealth()` to force death, ensuring death events trigger correctly.
*   **Respawn Lock**: The cauldron’s `DoDie` explicitly sets a 600s minimum respawn time to prevent exploit loops if the database spawn time is lower.
*   **Proximity Credit**: `npc_andorhal_towerAI` grants credit without combat, contingent solely on the presence of the Beacon Torch GO nearby.
*   **Manual GCD**: `npc_highprotectorlorikAI` implements a custom global cooldown (`m_uiGlobalCooldown`) rather than relying solely on engine-side GCDs, allowing precise control over spell interleaving (e.g., blocking casts while another is channeling).
*   **Counter-Attack Condition**: `Shield Slam` specifically targets victims who are actively casting non-melee spells, penalizing spell-heavy playstyles.

## Member Reference

**npc_the_scourge_cauldronAI**
Constructor for the Scourge Cauldron AI. Initializes `ScriptedAI` and calls `Reset`.

**Reset#3**
Method in `npc_the_scourge_cauldronAI`. Enables `MoveInLineOfSight` events.

**DoDie**
Method in `npc_the_scourge_cauldronAI`. Kills the cauldron via self-damage and enforces a 600s minimum respawn delay.

**MoveInLineOfSight#2**
Method in `npc_the_scourge_cauldronAI`. Checks player quest status against the cauldron's area ID; summons a specific NPC and calls `DoDie` if a match is found.

**GetAI_npc_the_scourge_cauldron**
Factory function returning a new `npc_the_scourge_cauldronAI` instance.

**npc_andorhal_towerAI**
Constructor for the Andorhal Tower Guard AI. Initializes `Scripted_NoMovementAI` and calls `Reset`.

**Reset**
Method in `npc_andorhal_towerAI`. Enables `MoveInLineOfSight` events.

**MoveInLineOfSight**
Method in `npc_andorhal_towerAI`. Grants kill credit to players in LOS if a Beacon Torch (GO 176093) is within 20 units.

**GetAI_npc_andorhal_tower**
Factory function returning a new `npc_andorhal_towerAI` instance.

**npc_highprotectorlorikAI**
Constructor for High Protector Lorik's AI. Initializes `ScriptedAI` and calls `Reset`.

**Reset#2**
Method in `npc_highprotectorlorikAI`. Resets all spell timers and the global cooldown.

**UpdateAI**
Method in `npc_highprotectorlorikAI`. Main combat loop managing auras, global cooldown, and conditional spell casts (Divine Shield, Arcane Blast, Holy Light, Shield Slam) plus melee attacks.

**GetAI_npc_highprotectorlorik**
Factory function returning a new `npc_highprotectorlorikAI` instance.

**AddSC_western_plaguelands**
Registration function creating and registering `Script` objects for all three NPCs with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — western_plaguelands

*Source:* western_plaguelands.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_the_scourge_cauldronAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | Creature.Main/EnableMoveInLosEvent | — | — |
| DoDie | method | Creature.Main/GetRespawnDelay, Creature.Main/SetRespawnDelay, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/SetInvincibilityHpThreshold | — | — |
| MoveInLineOfSight#2 | method | Object/GetTypeId, Player.Main/GetQuestStatus, WorldObject.Object/GetAreaId, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_npc_the_scourge_cauldron | function | — | — | — |
| npc_andorhal_towerAI | ctor | Scripted_NoMovementAI/Scripted_NoMovementAI | — | — |
| Reset | method | Creature.Main/EnableMoveInLosEvent | — | — |
| MoveInLineOfSight | method | GridSearchers/GetClosestGameObjectWithEntry, Object/GetEntry, Object/GetGUID, Object/GetTypeId, ObjectGuid/ObjectGuid#5, Player.Main/KilledMonsterCredit | — | — |
| GetAI_npc_andorhal_tower | function | — | — | — |
| npc_highprotectorlorikAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetHealthPercent, Unit.Main/GetPower, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_highprotectorlorik | function | — | — | — |
| AddSC_western_plaguelands | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
