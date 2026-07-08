# CharacterDatabaseCache

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CharacterDatabaseCache

**Purpose & Responsibilities**

`CharacterDatabaseCache` is a singleton in-memory cache responsible for managing the persistent state of player-controlled pets within the `wowvmangos` server. It acts as the bridge between the `character_pet`, `pet_spell`, `pet_spell_cooldown`, and `pet_aura` database tables and the runtime game logic.

Its primary responsibilities are:
1.  **Loading:** Reading pet data from the database into memory during server startup (`LoadAll`) or specific reload commands.
2.  **Storage:** Maintaining two indexed maps of `CharacterPetCache` objects: one keyed by the pet's unique ID (`m_petsByGuid`) and one keyed by the owner's GUID (`m_petsByCharacter`).
3.  **Retrieval:** Providing methods to find specific pets by ID, owner, entry, or slot status (e.g., current active pet vs. stable pet).
4.  **Modification:** Inserting new pets, deleting existing ones, and updating slot states (e.g., marking a pet as "not in slot" when another becomes current).
5.  **ID Generation:** Calculating the next available unique pet ID.

This unit does not handle network packets or direct AI logic; it strictly manages the data model. All interactions with the database occur via the `Database` unit, and all logging occurs via the `Log.Main` unit.

## Member-by-Member Behavior

### Initialization and Loading

The cache is initialized via its constructor and destructor, which are empty placeholders. The core functionality begins with `LoadAll`.

*   **`LoadAll`**: Orchestrates the loading process. It accepts an optional `singlePetId`. If provided, it first deletes that specific pet from the cache (via `DeleteCharacterPetById`) and then reloads only that pet's data. If no ID is provided (default), it clears the entire cache and reloads all pet data from the database. It sequentially calls `LoadCharacterPet`, `LoadPetSpell`, `LoadPetSpellCooldown`, and `LoadPetAura`.

*   **`LoadCharacterPet`**: Queries the `character_pet` table.
    *   If `singlePetId` is set, it uses a parameterized query (`PQuery`) to fetch that specific row.
    *   Otherwise, it clears `m_petsByCharacter` and queries all rows.
    *   For each row, it constructs a `CharacterPetCache` object, mapping database columns to struct fields (e.g., `id`, `entry`, `owner_guid`, `level`, `xp`, etc.).
    *   It inserts the new cache object into the internal maps via `InsertCharacterPet`.
    *   Logs the number of rows loaded if performing a full load.

*   **`LoadPetSpell`**: Queries the `pet_spell` table.
    *   If `singlePetId` is set, it fetches spells for that pet.
    *   Otherwise, it iterates through `m_petsByGuid` and clears the `spells` vector for every cached pet before querying all spells.
    *   It iterates through the result set. For each spell, it retrieves the corresponding `CharacterPetCache` using `GetCharacterPetById`. If the pet exists in the cache, it appends a `PetSpellCache` struct to the pet's `spells` vector.
    *   Note: The code assumes `pet_spell` rows correspond to pets already loaded in `LoadCharacterPet`. If a pet spell exists in the DB but the pet itself was not loaded (e.g., due to a previous error or filtering), the spell is silently ignored.

*   **`LoadPetSpellCooldown`**: Queries the `pet_spell_cooldown` table.
    *   Similar to `LoadPetSpell`, it clears existing cooldowns if doing a full load.
    *   It iterates through results, finds the parent pet via `GetCharacterPetById`, and appends `PetSpellCoodown` structs to the pet's `spellCooldowns` vector.

*   **`LoadPetAura`**: Queries the `pet_aura` table.
    *   Clears existing auras if doing a full load.
    *   Iterates through results. For each aura, it finds the parent pet via `GetCharacterPetById`.
    *   It constructs a `PetAuraCache` struct, mapping fields like `caster_guid`, `spell`, `stacks`, `charges`, `duration`, and effect-specific data (`damage`, `periodicTime`).
    *   **Edge Case**: If `_auraStruct.spellId` is 0, the aura is skipped and not added to the cache. This prevents invalid auras from persisting in memory.

### Retrieval Methods

These methods provide access to the cached data. They operate on the in-memory maps and do not touch the database.

*   **`GetCharacterPetById`**: Looks up a pet in `m_petsByGuid` by its `id`. Returns `nullptr` if not found. The header comment notes this is "Very slow" because `m_petsByGuid` is a `std::map`, implying O(log N) complexity, though in practice this is negligible for typical pet counts.

