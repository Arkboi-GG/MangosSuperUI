# arena_challenge_ai

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# arena_challenge_ai

**Purpose & Responsibilities**

`arena_challenge_ai.cpp` implements the artificial intelligence for seven specific Non-Player Characters (NPCs) participating in the "Arena Challenge" event within the Blackrock Depths dungeon. These NPCs represent distinct combat roles—warrior, priest, shaman, rogue, fire mage, frost mage, and hunter—and exhibit unique spell rotations, targeting behaviors, and mechanical quirks appropriate to their classes.

The unit defines seven `ScriptedAI` subclasses, one for each NPC. It provides factory functions (`GetAI_*`) to instantiate these AI objects and a registration function (`AddSC_blackrock_depths_arena_challenge`) to bind them to the server's script manager. The AI logic is timer-driven, relying on the `UpdateAI` loop to manage spell casting, melee attacks, and special mechanics such as feigning death or summoning pets.

**Cross-Unit Boundaries**

All AI classes inherit from `ScriptedAI` (defined in `ScriptedAI.h/cpp`), which provides base functionality for scripted creatures. They rely heavily on:
*   **`CreatureAI`**: For standard combat actions like `DoCastSpellIfCan` and `DoMeleeAttackIfReady`.
*   **`Unit` / `Creature`**: For target selection (`SelectHostileTarget`, `GetVictim`, `SelectAttackingTarget`, `FindLowestHpFriendlyUnit`, `GetPlayerAtMinimumRange`) and state queries.
*   **`shared_Util`**: For generating random intervals via `urand`.
*   **`Map` / `WorldObject`**: Specifically for `npc_malgen_longspearAI`, which uses `GetMap`, `GetCreature`, `SummonCreature`, and position getters to manage a summoned pet.
*   **`ObjectGuid`**: Used by `npc_malgen_longspearAI` to track the GUID of its summoned pet.

**Data Model**

This unit does not interact with any database tables. All behavior is driven by hardcoded spell IDs, timers, and runtime entity states.

**Notable Implementation Details**

1.  **Healer Spell Balance Concern**: In `npc_va_jashniAI::Reset`, a comment notes that the healing spells (`SPELL_VA_JASHNI_FLASH_HEAL`, etc.) have "huge" values and suggests they might be incorrect or require massive cooldowns. The implemented cooldowns (10s–65s) attempt to mitigate this, but the developer uncertainty remains documented in the code.
2.  **Feign Death Mechanic**: `npc_malgen_longspearAI` implements a complex "feign death" routine. When the timer expires, it casts `FEIGN_DEATH`, sets a flag `m_bIsFeigned`, and starts a short 1-second timer (`m_uiFrostTrapTimer`). During this brief window, the AI skips normal combat updates. After 1 second, it removes the feign aura, places a `FROST_TRAP`, clears the flag, and resumes combat by chasing a new random target. This creates a tactical pause and area-denial effect.
3.  **Pet Management**: `npc_malgen_longspearAI` summons a pet (`NPC_MALGEN_LONGSPEAR_PET_GNASHJAW`) upon entering combat. It tracks the pet's `ObjectGuid` to despawn it correctly in `EnterEvadeMode` if the combat ends prematurely.
4.  **Targeting Strategies**:
    *   **Theldren & Malgen**: Use `GetPlayerAtMinimumRange(8.0f)` for certain spells, prioritizing nearby players.
    *   **Korv, Snokh, Volida**: Use `SelectAttackingTarget(ATTACKING_TARGET_RANDOM, 0)` for AoE or disruptive spells, spreading damage/control effects randomly among enemies.
    *   **Va Jashni**: Uses `FindLowestHpFriendlyUnit(40.0f, 1)` to prioritize healing allies with low health within range.
5.  **Timer Precision**: Most timers use `urand` to vary cooldowns slightly (e.g., `urand(20000, 30000)`), preventing predictable patterns. However, some abilities like `SPELL_VOLIDA_CONEOFCOLD` have fixed 20-second cooldowns.

## Member Reference

