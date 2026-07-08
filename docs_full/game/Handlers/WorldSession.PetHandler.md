<!-- provenance: boundary-bleed -->
# WorldSession.PetHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.PetHandler

## Purpose & Responsibilities

The `WorldSession.PetHandler` partial implements the network-facing logic for all pet-related interactions in the WoWVMaNGOS server. It resides within the `WorldSession` class, which represents a single player's connection to the game world. This unit is responsible for receiving client opcodes related to pets (hunter pets, warlock summons, and charmed creatures), validating the requests against server-side state, executing the corresponding gameplay logic, and sending responses back to the client.

Key responsibilities include:
- **Action Execution:** Processing commands to make pets attack, stop attacking, or cast spells (`HandlePetAction`, `HandlePetCastSpellOpcode`).
- **Action Bar Management:** Handling changes to the pet's action bar, including adding/removing spells and toggling autocast states (`HandlePetSetAction`, `HandlePetSpellAutocastOpcode`).
- **Pet Lifecycle:** Managing pet renaming, abandonment/dismissal, and talent resetting (`HandlePetRename`, `HandlePetAbandon`, `HandlePetUnlearnOpcode`).
- **Information Queries:** Responding to client requests for pet names and validation errors (`HandlePetNameQueryOpcode`, `SendPetNameInvalid`).

This unit acts as a bridge between the raw network packets and the complex simulation logic contained in classes like `Pet`, `Creature`, `Unit`, and `CharmInfo`. It performs critical security checks (ownership verification, spell readiness, target validity) before delegating to these subsystems.

## Member-by-Member Behavior

### Pet Actions and Combat

**HandlePetAction**
This is the primary handler for general pet actions, triggered by clicking action bar buttons or issuing commands. It processes three main types of flags: `ACT_COMMAND` (follow, stay, attack), `ACT_REACTION` (passive, defensive, aggressive), and spell casts (`ACT_ENABLED`, `ACT_PASSIVE`, `ACT_DISABLED`).
1.  **Validation:** It retrieves the target `Unit` from the map using the provided GUID. It verifies that the unit exists, is alive, and is owned/charmed by the current player. It distinguishes between a `Creature` (which might be a pet) and a non-creature unit (like a charmed player).
2.  **Commands:** For `ACT_COMMAND`, it delegates to `Unit::HandlePetCommand` (in `Unit.cpp`) to handle movement or attack directives.
3.  **Reactions:** For `ACT_REACTION`, it updates the pet's reaction state via `CharmInfo::SetReactState` (in `CharmInfo.cpp`). Note that switching to `REACT_PASSIVE` interrupts non-melee spells and stops attacks.
4.  **Spell Casting:** For spell actions, it performs extensive validation:
    - Checks if the spell is known and not passive.
    - Verifies spell readiness (cooldowns).
    - Validates targets based on spell attributes (e.g., cannot cast negative spells on self, must face target if required).
    - If valid, it casts the spell via `Unit::CastSpell` (in `Unit.cpp`). On success, it checks for pet learning (`Pet::CheckLearning` in `Pet.cpp`) and stops movement if the spell prevents it. On failure, it sends a specific error code to the client.

**HandlePetStopAttack**
Handles the specific opcode for stopping a pet's current attack. It validates that the pet exists, is owned by the player, and is alive, then calls `Unit::AttackStop` (in `Unit.cpp`). This is gated behind `CLIENT_BUILD_1_6_1`.

**HandlePetCastSpellOpcode**
A specialized handler for direct spell casting, introduced in later client builds (`> 1.8.4`). Unlike `HandlePetAction`, this uses a more modern spell casting pipeline involving `SpellCastTargets`.
1.  **Validation:** Similar to `HandlePetAction`, it checks ownership, spell existence, readiness, and knowledge.
2.  **Preparation:** It prepares the spell targets for the spell system and clears the pet's moving state.
3.  **Execution:** It creates a temporary `Spell` object, checks the cast via `Spell::CheckPetCast` (in `Spell.cpp`), and if successful, prepares the spell. It also handles special audio cues (`SendPetTalk` in `Unit.cpp`) for warlock pets with a 10% random chance. If the cast fails, it cleans up the spell object and sends a failure response.

### Action Bar and Autocast Management