*   **`GetCharacterPetCacheByOwnerAndId`**: Looks up a pet by both `ownerGuidLow` and `id`. It first finds the owner's vector in `m_petsByCharacter`, then linearly scans that vector for the matching `id`. This is less efficient than `GetCharacterPetById` but ensures ownership consistency.

*   **`GetCharacterCurrentPet`**: Finds the pet for a given owner that has `slot == PET_SAVE_AS_CURRENT`. It scans the owner's pet vector. This is used to identify the pet currently following the player.

*   **`GetCharacterPetByOwnerAndEntry`**: Finds a pet for a given owner and creature `entry`. It filters for pets where `slot == PET_SAVE_AS_CURRENT` OR `slot > PET_SAVE_LAST_STABLE_SLOT`. This logic distinguishes between active/current pets and those stored in the stable (higher slot numbers).

*   **`GetCharacterPetByOwner`**: Similar to above, but ignores the `entry`. It returns the first pet found for the owner that is either current or in a stable slot. This is often used to check if a player has *any* valid pet available for summoning or taming checks.

*   **`GetCharPetsMap`**: Returns a constant reference to the entire `m_petsByCharacter` map. Used primarily by chat handlers to list all pets for a character.

### Modification Methods

*   **`InsertCharacterPet`**: Adds a `CharacterPetCache` pointer to both `m_petsByCharacter` (under the owner's key) and `m_petsByGuid` (under the pet's ID key). This maintains the dual-index structure.

*   **`DeleteCharacterPetById`**: Removes a pet from the cache.
    1.  Finds the pet in `m_petsByGuid`.
    2.  Finds the owner's vector in `m_petsByCharacter`.
    3.  Linearly scans the owner's vector to remove the pet pointer.
    4.  Deletes the `CharacterPetCache` object (freeing memory).
    5.  Erases the entry from `m_petsByGuid`.
    *   **Note**: This method does not update the database; it only cleans up the in-memory cache. The caller is responsible for ensuring the database record is also deleted or updated.

*   **`CharacterPetSetOthersNotInSlot`**: Given a specific `CharacterPetCache` (presumably the one just made current), it finds the owner's other pets. For any other pet that has `slot == PET_SAVE_AS_CURRENT`, it changes that slot to `PET_SAVE_NOT_IN_SLOT`. This enforces the rule that a player can only have one "current" pet at a time.

*   **`GetNextAvailablePetNumber`**: Calculates the next unused pet ID. It starts from a `minimumValue` and uses `lower_bound` on `m_petsByGuid` to find the first ID >= `minimumValue`. It then increments the value until it finds a gap in the IDs. This ensures unique IDs for new pets.

## Cross-Unit Boundaries

`CharacterDatabaseCache` interacts with several other units, primarily for data persistence, logging, and high-level game logic integration.

### Database Integration
*   **Calls `Database/PQuery` and `Database/Query`**: Used in `LoadCharacterPet`, `LoadPetSpell`, `LoadPetSpellCooldown`, and `LoadPetAura` to execute SQL statements against the `CharacterDatabase`.
*   **Calls `Field/*` and `QueryResult/*`**: Used to parse the results of the database queries. Specific methods like `GetUInt32`, `GetCppString`, `GetBool`, etc., are called to populate the `CharacterPetCache` structs.

### Logging
*   **Calls `Log.Main/Out`**: Used in the `Load*` methods to report progress (e.g., "* Loading table `character_pet`") and completion (e.g., "-> %u rows loaded."). This aids in debugging startup issues.

### Game Logic Integration (Called By)
*   **`ChatHandler.ServerCommands/HandleReloadCharacterPetCommand`**: Triggers `LoadAll` to refresh pet data without restarting the server.
*   **`World/SetInitialWorldSettings`**: Calls `LoadAll` during server startup to initialize the cache.
*   **`Pet.Main/*`**:
    *   `LoadPetFromDB`: Uses `GetCharacterPetCacheByOwnerAndId`, `GetCharacterCurrentPet`, `GetCharacterPetByOwnerAndEntry`, and `CharacterPetSetOthersNotInSlot` to reconstruct a pet object from cache data.
    *   `SavePetToDB`: Uses `GetCharacterPetCacheByOwnerAndId` and `InsertCharacterPet` to update the cache after saving to the database.
    *   `DeleteFromDB#2`: Calls `DeleteCharacterPetById` to remove the pet from the cache after deletion from the database.