**npc_theldrenAI** (ctor): Initializes the AI for NPC Theldren, calling `Reset()` to set initial spell timers. Inherits from `ScriptedAI`.

**Reset#5**: Resets Theldren's spell timers: Intercept (10s), Mortal Strike (10s), and Fear (30s).

**UpdateAI#5**: Manages Theldren's combat loop. Checks for a valid victim. If the Intercept timer expires, it targets a player within 8 yards and casts Charge. If Mortal Strike timer expires, it casts Mortal Strike on the current victim. If Fear timer expires, it casts Intimidating Shout on the victim. Finally, it attempts a melee attack if ready. Timers are decremented or reset with random intervals after successful casts.

**GetAI_npc_theldren**: Factory function that returns a new instance of `npc_theldrenAI` for the given creature.

**npc_va_jashniAI** (ctor): Initializes the AI for NPC Va Jashni, calling `Reset()` to set initial healing timers. Inherits from `ScriptedAI`.

**Reset#6**: Resets Va Jashni's healing timers: Flash Heal (10s), Shield (20s), and Renew (30s). Includes a comment questioning the potency of these spells.

**UpdateAI#6**: Manages Va Jashni's healing and combat loop. It first checks healing timers. For each expired timer (Flash Heal, Shield, Renew), it finds the lowest HP friendly unit within 40 yards and casts the respective spell. If no valid target or cast fails, the timer is still reset. After healing checks, it verifies a hostile target exists. If so, it attempts a melee attack. Note: Healing logic runs even if no hostile target is present, allowing pre-combat or defensive healing.

**GetAI_npc_va_jashni**: Factory function that returns a new instance of `npc_va_jashniAI` for the given creature.

**npc_korvAI** (ctor): Initializes the AI for NPC Korv, calling `Reset()` to set initial spell timers. Inherits from `ScriptedAI`.

**Reset**: Resets Korv's spell timers: Frost Shock (10s), Earthbind Totem (20s), and Fire Nova Totem (20s).

**UpdateAI**: Manages Korv's combat loop. Checks for a valid victim. If Frost Shock timer expires, it selects a random attacking target and casts Frost Shock. If Earthbind or Fire Nova timers expire, it casts the respective totem on itself. Finally, it attempts a melee attack. Timers are reset with fixed or random intervals after successful casts.

**GetAI_npc_korv**: Factory function that returns a new instance of `npc_korvAI` for the given creature.

**npc_leftyAI** (ctor): Initializes the AI for NPC Lefty, calling `Reset()` to set the initial spell timer. Inherits from `ScriptedAI`.

**Reset#2**: Resets Lefty's Five Fat Fingers timer to 2 seconds.

**UpdateAI#2**: Manages Lefty's combat loop. Checks for a valid victim. If the Five Fat Fingers timer expires, it casts the spell on the current victim. Finally, it attempts a melee attack. The timer resets with a random interval between 2–3 seconds after a successful cast.

**GetAI_npc_lefty**: Factory function that returns a new instance of `npc_leftyAI` for the given creature.

**npc_snokh_blackspineAI** (ctor): Initializes the AI for NPC Snokh Blackspine, calling `Reset()` to set initial spell timers. Inherits from `ScriptedAI`.

**Reset#4**: Resets Snokh's spell timers: Pyroblast (15s), Scorch (4s), Flamestrike (20s), and Polymorph (30s).

**UpdateAI#4**: Manages Snokh's combat loop. Checks for a valid victim. If Pyroblast or Scorch timers expire, it casts the spell on the current victim. If Flamestrike or Polymorph timers expire, it selects a random attacking target and casts the respective spell. Finally, it attempts a melee attack. Timers are reset with random intervals after successful casts.

**GetAI_npc_snokh_blackspine**: Factory function that returns a new instance of `npc_snokh_blackspineAI` for the given creature.

**npc_volidaAI** (ctor): Initializes the AI for NPC Volida, calling `Reset()` to set initial spell timers. Inherits from `ScriptedAI`.

**Reset#7**: Resets Volida's spell timers: Blizzard (4s) and Cone of Cold (20s).

