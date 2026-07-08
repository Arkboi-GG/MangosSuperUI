# CharmInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CharmInfo

`CharmInfo` is a data structure embedded within `Unit` (specifically accessible via `Unit::m_charmInfo`) that manages the state, behavior, and interface elements of a controlled entity—typically a pet, charmed creature, or possessed unit. It acts as the central repository for command states (follow, attack, stay), reaction modes (passive, defensive, aggressive), action bar configuration, and faction preservation during charm effects.

This unit does not perform complex logic itself; rather, it provides getters and setters for state variables that drive AI behavior in `PetAI`, `ScriptedPetAI`, and various spell effects. It ensures that when a creature is charmed or summoned, its original faction is preserved for dispel logic, its action bar is synchronized with the owner’s client, and its command state is correctly interpreted by the game world.

## Purpose & Responsibilities

The primary responsibilities of `CharmInfo` are:

1.  **Command State Management:** Tracking whether the controlled unit is following, attacking, staying, or returning to its owner. This is critical for pet AI (`PetAI`, `ScriptedPetAI`) to determine movement goals.
2.  **Reaction State Management:** Determining how the unit reacts to aggression (Passive, Defensive, Aggressive). This influences threat generation and auto-attack behavior.
3.  **Action Bar Synchronization:** Maintaining the layout of spells/actions on the pet’s action bar, which is sent to the client and saved to the database. It supports enabling/disabling slots and setting autocast states.
4.  **Faction Preservation:** Storing the original faction template of a charmed creature before it is altered by the charm spell. This allows dispels to restore the correct faction and enables proper targeting logic for spells like `EffectDispel`.
5.  **Pet Identification:** Providing a unique pet number used for database persistence and client identification.

## Member-by-Member Behavior

### Command and Reaction States

These members manage the high-level behavioral modes of the controlled unit.

*   **`SetCommandState` / `GetCommandState` / `HasCommandState`:**
    *   `SetCommandState` assigns a `CommandStates` enum value (e.g., `CS_FOLLOW`, `CS_ATTACK`) to `m_commandState`. It is called by spell effects (`EffectSummonPet`, `OnSummon`), unit commands (`HandlePetCommand`), and aura handlers (`HandleModCharm`, `ModPossess`).
    *   `GetCommandState` retrieves the current state. It is queried by `ChatHandler` for debugging, and by `Player` initialization routines (`CharmSpellInitialize`, `PetSpellInitialize`) to set initial pet behavior.
    *   `HasCommandState` checks if the current state matches a specific enum. It is heavily used by AI classes (`PetAI`, `ScriptedPetAI`) to decide whether to initiate combat (`CanAttack`, `AttackStart`) or handle return movements (`HandleReturnMovement`).

*   **`SetReactState` / `GetReactState` / `HasReactState`:**
    *   `SetReactState` updates `m_reactState` (e.g., `REACT_PASSIVE`, `REACT_DEFENSIVE`). It is invoked by spell effects (`EffectTameCreature`), aura handlers (`HandleModCharm`, `ModPossess`), and player actions (`HandlePetAction`). Specific NPC scripts (e.g., `npc_arcanite_dragonlingAI`) also set this directly.
    *   `GetReactState` retrieves the current reaction mode. It is used by `ChatHandler` for info commands and by `Player` initialization routines.
    *   `HasReactState` checks for a specific reaction mode. It is used by `PlayerAI` to determine if the player’s pets should react to threats and by `Unit`’s own `HasReactState` wrapper.

### Action Bar Management

These members handle the client-side action bar interface for pets.

*   **`SetActionBar` / `GetActionBarEntry`:**
    *   `SetActionBar` updates a specific slot in `m_petActionBar` with a spell ID and active state (`ActiveStates`). It is called by `Unit` methods for initializing/loading action bars (`InitPetActionBar`, `LoadPetActionBar`) and by `WorldSession` handlers for player interactions (`HandlePetSetAction`, `HandlePetUnlearnOpcode`).
    *   `GetActionBarEntry` returns a pointer to a specific `UnitActionBarEntry`. It is used by `Pet` to save the action bar to the database (`SavePetToDB`) and by `WorldSession` handlers to validate or modify actions.

*   **`GetCharmSpell`:**
    *   Returns a pointer to a `CharmSpellEntry` (which inherits from `UnitActionBarEntry`) at a specific index. This is used by `Player::CharmSpellInitialize` to set up the initial spells for a charmed creature.

### Faction and Identity

*   **`GetOriginalFactionTemplate` / `SetOriginalFactionTemplate`:**
    *   `SetOriginalFactionTemplate` stores a pointer to the `FactionTemplateEntry` of the unit before it was charmed. This is called by aura handlers (`HandleModCharm`, `ModPossess`) when the charm is applied.
    *   `GetOriginalFactionTemplate` retrieves this stored template. It is crucial for spell logic: `EffectDispel` uses it to restore the faction, `CheckCast` uses it to determine if a spell can target the charmed unit, and `IsPositiveEffect` uses it to determine if a healing spell is beneficial. `CombatBotBaseAI` also uses it to validate dispel targets.