*   **`WorldSession.NPCHandler/*`**:
    *   `HandleStablePet`, `HandleUnstablePet`, `HandleStableSwapPet`: Use various `GetCharacterPet...` methods to manage pet slots (stable vs. current).
    *   `SendStablePet`: Uses `GetCharPetsMap` and `GetCharacterPetByOwner` to send stable information to the client.
*   **`CombatBotBaseAI/SummonPetIfNeeded`**: Uses `GetCharacterPetByOwner` to determine if a bot should summon a pet.
*   **`Spell.Main/CheckTamingSpell`**: Uses `GetCharacterPetByOwner` to verify if a player already has a tameable pet before allowing a taming spell.
*   **`ObjectMgr/GeneratePetNumber`**: Calls `GetNextAvailablePetNumber` to assign a unique ID to a newly created pet.

## Data Model

The unit operates on four database tables. The schema below reflects the live database structure.

### `character_pet`
Stores the core attributes of each pet.
*   **Primary Key**: `id` (int unsigned)
*   **Key Columns**: `owner_guid` (links to the player), `entry` (creature template ID), `slot` (determines if pet is current/stable).
*   **Attributes**: `level`, `xp`, `loyalty`, `training_points`, `current_health`, `current_mana`, `current_happiness`, `name`, `renamed`, `action_bar_data`, `teach_spell_data`, `save_time`, `reset_talents_cost`, `reset_talents_time`, `created_by_spell`, `pet_type`, `display_id`, `react_state`.
*   **Usage**: `LoadCharacterPet` reads all these fields. `InsertCharacterPet` and `DeleteCharacterPetById` manage the in-memory representation of these rows.

### `pet_spell`
Stores the spells known by a pet.
*   **Primary Key**: `guid` (pet ID), `spell` (spell ID). Composite key.
*   **Attributes**: `active` (boolean-like flag).
*   **Usage**: `LoadPetSpell` reads this table and populates the `spells` vector in `CharacterPetCache`.

### `pet_spell_cooldown`
Stores active cooldowns for pet spells.
*   **Primary Key**: `guid` (pet ID), `spell` (spell ID). Composite key.
*   **Attributes**: `time` (timestamp of cooldown expiration).
*   **Usage**: `LoadPetSpellCooldown` reads this table and populates the `spellCooldowns` vector in `CharacterPetCache`.

### `pet_aura`
Stores active auras (buffs/debuffs) on pets.
*   **Primary Key**: `guid` (pet ID), `caster_guid`, `item_guid`, `spell`. Composite key.
*   **Attributes**: `stacks`, `charges`, `base_points0-2`, `periodic_time0-2`, `max_duration`, `duration`, `effect_index_mask`.
*   **Usage**: `LoadPetAura` reads this table and populates the `auras` vector in `CharacterPetCache`. Auras with `spellId == 0` are filtered out.

## Notable Implementation Details

1.  **Singleton Pattern**: The class uses a Meyers Singleton pattern (`static CharacterDatabaseCache* i = new CharacterDatabaseCache();` inside `instance()`). This ensures global access via `sCharacterDatabaseCache` macro. Memory for the singleton is never freed, which is standard for long-running server processes.

2.  **Dual Indexing**: The cache maintains two maps:
    *   `m_petsByGuid`: `std::map<uint32, CharacterPetCache*>` for fast lookup by pet ID.
    *   `m_petsByCharacter`: `std::map<uint32, std::vector<CharacterPetCache*>>` for iterating over a player's pets.
    *   **Consistency**: `InsertCharacterPet` and `DeleteCharacterPetById` must keep both maps in sync. Failure to do so would lead to memory leaks or dangling pointers.

3.  **Memory Management**: `CharacterPetCache` objects are heap-allocated (`new CharacterPetCache`) and stored as raw pointers in the maps. `DeleteCharacterPetById` explicitly `delete`s the object. This manual memory management requires careful handling to avoid double-free or use-after-free errors, especially since multiple maps hold references to the same object.

4.  **Loading Order Dependency**: `LoadPetSpell`, `LoadPetSpellCooldown`, and `LoadPetAura` depend on `LoadCharacterPet` having already populated `m_petsByGuid`. If `LoadCharacterPet` fails or skips a pet, the subsequent loaders will silently ignore associated spells/cooldowns/auras for that pet because `GetCharacterPetById` will return `nullptr`.

5.  **Slot Logic**: The constants `PET_SAVE_AS_CURRENT` and `PET_SAVE_LAST_STABLE_SLOT` are critical for distinguishing pet states. `GetCharacterPetByOwner` and `GetCharacterPetByOwnerAndEntry` use these to filter out pets that are not currently usable (e.g., dead or improperly slotted pets). `CharacterPetSetOthersNotInSlot` enforces mutual exclusivity of the "current" slot.

6.  **Thread Safety Warning**: The header contains a `@TODO` comment: "Lock these structures for thread safety, and process stable opcodes per map". Currently, the cache is not thread-safe. Concurrent access from different threads (e.g., a player stabilizing a pet while another thread loads data) could cause race conditions. In practice, this might be mitigated by the server's main loop architecture, but it remains a potential risk.

7.  **Inefficient Lookup in `GetCharacterPetCacheByOwnerAndId`**: This method performs a linear scan of the owner's pet vector. For players with many pets, this could be slower than necessary. However, given typical pet limits, this is likely acceptable.

8.  **ID Generation Strategy**: `GetNextAvailablePetNumber` searches for the smallest integer >= `minimumValue` that is not currently in use. This avoids gaps in IDs but does not guarantee sequential IDs if pets are deleted and recreated. It relies on `m_petsByGuid` being up-to-date.

## Member Reference

*   **~CharacterDatabaseCache**: Destructor. Empty implementation.
*   **CharacterDatabaseCache**: Constructor. Empty implementation.
*   **LoadAll**: Orchestrates loading of all pet data or a single pet. Calls `DeleteCharacterPetById` if a single ID is provided, then calls `LoadCharacterPet`, `LoadPetSpell`, `LoadPetSpellCooldown`, and `LoadPetAura`.
*   **LoadCharacterPet**: Queries `character_pet` table. Populates `CharacterPetCache` objects and inserts them into the cache maps. Handles both single-pet and full-load scenarios.
*   **instance**: Static method returning the singleton instance of `CharacterDatabaseCache`.
*   **LoadPetSpell**: Queries `pet_spell` table. Attaches spell data to existing `CharacterPetCache` objects in the cache.
*   **GetCharPetsMap**: Returns a constant reference to the `m_petsByCharacter` map.
*   **LoadPetSpellCooldown**: Queries `pet_spell_cooldown` table. Attaches cooldown data to existing `CharacterPetCache` objects in the cache.
*   **LoadPetAura**: Queries `pet_aura` table. Attaches aura data to existing `CharacterPetCache` objects in the cache. Skips auras with `spellId == 0`.
*   **GetCharacterPetById**: Retrieves a `CharacterPetCache` pointer by pet ID from `m_petsByGuid`.
*   **GetCharacterPetCacheByOwnerAndId**: Retrieves a `CharacterPetCache` pointer by owner GUID and pet ID by scanning the owner's pet vector.
*   **GetCharacterCurrentPet**: Retrieves the `CharacterPetCache` for the pet with `slot == PET_SAVE_AS_CURRENT` for a given owner.
*   **GetCharacterPetByOwnerAndEntry**: Retrieves the `CharacterPetCache` for a pet with a specific `entry` and valid slot (current or stable) for a given owner.
*   **GetCharacterPetByOwner**: Retrieves the first `CharacterPetCache` with a valid slot (current or stable) for a given owner.
*   **CharacterPetSetOthersNotInSlot**: Updates the slot of other pets owned by the same owner to `PET_SAVE_NOT_IN_SLOT` if they are currently marked as `PET_SAVE_AS_CURRENT`.
*   **InsertCharacterPet**: Adds a `CharacterPetCache` pointer to both `m_petsByCharacter` and `m_petsByGuid` maps.
*   **DeleteCharacterPetById**: Removes a `CharacterPetCache` from both maps and deletes the object from memory.
*   **GetNextAvailablePetNumber**: Calculates the next unused pet ID starting from a minimum value by scanning `m_petsByGuid`.

---

<!-- machine-true, projected from graph.json -->

## Map — CharacterDatabaseCache

*Source:* CharacterDatabaseCache.cpp, CharacterDatabaseCache.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~CharacterDatabaseCache | dtor | — | — | — |
| CharacterDatabaseCache | ctor | — | — | — |
| LoadAll | method | — | ChatHandler.ServerCommands/HandleReloadCharacterPetCommand, World/SetInitialWorldSettings | — |
| LoadCharacterPet | method | Database/PQuery, Database/Query, Field/GetBool, Field/GetCppString, Field/GetInt32, Field/GetUInt32, Field/GetUInt64, Field/GetUInt8, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow | — | character_pet |
| instance | method | — | ChatHandler.CharacterCommands/HandlePetDeleteCommand, ChatHandler.CharacterCommands/HandlePetListCommand, ChatHandler.CharacterCommands/HandlePetRenameCommand, ChatHandler.ServerCommands/HandleReloadCharacterPetCommand, CombatBotBaseAI/SummonPetIfNeeded, ObjectMgr/GeneratePetNumber, Pet.Main/DeleteFromDB#2, Pet.Main/LoadPetFromDB, Pet.Main/SavePetToDB, Spell.Main/CheckTamingSpell, World/SetInitialWorldSettings, WorldSession.NPCHandler/HandleStablePet, WorldSession.NPCHandler/HandleStableSwapPet, WorldSession.NPCHandler/HandleUnstablePet, WorldSession.NPCHandler/SendStablePet | — |
| LoadPetSpell | method | Database/PQuery, Database/Query, Field/GetUInt32, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow | — | pet_spell |
| GetCharPetsMap | method | — | ChatHandler.CharacterCommands/HandlePetListCommand, WorldSession.NPCHandler/HandleStablePet, WorldSession.NPCHandler/SendStablePet | — |
| LoadPetSpellCooldown | method | Database/PQuery, Database/Query, Field/GetUInt32, Field/GetUInt64, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow | — | pet_spell_cooldown |
| LoadPetAura | method | Database/PQuery, Database/Query, Field/GetFloat, Field/GetInt32, Field/GetUInt32, Field/GetUInt64, Field/GetUInt8, Log.Main/Out, ObjectGuid/ObjectGuid#5, QueryResult/Fetch, QueryResult/NextRow | — | pet_aura |
| GetCharacterPetById | method | — | ChatHandler.CharacterCommands/HandlePetDeleteCommand, ChatHandler.CharacterCommands/HandlePetRenameCommand | — |
| GetCharacterPetCacheByOwnerAndId | method | — | Pet.Main/LoadPetFromDB, Pet.Main/SavePetToDB, WorldSession.NPCHandler/HandleStableSwapPet, WorldSession.NPCHandler/HandleUnstablePet | — |
| GetCharacterCurrentPet | method | — | Pet.Main/LoadPetFromDB | — |
| GetCharacterPetByOwnerAndEntry | method | — | Pet.Main/LoadPetFromDB | — |
| GetCharacterPetByOwner | method | — | CombatBotBaseAI/SummonPetIfNeeded, Pet.Main/LoadPetFromDB, Spell.Main/CheckTamingSpell, WorldSession.NPCHandler/HandleUnstablePet, WorldSession.NPCHandler/SendStablePet | — |
| CharacterPetSetOthersNotInSlot | method | — | Pet.Main/LoadPetFromDB | — |
| InsertCharacterPet | method | — | Pet.Main/SavePetToDB | — |
| DeleteCharacterPetById | method | — | ChatHandler.CharacterCommands/HandlePetDeleteCommand, Pet.Main/DeleteFromDB#2 | — |
| GetNextAvailablePetNumber | method | — | ObjectMgr/GeneratePetNumber | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_pet`: id int(11) unsigned PK, entry int(11) unsigned, owner_guid int(11) unsigned, display_id int(11) unsigned?, created_by_spell int(11) unsigned, pet_type tinyint(3) unsigned, level int(11) unsigned, xp int(11) unsigned, react_state tinyint(1) unsigned, loyalty_points int(11), loyalty int(11) unsigned, training_points int(11), name varchar(100)?, renamed tinyint(1) unsigned, slot int(11) unsigned, current_health int(11) unsigned, current_mana int(11) unsigned, current_happiness int(11) unsigned, save_time bigint(20) unsigned, reset_talents_cost int(11) unsigned, reset_talents_time bigint(20) unsigned, action_bar_data longtext?, teach_spell_data longtext?
- `pet_aura`: guid int(11) unsigned PK, caster_guid bigint(20) unsigned PK, item_guid int(11) unsigned PK, spell int(11) unsigned PK, stacks int(11) unsigned, charges int(11) unsigned, base_points0 float, base_points1 float, base_points2 float, periodic_time0 int(11) unsigned, periodic_time1 int(11) unsigned, periodic_time2 int(11) unsigned, max_duration int(11), duration int(11), effect_index_mask tinyint(3) unsigned
- `pet_spell`: guid int(11) unsigned PK, spell int(11) unsigned PK, active int(11) unsigned
- `pet_spell_cooldown`: guid int(11) unsigned PK, spell int(11) unsigned PK, time bigint(20) unsigned

*`?` = nullable, `PK` = primary key column.*

