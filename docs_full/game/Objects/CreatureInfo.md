# CreatureInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureInfo

**Purpose & Responsibilities**

`CreatureInfo` is a lightweight, data-only struct defined in `CreatureDefines.h` that represents the **template** definition of a creature in the game world. It corresponds directly to the `creature_template` database table. Unlike the runtime `Creature` object (which holds dynamic state like current health, position, and AI), `CreatureInfo` holds static, immutable properties such as name, level, faction, base stats multipliers, display IDs, and innate behavioral flags.

Its primary responsibility is to provide a fast, memory-efficient lookup of creature metadata during initialization, spawning, and query handling. It contains four helper methods (`GetHighGuid`, `GetObjectGuid`, `IsTameable`, `GetTypeFlags`) that derive runtime-relevant values from this static data. These methods are called by various subsystems—including chat commands, summoning logic, pet management, and network query handlers—to determine how a creature should behave, appear, or interact without instantiating a full `Creature` object.

**Member-by-Member Behavior**

The `CreatureInfo` struct itself is a collection of public member variables mirroring database columns. The four documented methods serve specific derivation purposes:

### Guid Generation
*   **`GetHighGuid`**: A static method that returns `HIGHGUID_UNIT`. This indicates that all creatures share the same high-guid prefix in the game's object identification system. This is a constant value for the entire game version (pre-3.x).
*   **`GetObjectGuid`**: Constructs a full `ObjectGuid` for a specific creature instance. It combines the high-guid from `GetHighGuid`, the creature's `entry` ID (from the template), and a provided `lowguid` (unique instance identifier). This allows the creation of a valid GUID for a creature before it is fully instantiated in memory, which is critical for preemptive linking or command-line operations.

### Taming Logic
*   **`IsTameable`**: Determines if a creature can be tamed by a hunter. It enforces three strict conditions simultaneously:
    1.  The creature's `type` must be `CREATURE_TYPE_BEAST`.
    2.  The creature must have a non-zero `pet_family` ID.
    3.  The `static_flags1` field must include the `CREATURE_STATIC_FLAG_TAMEABLE` bit.
    All three must be true; otherwise, the creature is considered untameable regardless of other factors.

### Client-Facing Flags
*   **`GetTypeFlags`**: Translates internal server-side static flags into a bitmask suitable for transmission to the client. It checks specific bits in `static_flags1` and `static_flags2` and maps them to corresponding `CREATURE_TYPEFLAGS_*` values. This ensures the client receives only the relevant visual and interaction cues (e.g., whether the creature is tameable, visible to ghosts, or a raid boss) derived from the broader server-side configuration.

**Cross-Unit Boundaries**

`CreatureInfo` acts as a foundational data provider. It does not call into other units; instead, it is consumed by them to resolve static properties.

*   **Called by `ChatHandler.CreatureCommands`**:
    *   `HandleEscortShowWpCommand`, `HandleWpAddCommand`, `HandleWpModifyCommand`, `HandleWpShowCommand`: These commands use `GetHighGuid` and `GetObjectGuid` to construct valid GUIDs for waypoints and escort paths associated with a creature template. This allows administrators to manage pathing data using template IDs before or without spawning the actual creature.
*   **Called by `Creature.Main`**:
    *   `CreateFromProto`: Uses `GetHighGuid` to initialize the GUID of a newly spawned creature instance from its template.
*   **Called by `CreatureLinkingMgr`**:
    *   `DoCreatureLinkingEvent` and `TryFollowMaster`: Use `GetHighGuid` to identify linked creatures. This is essential for mechanics where multiple creatures act as a single unit (e.g., multi-part bosses or summoned minions).
*   **Called by `Player.Main`**:
    *   `SummonPossessedMinion`: Uses `GetHighGuid` to create the GUID for a minion summoned via possession mechanics.
*   **Called by `WorldObject.Object`**:
    *   `SummonCreature#2`: Uses `GetHighGuid` to prepare the GUID for a creature being summoned into the world.
*   **Called by `Pet.Main`**:
    *   `LoadPetFromDB`: Uses `IsTameable` to verify if a pet loaded from the database is still valid according to current template rules.
*   **Called by `Spell.Main`**:
    *   `CheckTamingSpell`: Uses `IsTameable` to validate if a target creature can be affected by a taming spell.
*   **Called by `WorldSession.NPCHandler`**:
    *   `HandleStableSwapPet` and `HandleUnstablePet`: Use `IsTameable` to ensure that only tameable creatures can be stabled or unstabled.
*   **Called by `WorldSession.QueryHandler`**:
    *   `HandleCreatureQueryOpcode`: Uses `GetTypeFlags` to populate the response packet sent to the client when it queries information about a creature. This ensures the client displays correct icons and interaction hints.

**Data Model**

`CreatureInfo` maps directly to the `creature_template` table. While the schema is not provided in the input, the struct members correspond to standard columns in this table:
*   `entry`: Primary key.
*   `name`, `subname`: Display names.
*   `level_min`, `level_max`: Level range.
*   `faction`: Faction ID.
*   `npc_flags`: Interaction flags (vendor, quest giver, etc.).
*   `display_id`: Visual appearance.
*   `type`: Creature type (beast, demon, etc.).
*   `static_flags1`, `static_flags2`: Behavioral modifiers.
*   `spells`: Array of spell IDs.
*   And many others related to stats, loot, and AI.

No other database tables are directly touched by `CreatureInfo` methods.

**Notable Implementation Details**

1.  **Static vs. Dynamic**: `CreatureInfo` is strictly static. It does not hold runtime state. Any modification to a creature's behavior or appearance at runtime must be handled by the `Creature` class or `CreatureDataAddon`, not by modifying `CreatureInfo`.
2.  **Taming Strictness**: The `IsTameable` method is highly restrictive. It requires `CREATURE_TYPE_BEAST`. This means demons, elementals, or undead cannot be tamed even if they have the `TAMEABLE` flag set, unless the code is explicitly changed. This reflects classic WoW mechanics where only beasts were tameable by hunters.
3.  **Flag Translation**: `GetTypeFlags` performs a bitwise translation from `static_flags1/2` to `typeFlags`. This decoupling allows the server to maintain detailed internal flags while sending a simplified, client-compatible subset. Note that `CREATURE_STATIC_FLAG_RAID_BOSS_MOB` is marked as "Not used by core" in comments, yet it is still translated to `CREATURE_TYPEFLAGS_RAID_BOSS_MOB` for client compatibility.
4.  **GUID Construction**: `GetObjectGuid` uses the `entry` ID as part of the GUID construction. This implies that the `entry` ID is unique across the game world for templates, which is a fundamental assumption of the engine.
5.  **No Database Access**: None of the methods perform database queries. They operate solely on the in-memory struct data. This makes them extremely fast and safe to call frequently.

## Member Reference

**GetHighGuid**
Static method returning `HIGHGUID_UNIT`. Used by `ChatHandler.CreatureCommands`, `Creature.Main`, `CreatureLinkingMgr`, `Player.Main`, and `WorldObject.Object` to establish the high-guid portion of creature GUIDs.

**GetObjectGuid**
Constructs an `ObjectGuid` using the template's `entry` and a provided `lowguid`. Currently has no external callers listed in the map, but is logically consistent with `GetHighGuid` usage patterns.

**IsTameable**
Returns `true` if the creature is a beast, has a pet family, and has the tameable static flag. Called by `Pet.Main`, `Spell.Main`, and `WorldSession.NPCHandler` to validate taming/stabling actions.

**GetTypeFlags**
Converts internal static flags to client-facing type flags. Called by `WorldSession.QueryHandler` to populate creature query responses.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureInfo

*Source:* CreatureDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetHighGuid | method | — | ChatHandler.CreatureCommands/HandleEscortShowWpCommand, ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand, Creature.Main/CreateFromProto, CreatureLinkingMgr/DoCreatureLinkingEvent, CreatureLinkingMgr/TryFollowMaster, Player.Main/SummonPossessedMinion, WorldObject.Object/SummonCreature#2 | — |
| GetObjectGuid | method | — | — | — |
| IsTameable | method | — | Pet.Main/LoadPetFromDB, Spell.Main/CheckTamingSpell, WorldSession.NPCHandler/HandleStableSwapPet, WorldSession.NPCHandler/HandleUnstablePet | — |
| GetTypeFlags | method | — | WorldSession.QueryHandler/HandleCreatureQueryOpcode | — |
