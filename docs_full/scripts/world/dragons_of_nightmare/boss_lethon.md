<!-- provenance: verbose -->
# boss_lethon

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_lethon

## Purpose & Responsibilities

`boss_lethon.cpp` implements the AI for **Lethon**, a boss in the **Dragon of Nightmare** instance, and its summoned minion, the **Spirit Shade**.

*   **`boss_lethonAI`**: Inherits from `boss_dragon_of_nightmareAI`. It manages Lethon’s combat loop, applying a persistent aura (`SPELL_SHADOW_BOLT_WHIRL`) on aggro and casting `SPELL_DRAW_SPIRIT` to summon Spirit Shades that mimic players.
*   **`npc_spirit_shadeAI`**: Controls the summoned `NPC_SPIRIT_SHADE`. These shades spawn invisible, wait 2.5 seconds, then become visible, follow Lethon, and cast `SPELL_DARK_OFFERING` on him upon proximity or arrival, triggering their own despawn.

The unit contains no database interactions.

## Member-by-Member Behavior

### Boss Lethon (`boss_lethonAI`)

*   **`boss_lethonAI`**: Constructor. Initializes `boss_dragon_of_nightmareAI` and calls `Reset()`.
*   **`Reset`**: Calls parent `Reset` and removes `SPELL_SHADOW_BOLT_WHIRL` from the boss.
*   **`Aggro`**: Calls parent `Aggro`, casts `SPELL_SHADOW_BOLT_WHIRL` (triggered, if absent), and broadcasts `SAY_LETHON_AGGRO`.
*   **`SpellHitTarget`**: When `SPELL_DRAW_SPIRIT` hits a player, summons `NPC_SPIRIT_SHADE` at the player’s location. Copies the player’s display ID, bytes, and orientation to create a visual mimic. Assigns Lethon’s GUID to the shade’s AI.
*   **`SummonedMovementInform`**: If a `NPC_SPIRIT_SHADE` completes `FOLLOW_MOTION_TYPE`, forces it to cast `SPELL_DARK_OFFERING` on Lethon.
*   **`DoSpecialAbility`**: Attempts to cast `SPELL_DRAW_SPIRIT`. On success, broadcasts `SAY_SUMMON_SHADE` and returns `true`.

### Spirit Shade (`npc_spirit_shadeAI`)

*   **`npc_spirit_shadeAI`**: Constructor. Initializes `ScriptedAI` and calls `Reset()`.
*   **`Reset`**: Sets a 2.5-second delay timer and sets visibility to `VISIBILITY_OFF`.
*   **`SpellHitTarget`**: If hit by `SPELL_DARK_OFFERING`, schedules a forced despawn in 300ms.
*   **`UpdateAI`**: Counts down the initial delay. Once expired, sets visibility to `VISIBILITY_ON`. Locates Lethon via GUID:
    *   If within 5.0 units, casts `SPELL_DARK_OFFERING` on him.
    *   If farther, moves to follow Lethon.
    *   If Lethon is missing, despawns immediately.

## Cross-Unit Boundaries

*   **`boss_dragon_of_nightmare`**: `boss_lethonAI` inherits from `boss_dragon_of_nightmareAI`, calling its `Reset` and `Aggro` methods. Instantiated by `GetAI_boss_dragon_of_nightmare`.
*   **`Unit` / `Creature` / `WorldObject`**: Used for positioning, appearance manipulation (`SetDisplayId`, `SetUInt32Value`), summoning (`SummonCreature`), and visibility control.
*   **`ScriptMgr`**: Broadcasts text/sounds (`DoScriptText`) during aggro and summoning.
*   **`CreatureAI`**: Provides `DoCastSpellIfCan` for conditional spell casting.
*   **`SpellCaster`**: Used by `npc_spirit_shadeAI` to cast `SPELL_DARK_OFFERING`.
*   **`Map`**: `npc_spirit_shadeAI` uses `GetMap()->GetCreature` to resolve Lethon’s entity from his GUID.