**HandlePetSetAction**
Manages changes to the pet's action bar slots.
1.  **Validation:** Ensures the pet is valid and enabled.
2.  **Swap Logic:** It contains specific logic to detect and validate swaps between command/reaction buttons and spell buttons. It prevents illegal removals of command/reaction buttons unless they are being swapped.
3.  **Application:** Iterates through the requested actions. For each, it toggles autocast states (`Pet::ToggleAutocast` in `Pet.cpp` or `CharmInfo::ToggleCreatureAutocast` in `CharmInfo.cpp`) based on whether the action is enabled/disabled. Finally, it updates the action bar entry via `CharmInfo::SetActionBar` (in `CharmInfo.cpp`).

**HandlePetSpellAutocastOpcode**
Handles explicit requests to toggle the autocast state of a specific spell.
1.  **Validation:** Checks if the pet knows the spell and if the spell is autocastable.
2.  **Execution:** Toggles the autocast state in both the `Pet`/`CharmInfo` internal state and the action bar representation via `CharmInfo::SetSpellAutocast` (in `CharmInfo.cpp`).

### Pet Lifecycle and Data

**HandlePetRename**
Allows hunters to rename their pets.
1.  **Prerequisites:** The pet must be a hunter pet, have the `UNIT_FLAG_PET_RENAME` flag set (indicating it was recently summoned or dismissed), and be owned by the player. Older client builds (`<= 1.6.1`) also prevent renaming while mounted.
2.  **Validation:** Uses `ObjectMgr::CheckPetName` and `IsReservedName` (both in `ObjectMgr.cpp`) to ensure the name is valid and not reserved.
3.  **Persistence:** Updates the pet's name in memory, sets the `renamed` flag in the `character_pet` table, and updates the name timestamp. It also notifies the group if the player is in one.

**HandlePetAbandon**
Handles dismissing or permanently abandoning a pet.
1.  **Hunter Pets:** If the pet is a hunter pet, it calls `Pet::Unsummon` (in `Pet.cpp`) with `PET_SAVE_AS_DELETED`, effectively removing it from the stable and database.
2.  **Other Pets:** For warlock summons or other charmed creatures, it calls `Unsummon` with `PET_SAVE_NOT_IN_SLOT` (dismissal) or `Player::Uncharm` (in `Player.cpp`) if it's a charmed mob.

**HandlePetUnlearnOpcode**
Resets a hunter pet's talents.
1.  **Cost Calculation:** Retrieves the reset cost from `Pet::GetResetTalentsCost` (in `Pet.cpp`).
2.  **Payment:** Checks if the player has enough gold. If not, sends an error. If yes, deducts the gold.
3.  **Reset Logic:** Iterates through the pet's known spells and unlearns them (except passives, which are relearned later). It resets training points based on loyalty level. It clears the action bar entries associated with spells.
4.  **Cleanup:** Relearns passive spells via `Pet::LearnPetPassives` (in `Pet.cpp`) and initializes the pet's spell list again via `Player::PetSpellInitialize` (in `Player.cpp`).

**HandlePetNameQueryOpcode**
A thin wrapper that receives a query packet and immediately calls `SendPetNameQuery`.

**SendPetNameQuery**
Constructs and sends the `SMSG_PET_NAME_QUERY_RESPONSE` packet. It validates that the pet exists, is charmed, and matches the expected pet number. It retrieves the pet's name and name timestamp to send to the client.

**SendPetNameInvalid**
Sends the `SMSG_PET_NAME_INVALID` packet to the client, indicating that a previous rename attempt failed due to the specified error code.

## Cross-Unit Boundaries

This unit relies heavily on several other subsystems to function correctly. Below are the key collaborations:

*   **`Pet` (Pet.cpp/h):** The core logic for hunter pets. `WorldSession.PetHandler` calls `Pet::IsEnabled`, `Pet::ToggleAutocast`, `Pet::CheckLearning`, `Pet::Unsummon`, `Pet::UnlearnSpell`, `Pet::LearnPetPassives`, and `Pet::GetResetTalentsCost`. It treats `Pet` as the authoritative source for pet-specific state and behavior.
*   **`CharmInfo` (CharmInfo.cpp/h):** Manages the relationship between a master and a charmed unit. `WorldSession.PetHandler` uses it to get/set reaction states (`SetReactState`), manage the action bar (`SetActionBar`, `GetActionBarEntry`), and toggle autocast for charmed creatures (`ToggleCreatureAutocast`).
*   **`Unit` (Unit.cpp/h):** The base class for all living entities. `WorldSession.PetHandler` uses it for generic operations like `CastSpell`, `AttackStop`, `IsAlive`, `HasSpell`, `IsSpellReady`, and `SendPetCastFail`. It delegates high-level commands to `Unit::HandlePetCommand`.
*   **`Creature` (Creature.cpp/h):** Used to distinguish between pets and regular mobs. `WorldSession.PetHandler` checks `Creature::IsPet` and `Creature::HasSpell`.
*   **`SpellMgr` (SpellMgr.cpp/h):** Provides access to static spell data (`GetSpellEntry`). `WorldSession.PetHandler` uses this to validate spell IDs and check attributes (e.g., `IsPassiveSpell`, `IsNeedFaceTarget`).
*   **`ObjectMgr` (ObjectMgr.cpp/h):** Used for name validation (`CheckPetName`, `IsReservedName`).
*   **`Database` (Database.cpp/h):** `HandlePetRename` directly interacts with the character database to persist name changes. It uses `PExecute` to run SQL updates.
*   **`Map` (Map.cpp/h):** Used to retrieve units from the world state (`GetUnit`, `GetAnyTypeCreature`, `GetPet`). This ensures that the pet being acted upon actually exists in the current map instance.
*   **`Log` (Log.cpp/h):** Used extensively for debugging and error reporting (e.g., logging unknown spell IDs, missing pets, or invalid actions).

## Data Model

This unit interacts with one database table:

*   **`character_pet`**: Used exclusively by `HandlePetRename`.
    *   **Columns Accessed:**
        *   `name`: Updated with the new pet name.
        *   `renamed`: Set to `'1'` to indicate the pet has been renamed.
        *   `owner_guid`: Used in the `WHERE` clause to identify the pet's owner.
        *   `id`: Used in the `WHERE` clause to identify the specific pet record (mapped from `CharmInfo::GetPetNumber`).
    *   **Usage:** The unit executes an `UPDATE` statement to change the name and set the renamed flag. It wraps this in a transaction (`BeginTransaction`/`CommitTransaction`) to ensure consistency.

## Notable Implementation Details

1.  **Client Build Gating:** Several handlers are wrapped in `#if SUPPORTED_CLIENT_BUILD > ...` preprocessor directives. For example, `HandlePetStopAttack`, `HandlePetUnlearnOpcode`, and `HandlePetSpellAutocastOpcode` are only available for clients newer than 1.6.1. `HandlePetCastSpellOpcode` is gated for clients newer than 1.8.4. This reflects the evolution of the WoW protocol over time.
2.  **Pet vs. Charmed Creature Distinction:** The code carefully distinguishes between `Pet` objects (hunter/warlock pets) and generic `Creature` objects that are charmed. For instance, `HandlePetAbandon` deletes hunter pets but only dismisses others. `HandlePetSetAction` checks `pet->IsCharmed()` to decide whether to use `CharmInfo::ToggleCreatureAutocast` or `Pet::ToggleAutocast`.
3.  **Action Bar Swap Validation:** `HandlePetSetAction` contains complex logic to prevent clients from illegally removing command/reaction buttons. It checks if a swap is occurring by comparing the actions at two positions. This is a security measure to prevent clients from manipulating the UI in ways that could bypass intended restrictions.
4.  **Spell Casting Safety:** `HandlePetAction` and `HandlePetCastSpellOpcode` perform rigorous checks before casting. They verify that the pet is not moving (clearing `UNIT_STATE_MOVING`), faces the target if required, and does not cast negative spells on itself. This prevents exploits where a player might try to force a pet to cast a spell in an invalid state.
5.  **Random Pet Talk:** In `HandlePetCastSpellOpcode`, there is a hardcoded 10% chance (`urand(0, 100) < 10`) for warlock pets to play a special "spell talk" sound instead of a generic growl. The comment notes that this is a simplification, as it technically should only happen for specific spells, but checking every spell was deemed too costly.
6.  **Transaction Safety:** `HandlePetRename` uses `CharacterDatabase.BeginTransaction()` and `CommitTransaction()` around the SQL update. This ensures that if the database fails during the update, the transaction is rolled back, preventing partial updates.
7.  **Iterator Invalidation Awareness:** In `HandlePetUnlearnOpcode`, the loop iterating over `pet->m_petSpells` increments the iterator *before* calling `pet->UnlearnSpell`. The comment explicitly warns that `UnlearnSpell` might invalidate the iterator, so this pattern is necessary to avoid undefined behavior.

## Member Reference

