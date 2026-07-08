<!-- provenance: boundary-bleed -->
# WorldSession.SpellHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.SpellHandler

## Purpose & Responsibilities

`WorldSession.SpellHandler` (implemented in `SpellHandler.cpp`) provides the network-layer entry points for spell-related actions initiated by a client. As part of the `WorldSession` class, which manages a single authenticated player connection, this unit acts as a gatekeeper. Its primary responsibility is to validate incoming opcodes, enforce basic security and state checks (such as combat status, remote control states, item ownership, and spell knowledge), and delegate the complex game-logic execution to the `Spell`, `Player`, `Unit`, and `GameObject` subsystems.

This unit handles:
1.  **Item Usage:** Validating and triggering spells attached to items (`HandleUseItemOpcode`).
2.  **Item Opening:** Processing locked containers and wrapped gifts (`HandleOpenItemOpcode`).
3.  **Spell Casting:** Initiating new spell casts, including target resolution and rank selection (`HandleCastSpellOpcode`).
4.  **Spell Interruption:** Canceling active casts, channeling spells, and removing buffs/debuffs (`HandleCancelCastOpcode`, `HandleCancelAuraOpcode`, `HandleCancelChanneling`, etc.).
5.  **Pet Spell Management:** Allowing players to remove auras from their pets (`HandlePetCancelAuraOpcode`).
6.  **Self-Resurrection:** Triggering self-res spells upon death confirmation (`HandleSelfResOpcode`).
7.  **GameObject Interaction:** Validating and processing interactions with world objects (`HandleGameObjectUseOpcode`).

## Member-by-Member Behavior

### Item Interaction

**HandleUseItemOpcode**
Processes the `CMSG_USE_ITEM` opcode. It validates that the player is controlling themselves (`Player.Main/IsSelfMover`), retrieves the item from the specified bag/slot, and verifies the item prototype exists. It enforces several constraints:
*   The specific spell slot requested must be valid and triggered by "use".
*   Items requiring equipment must be equipped (`game_Objects_Item/IsEquipped`).
*   The player must be able to use the item (`Player.Main/CanUseItem`).
*   Items cannot be used if they are currently in a trade window (`game_Objects_Item/IsInTrade`).
*   If the player is in combat (`Unit.Main/IsInCombat`), only combat-compatible spells are allowed; otherwise, it fails with `EQUIP_ERR_NOT_IN_COMBAT`.
*   It handles binding logic for items that bind on use/pickup/quest, setting the soulbound flag if necessary (`game_Objects_Item/SetBinding`).
*   It validates targets and checks for shapeshift restrictions (preventing item use in beast form unless equipped, depending on client build).
*   Finally, it delegates the actual casting to `Player.Main/CastItemUseSpell`.

**HandleOpenItemOpcode**
Processes the `CMSG_OPEN_ITEM` opcode. It validates the player is alive, not flying a taxi, and controls themselves. It checks if the item is locked; if so, it verifies the lock ID exists and doesn't require skill checks (pickpocketing/disarming are handled elsewhere or blocked here if skills are missing).
*   **Gift Handling:** If the item is wrapped (`ITEM_DYNFLAG_WRAPPED`), it queries the `character_gifts` table to retrieve the underlying item ID and flags. It updates the item's entry and flags, removes the gift creator GUID, and deletes the record from `character_gifts`. If no record exists, it logs an error and destroys the item.
*   **Container Opening:** If not a gift, it interrupts any non-melee spells (`SpellCaster/InterruptNonMeleeSpells`) and triggers the loot window via `Player.Main/SendLoot`.

### Spell Casting & Targeting

**HandleCastSpellOpcode**
Processes the `CMSG_CAST_SPELL` opcode. It performs critical anti-cheat validation:
*   Verifies the spell exists in the DBC (`SpellMgr/GetSpellEntry`).
*   Ensures the player actually knows the spell and it is not passive (`Player.Main/HasActiveSpell`, `SpellEntry/IsPassiveSpell`). If invalid, it logs a potential cheat attempt.
*   Prepares spell targets (`SpellCastTargetsInfo/PrepareForSpellSystem`).
*   **Self-Cast Protection:** Prevents casting negative (hostile) spells on oneself if the target was explicitly selected.
*   **Rank Selection:** Automatically selects the appropriate spell rank based on the target's level using `SpellMgr/SelectAuraRankForLevel`.
*   **Loot Interruption:** If the player is looting, it releases the loot via `WorldSession.LootHandler/DoLootRelease`.
*   Creates a new `Spell` object (`Spell.Main/Spell#2`), marks it as client-started (`Spell.Main/SetClientStarted`), and prepares it for execution (`Spell.Main/prepare`).

### Spell Cancellation & Aura Management

**HandleCancelCastOpcode**
Processes `CMSG_CANCEL_CAST`. It ignores the request if the player is remotely controlling another player. It interrupts non-melee spells (`SpellCaster/InterruptNonMeleeSpells`) and melee swing spells (`SpellCaster/InterruptSpell`) associated with the current mover.

**HandleCancelAuraOpcode**
Processes `CMSG_CANCEL_AURA`. It allows players to remove positive buffs or specific negative auras.
*   **Validation:** Checks for spell attributes that prevent cancellation (`SPELL_ATTR_NO_AURA_CANCEL`, `SPELL_ATTR_DO_NOT_DISPLAY`). Passive spells cannot be canceled.
*   **Negative Spells:** Generally prevents canceling negative spells unless the player is controlling themselves (`Player.Main/IsSelfMover`) and the aura is a possession type (`SPELL_AURA_MOD_POSSESS`), or if the player is not possessed/fleeing.
*   **Channeling:** If the spell is channeled, it interrupts the current channeling spell (`SpellCaster/InterruptSpell`).
*   **Area Auras:** Prevents canceling area auras owned by other players.
*   Executes removal via `Unit.Main/RemoveAurasDueToSpellByCancel`.

**HandlePetCancelAuraOpcode**
Processes `CMSG_PET_CANCEL_AURA`. Validates that the player controls themselves and that the target GUID belongs to their pet or charm (`Unit.Main/GetPetGuid`, `Unit.Main/GetCharmGuid`). If the pet is dead, it sends a feedback error. Otherwise, it removes the specified aura from the pet (`Unit.Main/RemoveAurasDueToSpell`).

**HandleCancelChanneling**
Processes `CMSG_CANCEL_CHANNELING`. Similar to `HandleCancelCast`, it ignores remote control states. It retrieves the current channeled spell (`SpellCaster/GetCurrentSpell`) and interrupts it if it was not triggered by the server (`Spell.Main/IsTriggered`).

**HandleCancelAutoRepeatSpellOpcode**
Processes `CMSG_CANCEL_AUTO_REPEAT_SPELL`. Simply interrupts the current auto-repeat spell (typically auto-attacks or auto-casts) on the player's mover (`SpellCaster/InterruptSpell`).

**HandleCancelGrowthAuraOpcode**
A stub handler that does nothing. Likely retained for protocol compatibility.

### Utility & Special Cases

**HandleSelfResOpcode**
Processes `CMSG_SELF_RES`. Used when a player confirms self-resurrection in the spirit realm.
*   For newer clients (1.6.1+), it reads the spell ID stored in `PLAYER_SELF_RES_SPELL`, casts it via `SpellCaster/CastSpell`, and clears the field.
*   For older clients, it uses `PLAYER_FLAGS_CAN_SELF_RESURRECT` and `GetResurrectionSpellId`.

**HandleGameObjectUseOpcode**
Processes `CMSG_GAMEOBJECT_USE`. It validates the object exists, is spawned, is interactable, and is within distance. It removes auras interrupted by looting (`Unit.Main/RemoveAurasWithInterruptFlags`) and calls `GameObject/Use`.

## Cross-Unit Boundaries

*   **Player.Main:** Heavily relied upon for state validation (`IsSelfMover`, `IsInCombat`, `CanUseItem`, `HasActiveSpell`) and action execution (`CastItemUseSpell`, `SendLoot`, `DestroyItem`). The `WorldSession` acts as the bridge between the network packet and the `Player` entity.
*   **Spell.Main / SpellCaster:** The core logic for spell creation, preparation, and interruption lives here. `WorldSession` creates the `Spell` object and calls `prepare`, but the actual effect application, damage calculation, and aura management are delegated to these units.
*   **game_Objects_Item:** Used for low-level item property checks (`IsEquipped`, `IsInTrade`, `IsSoulBound`) and state modification (`SetBinding`, `SetState`).
*   **SpellMgr:** Accessed via `sSpellMgr.Instance()` to look up `SpellEntry` data from DBC stores. Critical for validating spell existence, attributes, and selecting ranks.
*   **Database:** `HandleOpenItemOpcode` directly interacts with the `CharacterDatabase` to query and delete records from `character_gifts`. This is one of the few places in this unit performing direct SQL operations.
*   **Unit.Main:** Used for high-level state checks (`IsAlive`, `IsTaxiFlying`, `IsShapeShifted`) and aura manipulation (`RemoveAurasDueToSpellByCancel`).
*   **WorldSession.LootHandler:** `HandleCastSpellOpcode` calls `DoLootRelease` from the `LootHandler` partial of `WorldSession` to interrupt looting when a spell is cast.