## Data Model

This unit does not access any database tables.

## Notable Implementation Details

1.  **Visual Mimicry**: `SpellHitTarget` in `boss_lethonAI` copies `UNIT_FIELD_BYTES_0` and initializes player display IDs on the shade, ensuring it visually matches the targeted player.
2.  **Delayed Visibility**: `npc_spirit_shadeAI` starts invisible and waits 2.5 seconds before becoming visible, allowing movement initialization before appearing.
3.  **Despawn Trigger**: The shade casts `SPELL_DARK_OFFERING` on Lethon. The `SpellHitTarget` handler in the shade AI despawns the shade if hit by this spell, acting as a safety net if the spell resolves on the caster or reflects.
4.  **Movement Coupling**: `SummonedMovementInform` in the boss AI triggers the offering spell when the shade’s follow motion completes, linking the boss’s summon management to the shade’s movement state.

## Member Reference

*   **boss_lethonAI**: Constructor; initializes parent and calls `Reset`.
*   **Reset**: Calls parent `Reset` and removes `SPELL_SHADOW_BOLT_WHIRL`.
*   **Aggro**: Calls parent `Aggro`, casts `SPELL_SHADOW_BOLT_WHIRL`, and plays aggro text.
*   **SpellHitTarget**: Summons a player-mimicking `NPC_SPIRIT_SHADE` when `SPELL_DRAW_SPIRIT` hits a player.
*   **SummonedMovementInform**: Forces `SPELL_DARK_OFFERING` on Lethon when a shade completes follow movement.
*   **DoSpecialAbility**: Casts `SPELL_DRAW_SPIRIT` and plays summon text if successful.
*   **npc_spirit_shadeAI**: Constructor; initializes parent and calls `Reset`.
*   **Reset#2**: Sets 2.5s delay and hides the shade.
*   **SpellHitTarget#2**: Despawns the shade after 300ms if hit by `SPELL_DARK_OFFERING`.
*   **UpdateAI**: Manages visibility, follows Lethon, and casts `SPELL_DARK_OFFERING` when close.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_lethon

*Source:* boss_lethon.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_lethonAI | ctor | boss_dragon_of_nightmare/boss_dragon_of_nightmareAI | boss_dragon_of_nightmare/GetAI_boss_dragon_of_nightmare | — |
| Reset | method | boss_dragon_of_nightmare/Reset, Unit.Main/RemoveAurasDueToSpell | — | — |
| Aggro | method | boss_dragon_of_nightmare/Aggro, CreatureAI/DoCastSpellIfCan, ScriptMgr/DoScriptText | — | — |
| SpellHitTarget | method | Creature.Main/AI, Object/GetObjectGuid, Object/GetUInt32Value, Object/ToPlayer, Unit.Main/GetDisplayId, Unit.Main/InitPlayerDisplayIds, Unit.Main/SetDisplayId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2, WorldObject.Object/SetUInt32Value, WorldObject.Object/SummonCreature#2 | — | — |
| SummonedMovementInform | method | Object/GetEntry, SpellCaster/CastSpell#2 | — | — |
| DoSpecialAbility | method | CreatureAI/DoCastSpellIfCan, ScriptMgr/DoScriptText | — | — |
| npc_spirit_shadeAI | ctor | ScriptedAI/ScriptedAI | boss_dragon_of_nightmare/GetAI_npc_spirit_shade | — |
| Reset#2 | method | Unit.Main/SetVisibility | — | — |
| SpellHitTarget#2 | method | Creature.Main/ForcedDespawn | — | — |
| UpdateAI | method | Creature.Main/ForcedDespawn, Creature.MotionMaster/MoveFollow, Map.Main/GetCreature, SpellCaster/CastSpell#2, Unit.Main/GetMotionMaster, Unit.Main/SetVisibility, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDistInMap | — | — |
