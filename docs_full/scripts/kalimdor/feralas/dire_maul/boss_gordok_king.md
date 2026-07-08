# boss_gordok_king

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_gordok_king

This unit implements the artificial intelligence for two linked boss creatures in the *Dire Maul* instance: **King Gordok** (`boss_king_gordok`) and **Cho'Rush the Observer** (`boss_chorush`). The core design constraint, enforced by patch-specific logic, is that these two entities must remain in combat together; if one enters combat while the other is idle, the idle entity is forced to attack the same target.

King Gordok functions as a melee warrior-type boss, utilizing standard warrior abilities like Sunder Armor, Mortal Strike, and War Stomp. Cho'Rush acts as a caster support unit that randomly assumes one of three classes—Mage, Shaman, or Priest—at the start of the encounter. Each class specialization dictates a unique set of spells, healing behaviors, and movement patterns. Cho'Rush dynamically switches between melee and ranged positioning based on distance, line-of-sight, and mana levels.

The unit does not interact with any database tables; all state is managed in-memory via the `instance_dire_maul` script instance.

## Member-by-Member Behavior

### King Gordok AI (`boss_king_gordokAI`)

**boss_king_gordokAI**  
The constructor initializes the AI by casting the creature's instance data to `instance_dire_maul` and calling `Reset#2`. It establishes a compile-time constant `m_bLinkCheckEnabled` based on the server's configured WoW patch version. If the patch is 1.9.3 or higher, the link-check logic (forcing Cho'Rush to join combat) is active.

**Reset#2**  
Resets all ability timers to random intervals within defined ranges:
- War Stomp: 7–8 seconds
- Mortal Strike: 15–25 seconds
- Sunder Armor: 4–8 seconds
- Berserker Charge: 9–12 seconds
- Phase counter: 0
- Link Check Timer: 2.5 seconds

**Aggro**  
Triggers when the boss first enters combat. It broadcasts a predefined aggro speech (`SAY_AGGRO`, ID 9481) using `ScriptMgr::DoScriptText`.

**UpdateAI#2**  
The main update loop for King Gordok. It performs the following checks in order:
1.  **Hostile Target Validation**: Returns early if no hostile target or victim exists.
2.  **Sunder Armor**: Casts on the current victim. If the aura stacks reach 5, the next cast timer is extended (15–25s); otherwise, it remains shorter (5–15s).
3.  **Mortal Strike**: Casts on the victim. On success, resets timer to 12–20 seconds.
4.  **War Stomp**: Casts on self (AoE). On success, resets timer to 20–30 seconds.
5.  **Berserker Charge**: Selects a random player target and casts the charge spell. On success, resets timer to 25–30 seconds.
6.  **Melee Attack**: Attempts a standard melee attack if ready.
7.  **Link Check (Patch 1.9.3+)**: Every 2.5 seconds, it retrieves Cho'Rush via the instance data. If Cho'Rush is alive but not in combat, it forces Cho'Rush to attack King Gordok's current victim. This ensures the two bosses cannot be separated during the fight.

**GetAI_boss_king_gordok**  
Factory function that returns a new `boss_king_gordokAI` instance for the given creature.

### Cho'Rush the Observer AI (`boss_chorushAI`)

**boss_chorushAI**  
The constructor initializes the AI, retrieves the instance data, and calls `Reset`. Like King Gordok, it sets `m_bLinkCheckEnabled` based on the server patch version.

**Reset**  
Initializes the AI state:
- Sets the link check timer to 2.5 seconds.
- Retrieves the randomized equipment set (Mage, Shaman, or Priest) from the `instance_dire_maul` script via `GetChoRushEquipment`.
- Loads the corresponding equipment set (`MAGE_EQUIPMENT`, `SHAMAN_EQUIPMENT`, or `PRIST_EQUIPMENT`).
- Enables combat movement initially.
- Initializes all four spell timers to short random values (1–2 seconds) to ensure immediate spell availability.

**UpdateAI**  
The main update loop for Cho'Rush. It dispatches to the specific class-based update method (`UpdateAIMage`, `UpdateAIShaman`, or `UpdateAIPrist`) based on the current equipment set. After handling class-specific logic, it attempts a melee attack. Finally, if link checking is enabled, it verifies King Gordok's combat status. If King Gordok is not in combat, it forces King Gordok to attack Cho'Rush's current victim.

**UpdateAIMage**  
Handles Mage-specific behavior:
- **Fireball**: Casts on victim. Timer depends on melee state (shorter if ranged).
- **Bloodlust**: Checks if Cho'Rush or King Gordok lacks the aura. Randomly selects one to buff.
- **Arcane Explosion**: Checks for melee attackers (within 8 yards). If present, casts AoE damage.
- **Frost Nova**: Checks for melee attackers. If present, casts root effect.
- **Movement Logic**: Switches between melee and ranged modes. Moves to melee if distance < 5 yards, > 30 yards, no line-of-sight, or mana < 5%. Stops moving and casts if distance is 5–30 yards, LOS exists, and mana >= 5%.

**UpdateAIShaman**  
Handles Shaman-specific behavior:
- **Earthgrab Totem**: Checks for melee attackers (within 6 yards). If present, places a totem.
- **Healing Wave**: Targets the lowest HP friendly unit within 40 yards. If none found and Cho'Rush is below 50% health, heals self.
- **Lightning Bolt**: Casts on victim. Timer depends on melee state.
- **Chain Lightning**: Casts on victim.
- **Movement Logic**: Identical to the Mage movement logic (melee/ranged switching based on distance, LOS, and mana).

**UpdateAIPrist**  
Handles Priest-specific behavior:
- **Heal**: Targets the lowest HP friendly unit within 40 yards. If none found and Cho'Rush is below 50% health, heals self.
- **Mind Blast**: Casts on victim. Timer depends on melee state.
- **Power Word Shield**: Buffs the lowest HP friendly unit within 40 yards.
- **Psychic Scream**: Checks for melee attackers (within 8 yards). If present, casts fear effect.
- **Movement Logic**: Identical to the Mage and Shaman movement logic.

**GetAI_boss_chorush**  
Factory function that returns a new `boss_chorushAI` instance for the given creature.

**AddSC_npc_king_gordok**  
Registration function called by the script loader. It creates and registers two scripts:
1.  `boss_king_gordok`: Associates the `GetAI_boss_king_gordok` factory with the creature.
2.  `boss_chorush`: Associates the `GetAI_boss_chorush` factory with the creature.

## Cross-Unit Boundaries

### Instance Data Collaboration
Both `boss_king_gordokAI` and `boss_chorushAI` rely heavily on `instance_dire_maul` (defined in `instance_dire_maul.cpp`).
- **Direction**: Inbound calls from the AI to the instance script.
- **Purpose**:
    - `GetInstanceData`: Used in constructors to obtain the instance context.
    - `GetData64(NPC_CHORUSH)` / `GetData64(NPC_KING_GORDOK)`: Used in `UpdateAI#2` and `UpdateAI` respectively to retrieve the GUID of the partner boss for the link-check mechanism.
    - `GetChoRushEquipment`: Used in `boss_chorushAI::Reset` to determine which class specialization Cho'Rush will play for this encounter.

### Core Framework Integration
- **ScriptedAI**: Both AIs inherit from `ScriptedAI`, providing base functionality for timer management and spell casting helpers (`DoCastSpellIfCan`, `DoMeleeAttackIfReady`).
- **WorldObject/Creature/Unit**: Standard engine methods are used for targeting (`SelectHostileTarget`, `GetVictim`, `SelectAttackingTarget`), state checks (`IsAlive`, `IsInCombat`, `HasAura`), and spatial queries (`GetDistance2d`, `IsWithinLOSInMap`, `FindNearestCreature`).
- **ScriptMgr**: Used in `boss_king_gordokAI::Aggro` to broadcast text events.
- **Script/ScriptMgr**: Used in `AddSC_npc_king_gordok` to register the AI scripts with the engine.

## Data Model

This unit does not access any database tables. All state (timers, phase, equipment set) is held in memory within the AI objects and the `instance_dire_maul` script instance.

## Notable Implementation Details

### Patch-Dependent Linking Logic
The variable `m_bLinkCheckEnabled` is a `bool const` initialized at construction time based on `sWorld.GetWowPatch() >= WOW_PATCH_109`. This reflects a change in World of Warcraft patch 1.9.3 where King Gordok and Cho'Rush were linked. In older patches, players could potentially isolate one boss. The code enforces this link by checking every 2.5 seconds if the partner is in combat. If not, it forces an `AttackStart` on the partner, targeting the current victim of the active boss. This prevents "kiting" or separating the duo.

### Dynamic Class Specialization for Cho'Rush
Cho'Rush does not have a fixed spellbook. Instead, `instance_dire_maul` determines a random equipment set (Mage, Shaman, or Priest) before the encounter begins. `boss_chorushAI::Reset` loads the appropriate equipment and sets the internal state. The `UpdateAI` method then dispatches to one of three specialized update functions (`UpdateAIMage`, `UpdateAIShaman`, `UpdateAIPrist`). This allows a single creature template to behave as three different classes depending on the instance state.

### Movement State Machine
All three Cho'Rush specializations share identical movement logic. They toggle between `m_bInMeele` (true) and ranged (false) states.
- **Transition to Melee**: Occurs if the victim is too close (< 5 yards), too far (> 30 yards), out of line-of-sight, or if mana drops below 5%.
- **Transition to Ranged**: Occurs if the distance is optimal (5–30 yards), line-of-sight is clear, and mana is sufficient (>= 5%).
This logic ensures Cho'Rush maintains optimal casting range while avoiding being overwhelmed by melee attackers or running out of mana.

### Spell Timer Management
Timers are manually managed using `uiDiff` (time since last update). Spells are only cast if their timer expires. Some timers are reset to wider ranges after a successful cast, while others depend on contextual factors (e.g., Sunder Armor timer extends if max stacks are reached).

## Member Reference

**boss_king_gordokAI**  
Constructor for King Gordok's AI. Initializes instance data pointer, sets patch-dependent link check flag, and calls `Reset#2`.

**Reset#2**  
Resets King Gordok's ability timers to random intervals and initializes the phase counter and link check timer.

**Aggro**  
Broadcasts an aggro speech event when King Gordok enters combat.

**UpdateAI#2**  
Main update loop for King Gordok. Handles casting of Sunder Armor, Mortal Strike, War Stomp, and Berserker Charge. Performs melee attacks. Enforces combat linking with Cho'Rush if patch 1.9.3+ is enabled.

**GetAI_boss_king_gordok**  
Factory function returning a new `boss_king_gordokAI` instance.

**boss_chorushAI**  
Constructor for Cho'Rush's AI. Initializes instance data pointer, sets patch-dependent link check flag, and calls `Reset`.

**Reset**  
Initializes Cho'Rush's state. Retrieves the randomized class equipment from the instance script, loads the corresponding equipment, enables combat movement, and initializes spell timers.

**UpdateAI**  
Main update loop for Cho'Rush. Dispatches to the appropriate class-specific update method based on equipment set. Performs melee attacks. Enforces combat linking with King Gordok if patch 1.9.3+ is enabled.

**UpdateAIMage**  
Handles Mage-specific spell rotation (Fireball, Bloodlust, Arcane Explosion, Frost Nova) and movement logic for Cho'Rush.

**UpdateAIShaman**  
Handles Shaman-specific spell rotation (Earthgrab Totem, Healing Wave, Lightning Bolt, Chain Lightning) and movement logic for Cho'Rush.

**UpdateAIPrist**  
Handles Priest-specific spell rotation (Heal, Mind Blast, Power Word Shield, Psychic Scream) and movement logic for Cho'Rush.

**GetAI_boss_chorush**  
Factory function returning a new `boss_chorushAI` instance.

**AddSC_npc_king_gordok**  
Registers the `boss_king_gordok` and `boss_chorush` scripts with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_gordok_king

*Source:* boss_gordok_king.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_king_gordokAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | shared_Util/urand | — | — |
| Aggro | method | ScriptMgr/DoScriptText | — | — |
| UpdateAI#2 | method | Aura/GetStackAmount, Creature.Main/AI, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, instance_dire_maul/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, shared_Util/urand, Unit.Main/GetAura#2, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap | — | — |
| GetAI_boss_king_gordok | function | — | — | — |
| boss_chorushAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | Creature.Main/LoadEquipment, CreatureAI/SetCombatMovement, instance_dire_maul/GetChoRushEquipment, shared_Util/urand | — | — |
| UpdateAI | method | Creature.Main/AI, CreatureAI/AttackStart, CreatureAI/DoMeleeAttackIfReady, instance_dire_maul/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/GetVictim, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap | — | — |
| UpdateAIMage | method | CreatureAI/DoCastSpellIfCan, CreatureAI/IsCombatMovementEnabled, CreatureAI/SetCombatMovement, Map.Main/GetUnit, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedAI/DoStartMovement, ScriptedAI/DoStartNoMovement, shared_Util/urand, Unit.Main/GetAttackers, Unit.Main/GetPowerPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetMap, WorldObject.Object/IsInRange, WorldObject.Object/IsWithinLOSInMap | — | — |
| UpdateAIShaman | method | CreatureAI/DoCastSpellIfCan, CreatureAI/IsCombatMovementEnabled, CreatureAI/SetCombatMovement, Map.Main/GetUnit, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedAI/DoStartMovement, ScriptedAI/DoStartNoMovement, shared_Util/urand, Unit.Main/FindLowestHpFriendlyUnit, Unit.Main/GetAttackers, Unit.Main/GetHealthPercent, Unit.Main/GetPowerPercent, Unit.Main/GetVictim, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetMap, WorldObject.Object/IsInRange, WorldObject.Object/IsWithinLOSInMap | — | — |
| UpdateAIPrist | method | CreatureAI/DoCastSpellIfCan, CreatureAI/IsCombatMovementEnabled, CreatureAI/SetCombatMovement, Map.Main/GetUnit, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedAI/DoStartMovement, ScriptedAI/DoStartNoMovement, shared_Util/urand, Unit.Main/FindLowestHpFriendlyUnit, Unit.Main/GetAttackers, Unit.Main/GetHealthPercent, Unit.Main/GetPowerPercent, Unit.Main/GetVictim, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetMap, WorldObject.Object/IsInRange, WorldObject.Object/IsWithinLOSInMap | — | — |
| GetAI_boss_chorush | function | — | — | — |
| AddSC_npc_king_gordok | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