## Data Model

This unit interacts with one database table:

*   **`character_gifts`**: Used exclusively by `HandleOpenItemOpcode` to unwrap gift items.
    *   **Columns:** `item_guid` (PK), `item_id`, `flags`.
    *   **Usage:** When a wrapped item is opened, the unit queries this table using the item's GUID to find the actual item ID and flags to apply. After successfully unwrapping, it deletes the row to clean up the database. If the row is missing, the item is considered corrupted and destroyed.

## Notable Implementation Details

1.  **Anti-Cheat Validation in `HandleCastSpellOpcode`:** The code explicitly checks if a player is casting a spell they shouldn't have (`!_player->HasActiveSpell` or passive spells). It logs this as an error ("Player %u casts spell %u which he shouldn't have") but currently only returns without further punishment. The comment `//cheater? kick? ban?` suggests this is a known gap in enforcement.
2.  **Shapeshift Restrictions:** `HandleUseItemOpcode` contains conditional compilation (`#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`) to handle patch 1.10.0 changes where shapeshifted forms could use equipped items. This highlights the need to maintain backward compatibility with older client builds.
3.  **Gift Corruption Handling:** In `HandleOpenItemOpcode`, if a wrapped item lacks a corresponding entry in `character_gifts`, the code logs an error and immediately destroys the item (`pUser->DestroyItem`). This prevents players from exploiting orphaned gift items but results in item loss for legitimate database inconsistencies.
4.  **Remote Control Ignoring:** Many handlers (`HandleUseItemOpcode`, `HandleOpenItemOpcode`, `HandleCancelAuraOpcode`, etc.) begin with `if (!pUser->IsSelfMover()) return;`. This ensures that players controlling NPCs or other players (via possession spells) cannot perform actions intended for their own character, preventing exploits.
5.  **Loot Interruption:** `HandleCastSpellOpcode` checks if the player is looting (`UNIT_FLAG_LOOTING`) and releases the loot via `WorldSession.LootHandler/DoLootRelease` if so. This prevents players from holding a loot window open while casting spells, which could be used to manipulate loot timers or visibility.
6.  **Static SQL Statement:** `HandleOpenItemOpcode` uses a `static SqlStatementID delGifts` to cache the prepared statement for deleting gift records. This is a performance optimization to avoid re-preparing the SQL statement on every gift opening.

## Member Reference

**HandleUseItemOpcode**: Validates item usage requests, checking for combat restrictions, trade windows, shapeshift limits, and binding conditions before delegating to `Player.Main/CastItemUseSpell`.

**HandleOpenItemOpcode**: Processes container opening and gift unwrapping; queries `character_gifts` for wrapped items, updates item properties, and triggers loot windows for standard containers.

**HandleGameObjectUseOpcode**: Validates and processes interactions with Game Objects, ensuring distance, spawn status, and interaction flags are correct before calling `GameObject/Use`.

**HandleCastSpellOpcode**: Validates spell casts against player knowledge and passivity, resolves targets, selects appropriate spell ranks, interrupts looting via `WorldSession.LootHandler/DoLootRelease`, and initiates the spell via `Spell.Main/prepare`.

**HandleCancelCastOpcode**: Interrupts non-melee and melee swing spells for the player's current mover, ignoring requests during remote control.

**HandleCancelAuraOpcode**: Allows removal of positive buffs and specific negative auras, enforcing restrictions on passive spells, area auras, and possession states.

**HandlePetCancelAuraOpcode**: Removes specified auras from the player's pet or charm, verifying ownership and pet life status.

**HandleCancelGrowthAuraOpcode**: Stub handler that performs no action.

**HandleCancelAutoRepeatSpellOpcode**: Interrupts the current auto-repeat spell (e.g., auto-attack) on the player's mover.

**HandleCancelChanneling**: Interrupts the current channeled spell if it was initiated by the client, ignoring remote control states.

**HandleSelfResOpcode**: Triggers the self-resurrection spell stored in the player's fields, clearing the resurrection flag/spell ID afterward.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.SpellHandler