**UpdateAI#7**: Manages Volida's combat loop. Checks for a valid victim. If the Blizzard timer expires, it selects a random attacking target and casts Blizzard. If the Cone of Cold timer expires, it casts Cone of Cold on the current victim. Finally, it attempts a melee attack. Timers are reset with random (Blizzard) or fixed (Cone of Cold) intervals after successful casts.

**GetAI_npc_volida**: Factory function that returns a new instance of `npc_volidaAI` for the given creature.

**npc_malgen_longspearAI** (ctor): Initializes the AI for NPC Malgen Longspear, calling `Reset()` to set initial timers and state flags. Inherits from `ScriptedAI`.

**Reset#3**: Resets Malgen's spell timers (Aimed Shot: 8s, Multi-Shot: 15s, Feign Death: 10s, Frost Trap: 0). Sets `m_bIsFeigned` to false and clears the pet GUID.

**EnterEvadeMode**: Called when combat ends. Checks if the tracked pet GUID exists on the map. If so, it forces the pet to despawn and clears the GUID.

**EnterCombat**: Called when combat begins. Checks if a pet already exists via the stored GUID. If not, it summons `NPC_MALGEN_LONGSPEAR_PET_GNASHJAW` at the creature's current position with a timed despawn out of combat. It stores the new pet's GUID.

**UpdateAI#3**: Manages Malgen's complex combat loop. If not feigning, it checks for a valid victim; if no victim and not feigning, it returns early. If the Feign Death timer expires, it casts `FEIGN_DEATH`, sets `m_bIsFeigned` to true, and starts a 1-second `m_uiFrostTrapTimer`. The Feign Death timer is reset to a long random interval (50–60s). If `m_uiFrostTrapTimer` is active (non-zero), it decrements it. If it expires, it removes the feign aura, casts `FROST_TRAP`, clears the timer, sets `m_bIsFeigned` to false, selects a new random target, and orders the creature to chase it. If `m_bIsFeigned` is true, it skips further combat actions. Otherwise, it checks Aimed Shot and Multi-Shot timers. Both target players within 8 yards. If timers expire and casts succeed, timers are reset. Finally, it attempts a melee attack.

**GetAI_npc_malgen_longspear**: Factory function that returns a new instance of `npc_malgen_longspearAI` for the given creature.

**AddSC_blackrock_depths_arena_challenge**: Registration function. Creates `Script` objects for all seven NPCs (theldren, va_jashni, korv, lefty, snokh_blackspine, volida, malgen_longspear), assigns their respective `GetAI` factory functions, and registers them with the `ScriptMgr`. Called by `ScriptLoader/AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — arena_challenge_ai

*Source:* arena_challenge_ai.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_theldrenAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#5 | method | — | — | — |
| UpdateAI#5 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/GetPlayerAtMinimumRange, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_theldren | function | — | — | — |
| npc_va_jashniAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#6 | method | — | — | — |
| UpdateAI#6 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/FindLowestHpFriendlyUnit, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_va_jashni | function | — | — | — |
| npc_korvAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_korv | function | — | — | — |
| npc_leftyAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_lefty | function | — | — | — |
| npc_snokh_blackspineAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#4 | method | — | — | — |
| UpdateAI#4 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_snokh_blackspine | function | — | — | — |
| npc_volidaAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#7 | method | — | — | — |
| UpdateAI#7 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_volida | function | — | — | — |
| npc_malgen_longspearAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | ObjectGuid/ObjectGuid | — | — |
| EnterEvadeMode | method | Creature.Main/ForcedDespawn, Map.Main/GetCreature, ObjectGuid/ObjectGuid, WorldObject.Object/GetMap | — | — |
| EnterCombat | method | Map.Main/GetCreature, Object/GetObjectGuid, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI#3 | method | Creature.Main/SelectAttackingTarget, Creature.MotionMaster/MoveChase, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/GetPlayerAtMinimumRange, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_malgen_longspear | function | — | — | — |
| AddSC_blackrock_depths_arena_challenge | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