**HandlePetAction**
Processes general pet actions including commands (attack, follow), reaction state changes (passive, defensive, aggressive), and spell casting. Validates ownership, spell readiness, and target legality before delegating to `Unit` and `Pet` methods.

**HandlePetNameQueryOpcode**
Receives a pet name query packet from the client and immediately forwards the GUID and pet number to `SendPetNameQuery`.

**SendPetNameQuery**
Validates the pet's existence and charm status, then constructs and sends the `SMSG_PET_NAME_QUERY_RESPONSE` packet containing the pet's name and name timestamp.

**HandlePetSetAction**
Updates the pet's action bar. Validates swaps between command/reaction buttons and spells, toggles autocast states for enabled/disabled spells, and updates the `CharmInfo` action bar entries.

**HandlePetRename**
Validates and persists a pet name change. Checks for rename flags, name validity, and reserved names. Updates the `character_pet` table in the database and sets the pet's name timestamp.

**HandlePetAbandon**
Dismisses or permanently abandons a pet. Hunter pets are deleted from the stable; other pets are dismissed or uncharmed.

**HandlePetStopAttack**
Stops the pet's current attack. Validates ownership and alive status before calling `Unit::AttackStop`. Gated for client builds > 1.6.1.

**HandlePetUnlearnOpcode**
Resets a hunter pet's talents. Deducts gold, unlearns all non-passive spells, resets training points, clears the action bar, and relearns passives. Gated for client builds > 1.6.1.

**HandlePetSpellAutocastOpcode**
Toggles the autocast state of a specific spell on the pet. Validates spell knowledge and autocastability before updating `CharmInfo` and `Pet` states. Gated for client builds > 1.6.1.

**HandlePetCastSpellOpcode**
Handles direct spell casting for pets in newer client builds. Prepares spell targets, validates the cast, and executes the spell via the `Spell` class. Includes logic for special pet talk sounds. Gated for client builds > 1.8.4.

**SendPetNameInvalid**
Sends the `SMSG_PET_NAME_INVALID` packet to the client to report a failure in a pet rename attempt.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.PetHandler

