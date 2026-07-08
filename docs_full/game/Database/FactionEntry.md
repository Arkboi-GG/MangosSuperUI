# FactionEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# FactionEntry

**Purpose & Responsibilities**

`FactionEntry` is a C++ struct defined in `DBCStructure.h` that represents a single row from the game’s `Faction.dbc` data file. It serves as the core data model for defining faction relationships, initial reputation states, and visibility rules within the World of Warcraft emulation environment.

The struct holds raw binary data parsed from the DBC file, including:
*   **Identity:** The unique faction ID (`ID`).
*   **Reputation Tracking:** A link to the specific reputation list (`reputationListID`) used by the server to track player standing.
*   **Initial State Configuration:** Four parallel arrays (`BaseRepRaceMask`, `BaseRepClassMask`, `BaseRepValue`, `ReputationFlags`) that define how different races and classes interact with this faction upon character creation. This allows Blizzard to define complex rules like "All Orcs start at Neutral with Stormwind, but Human Paladins start at Hostile."
*   **Hierarchy:** A parent faction ID (`team`) used for grouping factions.
*   **Localization:** Localized names for the faction.

Crucially, `FactionEntry` provides two helper methods, `GetIndexFitTo` and `CanHaveReputation`, which abstract the logic for interpreting these raw arrays. These methods are called by higher-level reputation management systems to determine if a player should have a reputation bar for a faction and what their starting standing should be.

**Member-by-Member Behavior**

### `GetIndexFitTo`
This method determines which of the four possible reputation configuration slots applies to a specific player character.

*   **Logic:** It iterates through indices `0` to `3`. For each index `i`, it checks two conditions:
    1.  **Race Match:** If `BaseRepRaceMask[i]` is non-zero, it performs a bitwise AND with the provided `raceMask`. If the result is non-zero, the race matches. If `BaseRepRaceMask[i]` is zero, it is treated as a wildcard (matches any race).
    2.  **Class Match:** Similarly, if `BaseRepClassMask[i]` is non-zero, it performs a bitwise AND with the provided `classMask`. If the result is non-zero, the class matches. If zero, it is a wildcard.
*   **Result:** It returns the first index `i` where both the race and class conditions are satisfied. If no index matches (which should theoretically not happen for valid player data, as index 0 often acts as a default), it returns `-1`.
*   **Usage Context:** This index is then used by callers to look up the corresponding `BaseRepValue[i]` and `ReputationFlags[i]` to initialize the player's reputation state.

### `CanHaveReputation`
This method determines whether a faction is eligible to have a tracked reputation relationship with players.

*   **Logic:** It checks if `reputationListID` is greater than or equal to `0`.
*   **Significance:** In the DBC format, `reputationListID` is a signed integer. A value of `-1` typically indicates that the faction does not have a dedicated reputation tracking entry in the database. Therefore, factions with `reputationListID < 0` are effectively "invisible" in the reputation UI and do not accumulate standing. This method provides a clean boolean check for this condition.

**Cross-Unit Boundaries**

`FactionEntry` is a passive data structure; it does not initiate calls to other units. However, it is heavily consumed by the reputation subsystem.

*   **Called by `Player.Main/ChangeReputationsForRace`:**
    *   **Direction:** Inbound call to `GetIndexFitTo`.
    *   **Collaboration:** When a player changes race (e.g., via a racial change service), the `Player` unit needs to recalculate their reputation standings. It calls `GetIndexFitTo` on relevant `FactionEntry` instances to determine the correct starting reputation index for the new race/class combination, ensuring the player's reputation history aligns with the new character identity.

*   **Called by `ReputationMgr/GetBaseReputation`:**
    *   **Direction:** Inbound call to `GetIndexFitTo`.
    *   **Collaboration:** The `ReputationMgr` uses this to calculate the initial reputation value for a player joining a faction. It retrieves the index via `GetIndexFitTo` and then accesses the `BaseRepValue` array at that index to set the player's starting standing.