*   **`GetPetNumber`:**
    *   Retrieves the unique identifier for the pet. It is used extensively by `Pet` for database operations (`LoadPetFromDB`, `SavePetToDB`, `_SaveAuras`, `_SaveSpellCooldowns`, `_SaveSpells`) and by `WorldSession` handlers for renaming and stabilizing pets.

## Cross-Unit Boundaries

`CharmInfo` interacts with several key subsystems:

1.  **Pet System (`Pet.Main`):**
    *   **Direction:** Bidirectional.
    *   **Collaboration:** `Pet` relies on `CharmInfo` for its identity (`GetPetNumber`), command state (`HasCommandState`), and reaction state (`GetReactState`). Conversely, `Pet` saves the action bar data provided by `CharmInfo` (`GetActionBarEntry`) to the database. `Pet` also initializes the action bar via `CharmInfo` methods.

2.  **Player System (`Player.Main`):**
    *   **Direction:** Bidirectional.
    *   **Collaboration:** `Player` initializes charm and pet states using `CharmInfo` (`CharmSpellInitialize`, `PetSpellInitialize`). It queries command and reaction states to ensure consistency. `Player` also sets the action bar via `CharmInfo` (`AddSpellToActionBar`, `InitPetActionBar`).

3.  **Spell System (`Spell.Effects`, `Unit.SpellAuras`):**
    *   **Direction:** Primarily Inbound (Spells call CharmInfo).
    *   **Collaboration:** Spells that summon or charm units (`EffectSummonPet`, `EffectTameCreature`) set the command and reaction states. Aura handlers (`HandleModCharm`, `ModPossess`) set the original faction and reaction state when the charm is applied or removed.

4.  **AI System (`PetAI`, `ScriptedPetAI`, `PetEventAI`):**
    *   **Direction:** Outbound (AI calls CharmInfo).
    *   **Collaboration:** AI classes query `HasCommandState` and `HasReactState` to determine behavior. For example, `PetAI::CanAttack` checks if the pet is in an attack command state and has a defensive/aggressive reaction state.

5.  **World Session Handlers (`WorldSession.PetHandler`, `WorldSession.NPCHandler`):**
    *   **Direction:** Bidirectional.
    *   **Collaboration:** Handlers for pet actions (`HandlePetAction`, `HandlePetSetAction`) update the action bar and reaction state via `CharmInfo`. They also retrieve pet numbers for renaming and stabilizing.

## Data Model

`CharmInfo` itself does not directly interact with database tables. However, it provides data that is persisted by the `Pet` class. The relevant tables are:

*   **`pet`**: Stores pet data, including `petnumber` (retrieved via `GetPetNumber`), `actionbar` (serialized from `m_petActionBar` via `GetActionBarEntry`), and `reactstate` (derived from `GetReactState`).
*   **`character_pet`**: May store additional pet-specific character data.

The `CharmInfo` structure ensures that the data written to these tables accurately reflects the in-memory state of the pet’s commands, reactions, and action bar.

## Notable Implementation Details

*   **Action Bar Packing:** The `UnitActionBarEntry` structure packs the action ID and active state into a single `uint32` (`packedData`). The top byte represents the `ActiveStates` (disabled, enabled, passive), and the lower 24 bits represent the action ID. This compact representation is efficient for network transmission and storage.
*   **Faction Pointer Storage:** `CharmInfo` stores a raw pointer to `FactionTemplateEntry`. This assumes the `FactionTemplateEntry` object remains valid for the lifetime of the `CharmInfo`. Since faction templates are typically loaded into static memory at startup, this is safe. However, it means `CharmInfo` cannot be serialized directly to disk; only the faction ID would be persisted, and the pointer must be reconstructed upon loading.
*   **Command vs. React State:** It is crucial to distinguish between `CommandState` (what the owner *wants* the pet to do) and `ReactState` (how the pet *responds* to threats). A pet can be in `CS_FOLLOW` command state but still have `REACT_DEFENSIVE` reaction state, meaning it will follow the owner but defend itself if attacked.
*   **No Internal Logic:** `CharmInfo` contains no complex logic. All decision-making is pushed to the callers (AI, Spells, Players). This makes `CharmInfo` a pure data holder, simplifying its maintenance and reducing the risk of bugs within the structure itself.

## Member Reference

**GetPetNumber**
Returns the unique pet number (`m_petNumber`). Used by `Pet` for database operations and by `WorldSession` handlers for pet management.