*Source:* PetHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandlePetAction | method | CharmInfo/SetReactState, Creature.Main/IsPet, Log.Main/Out, Map.Main/GetUnit, Object/GetGUIDLow, Object/GetGuidStr, Object/GetObjectGuid, Object/GetTypeId, Object/IsPet, Object/ToCreature, ObjectGuid/GetString, ObjectGuid/IsEmpty, ObjectGuid/operator!=, Pet.Main/CheckLearning, Pet.Main/IsEnabled, SpellCaster/CastSpell, SpellCaster/InterruptNonMeleeSpells, SpellCaster/IsNoMovementSpellCasted, SpellCaster/IsSpellReady, SpellEntry/HasAttribute#3, SpellEntry/IsExplicitlySelectedUnitTarget, SpellEntry/IsNeedFaceTarget, SpellEntry/IsPassiveSpell#2, SpellEntry/IsPositiveSpell#3, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/AttackStop, Unit.Main/ClearUnitState, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetCharmInfo, Unit.Main/HandlePetCommand, Unit.Main/HasSpell, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/SendPetCastFail, Unit.Main/SetFacingTo, Unit.Main/StopMoving, WorldObject.Object/GetAngle, WorldObject.Object/GetMap, WorldObject.Object/IsFacingTarget, WorldObject.Object/IsMoving, WorldObject.Object/SetOrientation, WorldSession.Main/GetPlayer | — | — |
| HandlePetNameQueryOpcode | method | — | — | — |
| SendPetNameQuery | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, CharmInfo/GetPetNumber, Creature.Main/GetName, Map.Main/GetAnyTypeCreature, Object/GetUInt32Value, Player.Main/GetSession, Unit.Main/GetCharmInfo, WorldObject.Object/GetMap, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandlePetSetAction | method | CharmInfo/GetActionBarEntry, CharmInfo/SetActionBar, Creature.Main/HasSpell, Creature.Main/IsPet, Log.Main/Out, Map.Main/GetAnyTypeCreature, Object/GetGUIDLow, Object/GetTypeId, Pet.Main/IsEnabled, Pet.Main/ToggleAutocast, Player.Main/GetName, Unit.Main/GetCharm, Unit.Main/GetCharmInfo, Unit.Main/GetPet, Unit.Main/IsCharmed, Unit.Main/ToggleCreatureAutocast, UnitActionBarEntry/GetAction, UnitActionBarEntry/GetType, WorldObject.Object/GetMap | — | — |
| HandlePetRename | method | CharmInfo/GetPetNumber, Database/BeginTransaction, Database/CommitTransaction, Database/escape_string, Database/PExecute#2, Map.Main/GetPet, Object/GetGUIDLow, Object/GetObjectGuid, Object/HasFlag, ObjectGuid/operator!=, ObjectMgr/CheckPetName, ObjectMgr/IsReservedName, Pet.Main/GetPetType, Pet.Main/SetName, Player.Main/GetGroup, Player.Main/SetGroupUpdateFlag, Unit.Main/GetCharmInfo, Unit.Main/GetOwnerGuid, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SetUInt32Value | — | character_pet |
| HandlePetAbandon | method | Creature.Main/IsPet, Map.Main/GetUnit, Object/GetObjectGuid, Object/GetTypeId, Object/IsInWorld, ObjectGuid/operator!=, ObjectGuid/operator==, Pet.Main/GetPetType, Pet.Main/Unsummon, Unit.Main/GetCharmGuid, Unit.Main/GetCharmInfo, Unit.Main/GetOwnerGuid, Unit.Main/Uncharm, WorldObject.Object/GetMap | — | — |
| HandlePetStopAttack | method | Log.Main/Out, Map.Main/GetUnit, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/operator!=, Unit.Main/AttackStop, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| HandlePetUnlearnOpcode | method | CharmInfo/GetActionBarEntry, CharmInfo/SetActionBar, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/operator!=, Pet.Main/GetLoyaltyLevel, Pet.Main/GetPetType, Pet.Main/GetResetTalentsCost, Pet.Main/LearnPetPassives, Pet.Main/SetTP, Pet.Main/UnlearnSpell, Player.Main/GetMoney, Player.Main/ModifyMoney, Player.Main/PetSpellInitialize, Player.Main/SendBuyError, Unit.Main/GetCharmInfo, Unit.Main/GetLevel, Unit.Main/GetPet, UnitActionBarEntry/GetAction, UnitActionBarEntry/IsActionBarForSpell, WorldSession.Main/GetPlayer | — | — |
| HandlePetSpellAutocastOpcode | method | Creature.Main/HasSpell, Log.Main/Out, Map.Main/GetAnyTypeCreature, Object/GetGuidStr, ObjectGuid/GetString, ObjectGuid/operator!=, Pet.Main/ToggleAutocast, SpellEntry/IsAutocastable, Unit.Main/GetCharmGuid, Unit.Main/GetCharmInfo, Unit.Main/GetPetGuid, Unit.Main/IsCharmed, Unit.Main/SetSpellAutocast, Unit.Main/ToggleCreatureAutocast, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| HandlePetCastSpellOpcode | method | Creature.Main/HasSpell, Creature.Main/IsPet, Log.Main/Out, Map.Main/GetAnyTypeCreature, Object/GetGuidStr, ObjectGuid/GetString, ObjectGuid/operator!=, Pet.Main/CheckLearning, Pet.Main/GetPetType, Player.Main/SendClearCooldown, shared_Util/urand, Spell.Main/CheckPetCast, Spell.Main/Delete, Spell.Main/finish, Spell.Main/prepare#2, Spell.Main/Spell#2, SpellCaster/IsSpellReady, SpellCaster/IsSpellReady#2, SpellCastTargetsInfo/operator=, SpellCastTargetsInfo/PrepareForSpellSystem, SpellEntry/IsPassiveSpell#2, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/ClearUnitState, Unit.Main/GetCharmGuid, Unit.Main/GetPetGuid, Unit.Main/SendPetAIReaction, Unit.Main/SendPetCastFail, Unit.Main/SendPetTalk, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| SendPetNameInvalid | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_pet`: id int(11) unsigned PK, entry int(11) unsigned, owner_guid int(11) unsigned, display_id int(11) unsigned?, created_by_spell int(11) unsigned, pet_type tinyint(3) unsigned, level int(11) unsigned, xp int(11) unsigned, react_state tinyint(1) unsigned, loyalty_points int(11), loyalty int(11) unsigned, training_points int(11), name varchar(100)?, renamed tinyint(1) unsigned, slot int(11) unsigned, current_health int(11) unsigned, current_mana int(11) unsigned, current_happiness int(11) unsigned, save_time bigint(20) unsigned, reset_talents_cost int(11) unsigned, reset_talents_time bigint(20) unsigned, action_bar_data longtext?, teach_spell_data longtext?

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: UPDATE, WorldSession -->
