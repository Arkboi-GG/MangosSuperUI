<!-- provenance: boundary-bleed -->
# WorldSession.CombatHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.CombatHandler

## Purpose & Responsibilities

`WorldSession.CombatHandler` implements the server-side logic for processing combat-related network opcodes sent by the client. It resides within the `WorldSession` class, which represents a single player's connection to the game world. This unit handles three primary combat interactions: initiating melee attacks (`HandleAttackSwingOpcode`), stopping attacks (`HandleAttackStopOpcode`), and changing weapon sheath states (`HandleSetSheathedOpcode`).

It acts as the validation layer between the client's intent and the game engine's simulation. Before allowing an action to proceed, it verifies that targets exist, are valid enemies, and are alive. It also manages the synchronization of attack states back to the client via `SendAttackStop`, ensuring the client's UI reflects the server's authoritative state. Crucially, it enforces specific game mechanics regarding extra attack procs (such as Reckoning) when attacks are cancelled.

## Member-by-Member Behavior

### Attack Initiation
**`HandleAttackSwingOpcode`** processes the client's request to perform a melee swing. It performs a series of validity checks on the target GUID provided by the client:
1.  **Type Check:** Verifies the target is a `Unit` using `ObjectGuid/IsUnit`. If not, it silently ignores the request.
2.  **Existence Check:** Retrieves the `Unit` object from the current map using `Map.Main/GetUnit`. If the unit is not found (e.g., despawned or out of range), it sends an `SMSG_ATTACKSTOP` packet to the client to halt the attack animation/state.
3.  **Validity Checks:**
    *   **Friendship:** Checks if the target is friendly to the player using `Unit.Main/IsFriendlyTo`.
    *   **Flags:** Checks if the target has `UNIT_FLAG_SPAWNING` or `UNIT_FLAG_NOT_SELECTABLE` using `Object/HasFlag`. These flags indicate the unit is not yet ready for interaction or cannot be targeted.
    *   **Life State:** Checks if the target is alive using `Unit.Main/IsAlive`. The code notes that clients may send swings to known dead targets due to auto-switching options (e.g., between auto-shot and auto-hit).
    *   If any of these checks fail, it sends `SMSG_ATTACKSTOP` to the client.
4.  **Execution:** If all checks pass, it calls `Unit.Main/Attack` on the player with the `true` flag (indicating a melee swing), initiating the combat sequence in the game engine.

### Attack Termination
**`HandleAttackStopOpcode`** handles the client's signal to stop attacking. It performs two critical actions on the player object retrieved via `WorldSession.Main/GetPlayer`:
1.  Calls `Unit.Main/AttackStop` to cease the current attack cycle.
2.  Calls `Unit.Main/ResetExtraAttacks`. This is a significant mechanical enforcement. As documented in the source comments, this ensures that extra attack procs (like Reckoning stacks) are lost when an attack is initiated and then cancelled. This behavior aligns with the 1.12 reference client standards, correcting previous inconsistencies where stacks might persist incorrectly.

### Weapon Sheathing
**`HandleSetSheathedOpcode`** processes requests to change the player's weapon state (unsheathed, off-hand sheathed, main-hand sheathed).
1.  **Validation:** Checks if the requested `sheathed` state is within the valid range (`MAX_SHEATH_STATE`). Invalid values are ignored.
2.  **Spell Interruption:** Calls `SpellCaster/InterruptSpellsWithChannelFlags` and `Unit.Main/RemoveAurasWithInterruptFlags` with the `AURA_INTERRUPT_SHEATHING_CANCELS` flag. This ensures that channeling spells or auras that should be broken by sheathing weapons are properly terminated.
3.  **State Change:** Calls `Unit.Main/SetSheath` to update the player's visual and logical weapon state.

### Client Synchronization
**`SendAttackStop`** constructs and sends the `SMSG_ATTACKSTOP` packet to the client. This packet informs the client to stop the attack animation and reset the attack timer.
*   It includes the player's packed GUID.
*   It includes the target's packed GUID (or an empty GUID if no specific target is being referenced).
*   It includes an unknown field set to `0` (noted in comments as potentially being `1` in some contexts, but hardcoded to `0` here).
*   **Version Compatibility:** The packet structure differs slightly between client builds older and newer than `1.8.4`. For newer builds, it uses `GetPackGUID()` and `PackedGuid`; for older builds, it uses `GetGUID()` and raw `uint64`.

## Cross-Unit Boundaries