*   **Called by `ReputationMgr/GetDefaultStateFlags`:**
    *   **Direction:** Inbound call to `GetIndexFitTo`.
    *   **Collaboration:** Similar to `GetBaseReputation`, this retrieves the index to look up `ReputationFlags` at that index. These flags control UI behavior, such as whether the reputation bar is hidden, whether it shows as neutral, or other display modifiers.

*   **Called by `WorldObject.Object/GetFactionReactionTo`:**
    *   **Direction:** Inbound call to `CanHaveReputation`.
    *   **Collaboration:** When determining how one object reacts to another (e.g., an NPC reacting to a player), the system first checks if the target faction actually has a reputation system. If `CanHaveReputation` returns `false`, the reaction logic may skip detailed reputation calculations and fall back to default hostility/neutrality rules defined elsewhere.

*   **Called by `WorldObject.Object/GetReactionTo`:**
    *   **Direction:** Inbound call to `CanHaveReputation`.
    *   **Collaboration:** Used during general reaction calculations between objects. It ensures that reputation-based reactions are only computed for factions that support them, optimizing performance and preventing errors when accessing invalid reputation data.

**Data Model**

`FactionEntry` does not interact with SQL database tables directly. It consumes data from the `Faction.dbc` file, which is a binary data file shipped with the game client. The struct maps directly to the columns of this DBC file. There are no SQL queries or table interactions in this unit.

**Notable Implementation Details**

1.  **Wildcard Logic in `GetIndexFitTo`:** The method treats a mask value of `0` as a wildcard ("match all"). This is a critical detail because bitwise AND with `0` always results in `0` (false). The code explicitly checks `== 0` before performing the bitwise AND to allow for this wildcard behavior. If this check were missing, a mask of `0` would never match, breaking default reputation assignments.
2.  **Signed `reputationListID`:** The use of `int32` for `reputationListID` is intentional. The negative value `-1` is a sentinel value indicating "no reputation." This is why `CanHaveReputation` checks `>= 0`. Using an unsigned type here would make this distinction impossible.
3.  **Array Parallelism:** The four arrays (`BaseRepRaceMask`, `BaseRepClassMask`, `BaseRepValue`, `ReputationFlags`) are tightly coupled. They must always be accessed using the same index returned by `GetIndexFitTo`. The struct does not enforce this coupling programmatically; it relies on the caller to use the index consistently.
4.  **No Validation:** `GetIndexFitTo` returns `-1` if no match is found. Callers must handle this case, although in practice, the DBC data is designed such that at least one entry (usually index 0) will match any valid race/class combination.
5.  **Packing:** The struct is defined within a `#pragma pack(1)` block, ensuring that the memory layout matches the exact byte alignment of the DBC file. This is essential for correctly parsing the binary data. Misalignment would lead to corrupted data reads.

## Member Reference

**GetIndexFitTo**: Iterates through the four reputation configuration slots (indices 0-3) to find the first slot where the provided `raceMask` and `classMask` match the stored masks. A mask value of `0` is treated as a wildcard (matching any race/class). Returns the matching index, or `-1` if no match is found. Used by `Player.Main/ChangeReputationsForRace`, `ReputationMgr/GetBaseReputation`, and `ReputationMgr/GetDefaultStateFlags` to determine the correct reputation initialization parameters for a player.

**CanHaveReputation**: Returns `true` if `reputationListID` is greater than or equal to `0`. This indicates that the faction has a valid reputation tracking entry in the game data. Factions with `reputationListID < 0` do not have a reputation bar and are ignored by reputation systems. Used by `WorldObject.Object/GetFactionReactionTo` and `WorldObject.Object/GetReactionTo` to filter out factions that do not support reputation tracking.

---

<!-- machine-true, projected from graph.json -->

## Map — FactionEntry

*Source:* DBCStructure.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetIndexFitTo | method | — | Player.Main/ChangeReputationsForRace, ReputationMgr/GetBaseReputation, ReputationMgr/GetDefaultStateFlags | — |
| CanHaveReputation | method | — | WorldObject.Object/GetFactionReactionTo, WorldObject.Object/GetReactionTo | — |
