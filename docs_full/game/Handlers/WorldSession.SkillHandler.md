<!-- provenance: boundary-bleed -->
# WorldSession.SkillHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.SkillHandler

## Purpose & Responsibilities

`WorldSession.SkillHandler` implements the server-side logic for three specific client-to-server network opcodes related to character skills and talents within the `wowvmangos` emulator. It acts as the initial entry point for these requests, performing basic validation, anti-cheat checks, and interaction verification before delegating complex state changes to the `Player` class or triggering visual effects via spells.

The unit handles:
1.  **Learning Talents:** Forwarding talent selection requests to the player object.
2.  **Wiping Talents:** Validating interaction with a trainer NPC, removing restrictive states (like feign death), resetting the player's talent tree, and playing a visual effect.
3.  **Unlearning Skills:** Verifying that a skill is legally unlearnable for the player's race and class, logging potential cheating attempts if the check fails, and resetting the skill value.

This unit does not persist data directly to the database; it relies entirely on the `Player` object to manage state and persistence.

## Member-by-Member Behavior

### Talent Learning
**`HandleLearnTalentOpcode`** is a thin wrapper that receives a `LearnTalent` packet containing a talent ID and requested rank. It immediately delegates the entire operation to `Player.Main/LearnTalent`. The session layer performs no validation or logic itself for this action.

### Talent Wiping
**`HandleTalentWipeConfirmOpcode`** manages the process of resetting a character's talents. This is a multi-step process involving NPC interaction, state cleanup, and visual feedback:

1.  **NPC Interaction Validation:** It retrieves the `Creature` object corresponding to the GUID in the packet using `Player.Main/GetNPCIfCanInteractWith`, ensuring the target has the `UNIT_NPC_FLAG_TRAINER` flag. If the NPC is missing or inaccessible, it logs a debug message via `Log.Main/Out` and aborts.
2.  **State Cleanup:** If the player is currently in a "feign death" state (`UNIT_STATE_FEIGN_DEATH`), checked via `Unit.Main/HasUnitState`, the handler removes the spells causing this aura using `Unit.Main/RemoveSpellsCausingAura`. This ensures the player is visually and mechanically active for the reset.
3.  **Talent Reset Execution:** It calls `Player.Main/ResetTalents`. If this returns `false` (indicating the player has no talents to reset), it constructs a `MSG_TALENT_WIPE_CONFIRM` packet using `WorldPacket/WorldPacket#4` and `ByteBuffer/operator<<` variants to inform the client of the failure, then sends it via `WorldSession.Main/SendPacket`.
4.  **Visual Feedback:** If the reset succeeds, it casts spell ID `14867` ("Untalent Visual Effect") on the player using `SpellCaster/CastSpell#2`.

### Skill Unlearning
**`HandleUnlearnSkillOpcode`** allows players to drop passive skills (typically racial or class passives) to free up points or change specialization paths. It enforces strict validation:

1.  **Lookup:** It retrieves the `SkillRaceClassInfoEntry` from `DBCStores/GetSkillRaceClassInfo` using the skill ID from the packet and the player's race and class (obtained via `Unit.Main/GetRace` and `Unit.Main/GetClass`).
2.  **Validation:** It checks if the entry exists and if the `SKILL_FLAG_UNLEARNABLE` flag is set.
3.  **Anti-Cheat Enforcement:** If the skill is not unlearnable, it constructs a reason string and triggers `WorldSession.Main/ProcessAnticheatAction` with the detector "PassiveAnticheat". This logs the event and reports it to Game Masters. The function then returns without modifying the skill.
4.  **Execution:** If valid, it calls `Player.Main/SetSkill` to set the skill value to 0.

## Cross-Unit Boundaries

### Calls Out (Dependencies)

*   **`Player.Main`**: The primary collaborator. `SkillHandler` relies on `Player` for high-level game logic:
    *   `LearnTalent`: Handles the actual talent assignment logic.
    *   `ResetTalents`: Manages the internal state of the talent tree reset.
    *   `SetSkill`: Updates the skill value in the player's data structure.
    *   `GetNPCIfCanInteractWith`: Validates proximity and interaction rights with trainers.