**SetCommandState**
Sets the command state (`m_commandState`). Called by spell effects, unit commands, and aura handlers to define the pet’s primary behavior (follow, attack, stay).

**GetCommandState**
Retrieves the current command state. Used by `ChatHandler` and `Player` initialization routines.

**HasCommandState**
Checks if the current command state matches a specific value. Heavily used by AI classes to determine behavior.

**SetReactState**
Sets the reaction state (`m_reactState`). Called by spell effects, aura handlers, and player actions to define how the pet responds to aggression.

**GetReactState**
Retrieves the current reaction state. Used by `ChatHandler` and `Player` initialization routines.

**HasReactState**
Checks if the current reaction state matches a specific value. Used by `PlayerAI` and `Unit` wrappers.

**GetOriginalFactionTemplate**
Returns the pointer to the original faction template. Used by spell logic for dispels, targeting, and healing checks.

**SetOriginalFactionTemplate**
Stores the pointer to the original faction template. Called by aura handlers when a charm is applied.

**SetActionBar**
Updates a specific action bar slot with a spell ID and active state. Called by `Unit` initialization methods and `WorldSession` handlers.

**GetActionBarEntry**
Returns a pointer to a specific action bar entry. Used by `Pet` to save the action bar to the database and by `WorldSession` handlers to validate actions.

**GetCharmSpell**
Returns a pointer to a charm spell entry at a specific index. Used by `Player::CharmSpellInitialize` to set up initial spells for charmed creatures.

---

<!-- machine-true, projected from graph.json -->

## Map — CharmInfo

*Source:* Unit.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetPetNumber | method | — | Pet.Main/LoadPetFromDB, Pet.Main/SavePetToDB, Pet.Main/Unsummon, Pet.Main/_SaveAuras, Pet.Main/_SaveSpellCooldowns, Pet.Main/_SaveSpells, Player.Main/UnsummonPetTemporaryIfAny, WorldSession.NPCHandler/SendStablePet, WorldSession.PetHandler/HandlePetRename, WorldSession.PetHandler/SendPetNameQuery | — |
| SetCommandState | method | — | Spell.Effects/EffectSummonPet#2, spell_item/OnSummon#4, Unit.Main/HandlePetCommand, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/ModPossess | — |
| GetCommandState | method | — | ChatHandler.CreatureCommands/HandleNpcAIInfoCommand, Pet.Main/SetEnabled, Player.Main/CharmSpellInitialize, Player.Main/PetSpellInitialize | — |
| HasCommandState | method | — | Pet.Main/AddToWorld, PetAI/AttackStart, PetAI/CanAttack, PetAI/HandleReturnMovement, PetEventAI/UpdateAI, ScriptedPetAI/ResetPetCombat, ScriptedPetAI/UpdateAI | — |
| SetReactState | method | — | Map.ScriptCommands/ScriptCommand_SetReactState, npcs_special/npc_arcanite_dragonlingAI, npcs_special/npc_cannonball_runnerAI, npcs_special/npc_emerald_dragon_whelpAI, npcs_special/npc_explosive_sheepAI, npcs_special/npc_felhound_minionAI, npcs_special/npc_gnomish_battle_chickenAI, npcs_special/npc_goblin_bomb_dispenserAI, npcs_special/npc_shahramAI, Spell.Effects/EffectTameCreature, Unit.Main/SetReactState, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/ModPossess, WorldSession.PetHandler/HandlePetAction | — |
| GetReactState | method | — | ChatHandler.CreatureCommands/HandleNpcAIInfoCommand, Pet.Main/SetEnabled, Player.Main/CharmSpellInitialize, Player.Main/PetSpellInitialize, Unit.Main/GetReactState | — |
| HasReactState | method | — | PlayerAI/UpdateAI#2, Unit.Main/HasReactState | — |
| GetOriginalFactionTemplate | method | — | CombatBotBaseAI/IsValidDispelTarget, Spell.Effects/EffectDispel, Spell.Main/CheckCast, SpellEntry/IsPositiveEffect | — |
| SetOriginalFactionTemplate | method | — | Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/ModPossess | — |
| SetActionBar | method | — | Pet.Main/CleanupActionBar, Unit.Main/AddSpellToActionBar, Unit.Main/InitEmptyActionBar, Unit.Main/InitPetActionBar, Unit.Main/LoadPetActionBar, Unit.Main/RemoveSpellFromActionBar, WorldSession.PetHandler/HandlePetSetAction, WorldSession.PetHandler/HandlePetUnlearnOpcode | — |
| GetActionBarEntry | method | — | Pet.Main/CleanupActionBar, Pet.Main/SavePetToDB, WorldSession.PetHandler/HandlePetSetAction, WorldSession.PetHandler/HandlePetUnlearnOpcode | — |
| GetCharmSpell | method | — | Player.Main/CharmSpellInitialize | — |