*Source:* SpellHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleUseItemOpcode | method | game_Objects_Item/GetProto, game_Objects_Item/IsEquipped, game_Objects_Item/IsInTrade, game_Objects_Item/IsSoulBound, game_Objects_Item/IsTargetValidForItemUse, game_Objects_Item/SetBinding, game_Objects_Item/SetState, Player.Main/CanUseItem, Player.Main/CastItemUseSpell, Player.Main/GetItemByPos, Player.Main/IsSelfMover, Player.Main/SendEquipError, Spell.Main/SendCastResult#2, SpellCastTargetsInfo/getUnitTarget, SpellCastTargetsInfo/PrepareForSpellSystem, SpellEntry/IsNonCombatSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/IsInCombat, Unit.Main/IsShapeShifted | — | — |
| HandleOpenItemOpcode | method | Database/CreateStatement, Database/PQuery, Field/GetUInt32, game_Objects_Item/GetBagSlot, game_Objects_Item/GetProto, game_Objects_Item/GetSlot, game_Objects_Item/SetState, Log.Main/Out, Object/GetGUIDLow, Object/GetObjectGuid, Object/HasFlag, Object/SetEntry, Object/SetGuidValue, ObjectGuid/ObjectGuid, Player.Main/DestroyItem, Player.Main/GetItemByPos, Player.Main/IsSelfMover, Player.Main/SendEquipError, Player.Main/SendLoot, QueryResult/Fetch, SpellCaster/InterruptNonMeleeSpells, SpellCaster/IsNonMeleeSpellCasted, SqlStatementID/SqlStatementID, Unit.Main/IsAlive, Unit.Main/IsTaxiFlying, WorldObject.Object/SetUInt32Value | — | character_gifts |
| HandleGameObjectUseOpcode | method | GameObject/GetGoType, GameObject/IsAtInteractDistance#2, GameObject/isSpawned, GameObject/PlayerCanUse, GameObject/Use, Map.Main/GetGameObject, Object/HasFlag, Object/IsDeleted, Player.Main/IsSelfMover, Unit.Main/RemoveAurasWithInterruptFlags, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | BattleBotAI.BattleBotWaypoints/WSG_AtAllianceFlag, BattleBotAI.BattleBotWaypoints/WSG_AtHordeFlag | — |
| HandleCastSpellOpcode | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Log.Main/Out, Object/GetGUIDLow, Object/HasFlag, ObjectGuid/ObjectGuid, Player.Main/GetLootGuid, Player.Main/HasActiveSpell, Spell.Main/prepare, Spell.Main/SetClientStarted, Spell.Main/Spell#2, SpellCastTargetsInfo/getUnitTarget, SpellCastTargetsInfo/PrepareForSpellSystem, SpellEntry/IsExplicitlySelectedUnitTarget, SpellEntry/IsPassiveSpell#2, SpellEntry/IsPositiveSpell#3, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/SelectAuraRankForLevel, Unit.Main/GetLevel, WorldPacket/WorldPacket#4, WorldSession.LootHandler/DoLootRelease, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleCancelCastOpcode | method | Object/GetTypeId, Player.Main/GetMover, SpellCaster/InterruptNonMeleeSpells, SpellCaster/InterruptSpell, SpellCaster/IsNextSwingSpellCasted, SpellCaster/IsNonMeleeSpellCasted | — | — |
| HandleCancelAuraOpcode | method | Object/GetObjectGuid, Object/HasFlag, ObjectGuid/operator!=, Player.Main/IsSelfMover, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetSpellProto, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell, SpellEntry/HasAreaAuraEffect, SpellEntry/HasAttribute, SpellEntry/HasAttribute#3, SpellEntry/IsChanneledSpell, SpellEntry/IsPassiveSpell#2, SpellEntry/IsPositiveSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetSpellAuraHolder#2, Unit.Main/RemoveAurasDueToSpellByCancel | — | — |
| HandlePetCancelAuraOpcode | method | Map.Main/GetAnyTypeCreature, ObjectGuid/operator!=, Player.Main/IsSelfMover, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetCharmGuid, Unit.Main/GetPetGuid, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SendPetActionFeedback, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| HandleCancelGrowthAuraOpcode | method | — | — | — |
| HandleCancelAutoRepeatSpellOpcode | method | Player.Main/GetMover, SpellCaster/InterruptSpell | — | — |
| HandleCancelChanneling | method | Object/GetTypeId, Player.Main/GetMover, Spell.Main/IsTriggered, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell | — | — |
| HandleSelfResOpcode | method | Object/GetUInt32Value, SpellCaster/CastSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, WorldObject.Object/SetUInt32Value | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_gifts`: guid int(20) unsigned, item_guid int(11) unsigned PK, item_id int(20) unsigned, flags int(20) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: WorldSession -->