*   **`Map.Main/GetUnit`**: Called by `HandleAttackSwingOpcode` to resolve the target GUID into a live `Unit` object. This is the primary bridge between the network layer and the game world simulation.
*   **`Unit.Main/Attack`**: Called by `HandleAttackSwingOpcode` to initiate the combat logic. This delegates the actual damage calculation, threat generation, and animation triggering to the `Unit` class.
*   **`Unit.Main/AttackStop`** & **`Unit.Main/ResetExtraAttacks`**: Called by `HandleAttackStopOpcode`. These methods in the `Unit` class handle the internal state cleanup of the combat system.
*   **`Unit.Main/SetSheath`**, **`Unit.Main/RemoveAurasWithInterruptFlags`**: Called by `HandleSetSheathedOpcode`. These methods in the `Unit` class manage the player's equipment state and aura effects.
*   **`SpellCaster/InterruptSpellsWithChannelFlags`**: Called by `HandleSetSheathedOpcode`. This interacts with the spell system to ensure channeling spells are broken appropriately during sheathing.
*   **`WorldSession.Main/GetPlayer`**: Called by `HandleAttackStopOpcode`, `HandleSetSheathedOpcode`, and `SendAttackStop` to access the `Player` object associated with the session. Note that `GetPlayer` is defined in the `WorldSession` class but implemented in a different partial/unit; this unit only consumes its return value.
*   **`WorldSession.Main/SendPacket`**: Called by `SendAttackStop` to transmit the constructed packet to the client. Similarly, `SendPacket` is a member of `WorldSession` owned by another partial.
*   **`ObjectGuid/IsUnit`**: Called by `HandleAttackSwingOpcode` to verify the target type.
*   **`Object/HasFlag`**: Called by `HandleAttackSwingOpcode` to check unit flags.
*   **`Unit.Main/IsAlive`**: Called by `HandleAttackSwingOpcode` to verify target life state.
*   **`Unit.Main/IsFriendlyTo`**: Called by `HandleAttackSwingOpcode` to verify faction alignment.
*   **`WorldObject.Object/GetMap`**: Called by `HandleAttackSwingOpcode` to retrieve the map context for the player.
*   **`ByteBuffer/operator<<#10`**, **`Object/GetPackGUID`**, **`ObjectGuid/operator<<#2`**, **`PackedGuid/PackedGuid`**, **`WorldPacket/WorldPacket#4`**: Called by `SendAttackStop` to construct the network packet.

## Data Model

This unit does not interact directly with any database tables. All operations are performed on in-memory objects (`Unit`, `Player`, `Map`) derived from the current game state.

## Notable Implementation Details

*   **Reckoning/Extra Attack Reset Logic:** The comment in `HandleAttackStopOpcode` provides crucial context for the call to `ResetExtraAttacks`. It explicitly references a Blizzard forum post explaining that losing Reckoning stacks upon cancelling an auto-attack is the intended behavior for the 1.12 era. This prevents exploits where players could maintain extra attack stacks indefinitely by starting and stopping attacks.
*   **Client-Side Auto-Switch Handling:** `HandleAttackSwingOpcode` accounts for client behavior where the UI might automatically switch between ranged and melee modes, potentially sending swing commands to dead targets. The server gracefully handles this by sending an `SMSG_ATTACKSTOP` rather than crashing or ignoring the packet entirely.
*   **Packet Versioning:** `SendAttackStop` contains preprocessor directives (`#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_8_4`) to handle differences in GUID packing between older and newer client versions. This ensures compatibility across different patches of the game client.
*   **Silent Failure on Invalid Target Type:** If `HandleAttackSwingOpcode` receives a target GUID that is not a Unit (e.g., a GameObject or Corpse), it returns immediately without sending any feedback to the client. This relies on the client to eventually timeout or correct its state, rather than actively correcting it.

## Member Reference

**HandleAttackSwingOpcode**: Validates the target GUID, checks for existence, friendship, spawn flags, and life state. If valid, initiates a melee attack via `Unit.Main/Attack`; otherwise, sends `SMSG_ATTACKSTOP` to the client.

**HandleAttackStopOpcode**: Stops the player's current attack and resets extra attack counters (e.g., Reckoning stacks) to enforce correct game mechanics regarding cancelled attacks.

**HandleSetSheathedOpcode**: Validates the sheath state, interrupts channeling spells/auras affected by sheathing, and updates the player's weapon state.

**SendAttackStop**: Constructs and sends the `SMSG_ATTACKSTOP` packet to the client, handling GUID packing differences for client versions older and newer than 1.8.4.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.CombatHandler

*Source:* CombatHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleAttackSwingOpcode | method | Map.Main/GetUnit, Object/HasFlag, ObjectGuid/IsUnit, Unit.Main/Attack, Unit.Main/IsAlive, Unit.Main/IsFriendlyTo, WorldObject.Object/GetMap | — | — |
| HandleAttackStopOpcode | method | Unit.Main/AttackStop, Unit.Main/ResetExtraAttacks, WorldSession.Main/GetPlayer | — | — |
| HandleSetSheathedOpcode | method | SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/SetSheath, WorldSession.Main/GetPlayer | — | — |
| SendAttackStop | method | ByteBuffer/operator<<#10, Object/GetPackGUID, ObjectGuid/operator<<#2, PackedGuid/PackedGuid, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |

---

<!-- verify: boundary-bleed | foreign: GetGUID, update, WorldSession -->
