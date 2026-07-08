<!-- provenance: boundary-bleed -->
# WorldSession.DuelHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.DuelHandler

## Purpose & Responsibilities

`WorldSession.DuelHandler` implements the server-side logic for processing two specific client-to-server network opcodes related to player-versus-player duels: acceptance of a duel invitation and cancellation (or forfeiture) of a duel.

This unit acts as the entry point for these events within the `WorldSession` class. It validates the state of the local player (`Player`) and their opponent, ensures consistency between the two participants' internal duel states, and triggers the appropriate game-state transitions (countdown initiation, combat cessation, spell casting, and duel resolution). It relies heavily on the `Player` class to manage the actual duel state machine and on the `SpellCaster` interface to apply visual/audio effects like the "beg" animation.

## Member-by-Member Behavior

### **HandleDuelAcceptedOpcode**

This method processes the `SMSG_DUEL_ACCEPTED` (or equivalent) packet sent by a client when they click "Accept" on a duel invitation.

1.  **Initial Validation**:
    *   It retrieves the local `Player` via `WorldSession.Main/GetPlayer`.
    *   It immediately checks if `pl->m_duel` exists. If not, the method returns silently. This guards against accepting a duel when no invitation is pending or if the player is the initiator (who typically doesn't send an "accept" packet in this flow, or whose state is handled differently).
2.  **State Consistency Checks**:
    *   It retrieves the opponent pointer from `pl->m_duel->opponent`.
    *   It performs a series of strict equality and null checks to ensure the duel state is valid:
        *   The local player must not be the initiator (`pl != pl->m_duel->initiator`).
        *   The opponent must exist (`plTarget` is not null).
        *   The opponent must also have an active duel state (`plTarget->m_duel` is not null).
        *   The local player and opponent must be distinct entities (`pl != plTarget`).
        *   Neither player's duel `startTime` can already be non-zero. This prevents double-acceptance or race conditions where the countdown has already begun.
3.  **Countdown Initiation**:
    *   If all checks pass, it captures the current server time (`time(nullptr)`).
    *   It sets the `startTimer` field in both the local player's and the opponent's duel structures to this timestamp. This synchronizes the start of the duel countdown on the server side.
    *   It calls `Player.Main/SendDuelCountdown(3000)` on both players. This sends a packet to the clients indicating the duel will begin in 3 seconds (3000 milliseconds), triggering the client-side UI countdown.

### **HandleDuelCancelledOpcode**

This method processes the `SMSG_DUEL_CANCELLED` (or equivalent) packet. It handles two distinct scenarios: forfeiting an active duel and cancelling a pending duel invitation.

1.  **Initial Validation**:
    *   Retrieves the local `Player` via `WorldSession.Main/GetPlayer`.
    *   Returns silently if `pPlayer->m_duel` is null (no duel context).
2.  **Scenario A: Forfeiting an Active Duel**:
    *   Checks if `pPlayer->m_duel->startTime` is non-zero. A non-zero `startTime` indicates the duel countdown has started or finished, meaning the duel is officially "active" or in progress.
    *   **Combat Cessation**: Calls `Unit.Main/CombatStopWithPets(true)` on both the local player and the opponent (if the opponent pointer is valid). The `true` argument likely forces the stop regardless of current combat state.
    *   **Visual Effect**: Calls `SpellCaster/CastSpell#2` with spell ID `7267` on the local player. In World of Warcraft, spell 7267 is "Beg," which plays the surrender animation. The `true` argument indicates it is triggered by the server/script, not a client cast.
    *   **Resolution**: Calls `Player.Main/DuelComplete(DUEL_WON)`. Note that the *forfeiting* player calls `DuelComplete` with `DUEL_WON`. This implies that `DuelComplete` interprets the result relative to the *opponent* or that the enum value `DUEL_WON` signifies that the *duel event* concluded with a winner determined (likely the opponent). The method then returns, skipping the cancellation logic below.
3.  **Scenario B: Cancelling a Pending Duel**:
    *   If `startTime` is zero, the duel was cancelled before the countdown began (e.g., clicking "Discard" on the invite window).
    *   Calls `Player.Main/DuelComplete(DUEL_INTERRUPTED)`. This cleans up the duel state for both parties without declaring a winner.

## Cross-Unit Boundaries

*   **`WorldSession.Main/GetPlayer`**:
    *   *Direction*: Called by `HandleDuelAcceptedOpcode` and `HandleDuelCancelledOpcode`.
    *   *Purpose*: Retrieves the `Player` object associated with the current network session. This is the primary actor in the duel logic.
*   **`Player.Main/SendDuelCountdown`**:
    *   *Direction*: Called by `HandleDuelAcceptedOpcode`.
    *   *Purpose*: Sends the countdown packet to the client. This is the only network output generated by the acceptance handler.
*   **`Player.Main/DuelComplete`**:
    *   *Direction*: Called by `HandleDuelCancelledOpcode`.
    *   *Purpose*: Finalizes the duel state. It is responsible for cleaning up `m_duel` pointers, sending final results to clients, and applying any honor/reputation changes. The caller passes the result code (`DUEL_WON` or `DUEL_INTERRUPTED`).
*   **`Unit.Main/CombatStopWithPets`**:
    *   *Direction*: Called by `HandleDuelCancelledOpcode` (during forfeit).
    *   *Purpose*: Stops combat for the player and their pets. This is necessary because a player cannot forfeit while actively attacking; the server must force the combat state to end before resolving the duel.
*   **`SpellCaster/CastSpell#2`**:
    *   *Direction*: Called by `HandleDuelCancelledOpcode` (during forfeit).
    *   *Purpose*: Casts the "Beg" spell (ID 7267) on the forfeiting player to provide visual feedback of surrender.

## Data Model

This unit does not interact directly with any database tables. All duel state is held in memory within the `Player` objects (`m_duel` structure).

## Notable Implementation Details

*   **Race Condition Prevention**: `HandleDuelAcceptedOpcode` includes a check `pl->m_duel->startTime != 0 || plTarget->m_duel->startTime != 0`. This is critical. If a player accepts, then quickly clicks again (or if packets are duplicated), the second acceptance is ignored. This prevents resetting the timer or causing inconsistent states between the two players.
*   **Forfeit Logic Asymmetry**: In `HandleDuelCancelledOpcode`, when a player forfeits, they cast the "Beg" spell and call `DuelComplete(DUEL_WON)`. It is notable that the *loser* (forfeiter) initiates the completion with a `DUEL_WON` flag. This suggests that `Player::DuelComplete` likely treats `DUEL_WON` as "The duel ended with a decisive victory" and determines who won based on who called it or the context, rather than "I won." Alternatively, it may mean "The opponent won." Maintainers should verify the semantics of `DUEL_WON` in `Player.cpp` to ensure this isn't a misleading enum name.
*   **Silent Failures**: Both handlers return silently (`return;`) if preconditions fail (e.g., no duel object, invalid opponent). No error messages are sent to the client in these cases. This assumes the client will eventually timeout or correct its state, or that these invalid packets are rare/exploitative.
*   **Opponent Null Check in Forfeit**: In the forfeit branch of `HandleDuelCancelledOpcode`, the code checks `if (pPlayer->m_duel->opponent)` before calling `CombatStopWithPets` on the opponent. However, it does *not* check for null before calling `DuelComplete`. This implies `DuelComplete` must handle a null opponent gracefully, or that a duel cannot reach the "active" state (`startTime != 0`) without a valid opponent.

## Member Reference

**HandleDuelAcceptedOpcode**
Processes the client's acceptance of a duel invitation. Validates that the player is the recipient (not the initiator), that the opponent exists and is also ready, and that the duel hasn't already started. If valid, it sets the server-side start timer for both players and sends a 3-second countdown packet to both clients via `Player.Main/SendDuelCountdown`.

**HandleDuelCancelledOpcode**
Processes the client's cancellation or forfeiture of a duel. If the duel has already started (`startTime != 0`), it treats this as a forfeit: it stops combat for both players via `Unit.Main/CombatStopWithPets`, casts the "Beg" spell (ID 7267) on the forfeiter via `SpellCaster/CastSpell#2`, and completes the duel with a `DUEL_WON` result via `Player.Main/DuelComplete`. If the duel has not yet started, it treats this as a simple cancellation and completes the duel with a `DUEL_INTERRUPTED` result via `Player.Main/DuelComplete`.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.DuelHandler

*Source:* DuelHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleDuelAcceptedOpcode | method | Player.Main/SendDuelCountdown, WorldSession.Main/GetPlayer | — | — |
| HandleDuelCancelledOpcode | method | Player.Main/DuelComplete, SpellCaster/CastSpell#2, Unit.Main/CombatStopWithPets, WorldSession.Main/GetPlayer | — | — |

---

<!-- verify: boundary-bleed | foreign: WorldSession -->