*   **`Unit.Main`**: Used for low-level state checks:
    *   `HasUnitState`: Checks for `FEIGN_DEATH` during talent wipe.
    *   `RemoveSpellsCausingAura`: Cleans up the feign death state.
    *   `GetRace` / `GetClass`: Provides context for skill validation.
*   **`DBCStores`**: `GetSkillRaceClassInfo` provides static configuration data to determine if a skill is allowed to be unlearned.
*   **`SpellCaster`**: `CastSpell` is used to trigger the visual effect spell (ID 14867) after a successful talent wipe.
*   **`WorldSession.Main`**:
    *   `GetPlayer`: Retrieves the current `Player` pointer.
    *   `SendPacket`: Sends error responses to the client.
    *   `ProcessAnticheatAction`: Logs suspicious unlearn attempts.
*   **`Log.Main`**: `Out` is used for debugging failed NPC interactions during talent wipes.
*   **`WorldPacket` / `ByteBuffer`**: Used to construct the error response packet for empty talent trees.
*   **`ObjectGuid`**: `GetString` is used for logging the GUID of the interacting NPC.

### Called By

None of the members in this unit are called by other units outside of the standard opcode dispatch mechanism handled by the `WorldSession` base infrastructure. They are leaf nodes in the call graph for these specific opcodes.

## Data Model

This unit does not directly access any database tables. All state modifications (talent resets, skill changes) are performed on the in-memory `Player` object, which handles its own persistence.

## Notable Implementation Details

*   **Hardcoded Spell ID:** In `HandleTalentWipeConfirmOpcode`, the visual effect spell ID `14867` is hardcoded. This assumes the existence of a specific spell in the DBC data for the "Untalent Visual Effect."
*   **Anti-Cheat Trigger:** `HandleUnlearnSkillOpcode` actively flags attempts to unlearn non-unlearnable skills as cheating. This suggests that the client might allow sending this opcode for any skill, and the server must strictly enforce the `SKILL_FLAG_UNLEARNABLE` constraint to prevent exploitation.
*   **Feign Death Handling:** The explicit removal of `SPELL_AURA_FEIGN_DEATH` in `HandleTalentWipeConfirmOpcode` indicates that players might attempt to wipe talents while dead or feigning death, which would otherwise cause issues with spell casting or visual updates.
*   **Empty Talent Tree Response:** If `Player.Main/ResetTalents` returns false, the server sends a specific `MSG_TALENT_WIPE_CONFIRM` packet with zeroed data. This prevents the client from hanging or displaying incorrect UI states when a player with no talents tries to wipe them.

## Member Reference

**HandleLearnTalentOpcode**
Delegates the learning of a talent to `Player.Main/LearnTalent` using the talent ID and rank from the packet. No local validation occurs.

**HandleTalentWipeConfirmOpcode**
Validates interaction with a trainer NPC. Removes `FEIGN_DEATH` state if present. Calls `Player.Main/ResetTalents`; if it fails, sends an error packet to the client. If it succeeds, casts spell 14867 on the player.

**HandleUnlearnSkillOpcode**
Checks if a skill is unlearnable for the player's race/class via `DBCStores/GetSkillRaceClassInfo`. If not unlearnable, logs an anticheat violation via `WorldSession.Main/ProcessAnticheatAction`. If valid, sets the skill to 0 via `Player.Main/SetSkill`.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.SkillHandler

*Source:* SkillHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleLearnTalentOpcode | method | Player.Main/LearnTalent | — | — |
| HandleTalentWipeConfirmOpcode | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#11, Log.Main/Out, ObjectGuid/GetString, Player.Main/GetNPCIfCanInteractWith, Player.Main/ResetTalents, SpellCaster/CastSpell#2, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleUnlearnSkillOpcode | method | DBCStores/GetSkillRaceClassInfo, Player.Main/SetSkill, Unit.Main/GetClass, Unit.Main/GetRace, WorldSession.Main/GetPlayer, WorldSession.Main/ProcessAnticheatAction | — | — |

---

<!-- verify: boundary-bleed | foreign: process, WorldSession -->
