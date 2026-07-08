<!-- provenance: boundary-bleed -->
# WorldSession.NPCHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.NPCHandler

## Purpose & Responsibilities

`WorldSession.NPCHandler` implements the server-side logic for player interactions with Non-Player Characters (NPCs) in the `wowvmangos` emulator. It serves as the primary entry point for handling network opcodes related to specific NPC services, including training, banking, tabard vendors, spirit healers, innkeepers, and stable masters.

The unit is responsible for:
1.  **Validating Interactions:** Ensuring the player is within range, has line-of-sight, and is interacting with a valid NPC of the correct type (e.g., verifying a `UNIT_NPC_FLAG_TRAINER` flag).
2.  **Managing State Transitions:** Interrupting channeling spells or removing auras that would prevent interaction (e.g., `AURA_INTERRUPT_INTERACTING_CANCELS`).
3.  **Processing Transactions:** Handling currency deductions for spell learning or stable slot purchases, applying reputation discounts, and updating player data.
4.  **Generating Responses:** Constructing and sending appropriate server-to-client packets (`SMSG_*`) to update the client UI, such as displaying trainer lists, opening bank windows, or confirming pet stabling.
5.  **Delegating Complex Logic:** Offloading high-level gameplay decisions (like gossip menu content or script-specific behaviors) to `Player`, `Creature`, `ScriptMgr`, and `Spell` units.

This unit does not define the core behavior of NPCs themselves but rather handles the *session-level* coordination required when a player initiates an action with them. Note that while `WorldSession.h` declares methods like `SendTrainerList`, the implementation of `SendTrainerList` resides in this unit (`NPCHandler.cpp`). Other methods declared in the header but implemented elsewhere (such as `CheckBanker` in `WorldSession.ItemHandler`) are not part of this unit's behavior.

## Member-by-Member Behavior

### Tabard Vendor Interaction
*   **`HandleTabardVendorActivateOpcode`**: Validates the target NPC as a Tabard Designer. If valid, it interrupts any channeling spells on the player and triggers `SendTabardVendorActivate`.
*   **`SendTabardVendorActivate`**: Constructs and sends the `MSG_TABARDVENDOR_ACTIVATE` packet to the client, enabling the tabard customization interface.

### Banker Interaction
*   **`HandleBankerActivateOpcode`**: Delegates validation to `WorldSession.ItemHandler.CheckBanker`. If the player is feigning death, it removes the associated aura. Finally, it calls `SendShowBank`.
*   **`SendShowBank`**: Sends the `SMSG_SHOW_BANK` packet and records the banker's GUID in the player's session state (`m_currentBankerGuid`) to track which bank window is open.

### Trainer Interaction
*   **`HandleTrainerListOpcode`**: A thin wrapper that immediately calls `SendTrainerList`.
*   **`SendTrainerList`**: The core logic for displaying the trainer menu. It:
    1.  Validates the NPC is a trainer and compatible with the player's class/race.
    2.  Retrieves spell lists from both the specific creature instance (`npc_trainer`) and the general template (`npc_trainer_template`).
    3.  Calculates reputation discounts and checks for primary profession points.
    4.  Iterates through available spells, filtering out those the player cannot learn, and uses `SendTrainerSpellHelper` to serialize each spell's data (cost, state, requirements) into the packet.
    5.  Sends the `SMSG_TRAINER_LIST` packet.
*   **`SendTrainerSpellHelper`**: A static helper function that serializes individual spell entries for the trainer list. It determines spell levels, checks for primary profession first-rank flags, and handles spell chain prerequisites (requiring previous ranks).
*   **`HandleTrainerBuySpellOpcode`**: Processes the purchase of a spell. It:
    1.  Validates the NPC, line-of-sight, and spell availability.
    2.  Checks if the player has sufficient gold (applying reputation discounts).
    3.  Removes "mounted" auras to ensure the spell cast isn't blocked.
    4.  Creates a `Spell` object (cast by the player or the trainer depending on visual effects) and prepares it.
    5.  If the spell preparation succeeds, it deducts the gold and calls `SendTrainingSuccess`; otherwise, it calls `SendTrainingFailure`.
*   **`SendTrainingSuccess`** / **`SendTrainingFailure`**: Send the respective `SMSG_TRAINER_BUY_SUCCEEDED` or `SMSG_TRAINER_BUY_FAILED` packets to confirm or deny the transaction.

### Gossip Interaction
*   **`HandleGossipHelloOpcode`**: Initiates a gossip conversation. It pauses the NPC's movement (unless flagged otherwise), checks if the NPC is a Spirit Guide (sending a query if so), and delegates to `ScriptMgr.OnGossipHello`. If no script handles it, it falls back to the default gossip menu defined in the database.
*   **`HandleGossipSelectOptionOpcode`**: Handles the selection of a gossip option. It validates coded options (requiring a password/code if specified), pauses NPC movement, and delegates to `ScriptMgr.OnGossipSelect`. If no script handles it, it calls `Player.OnGossipSelect` to handle standard database-driven gossip actions.

### Spirit Healer & Resurrection
*   **`HandleSpiritHealerActivateOpcode`**: Validates the NPC as a Spirit Healer, interrupts channeling spells, and calls `SendSpiritResurrect`.
*   **`SendSpiritResurrect`**: Executes the resurrection sequence:
    1.  Resurrects the player with 50% health and applies durability loss.
    2.  Spawns the player's corpse bones at the death location.
    3.  Determines the nearest graveyard to the corpse and the nearest graveyard to the player's ghost position.
    4.  If they differ, teleports the player to the corpse's graveyard; otherwise, updates visibility at the current position.

### Innkeeper (Binder) Interaction
*   **`HandleBinderActivateOpcode`**: Validates the player is alive and in the world, and that the NPC is an Innkeeper. It then calls `SendBindPoint`.
*   **`SendBindPoint`**: Prevents binding in instances. Casts the "Bind Sight" spell (ID 3286) on the player to set their homebind and closes any open gossip menus.

### Stable Master Interaction
*   **`HandleListStabledPetsOpcode`**: Validates the Stable Master and calls `SendStablePet`.
*   **`SendStablePet`**: Constructs the `MSG_LIST_STABLED_PETS` packet. It includes the player's current active pet (if alive and a hunter pet) and iterates through the character's cached pet data to list all stabled pets, including their level, loyalty, and slot index.
*   **`CheckStableMaster`**: A utility that verifies if the target GUID is a valid Stable Master NPC or if the player is using a GM command to bypass the NPC requirement.
*   **`HandleStablePet`**: Stables the player's current pet. It finds the first free stable slot, verifies the pet is alive and tameable, and calls `Pet.Unsummon` to save it to the database and remove it from the world.
*   **`HandleUnstablePet`**: Unstables a pet from a specific slot. It verifies the slot is occupied by a tameable pet, ensures the player doesn't already have an active pet, loads the pet from the database via `Pet.LoadPetFromDB`, and summons it.
*   **`HandleBuyStableSlot`**: Allows purchasing additional stable slots. It checks the price from `StableSlotPricesEntry`, verifies the player has enough gold, increments the player's stable slot count, and deducts the cost.
*   **`HandleStableSwapPet`**: Swaps the player's current active pet with a stabled pet. It unstables the current pet into the slot of the selected stabled pet, then loads and summons the previously stabled pet.
*   **`HandleStableRevivePet`**: Currently a stub; it accepts the opcode but performs no action.
*   **`SendStableResult`**: Sends the `SMSG_STABLE_RESULT` packet with a success or error code (e.g., `STABLE_ERR_MONEY`, `STABLE_SUCCESS_STABLE`).

### Repair Vendor Interaction
*   **`HandleRepairItemOpcode`**: Validates the NPC as a Repair vendor. It calculates the reputation discount. If a specific item GUID is provided, it repairs only that item; otherwise, it repairs all damaged items. It logs the action for debugging.

## Cross-Unit Boundaries

*   **`Player.Main`**: Heavily relied upon for validation (`GetNPCIfCanInteractWith`, `IsSpellFitByClassAndRace`), state management (`GetMoney`, `ModifyMoney`, `DurabilityRepair`), and high-level actions (`ResurrectPlayer`, `PrepareGossipMenu`).
*   **`Creature.Main`**: Used to verify NPC types (`IsTrainerOf`, `HasExtraFlag`), retrieve trainer spell data (`GetTrainerSpells`), and control NPC behavior (`PauseOutOfCombatMovement`).
*   **`SpellMgr` / `Spell.Main`**: `SpellMgr` provides static data lookups (`GetSpellEntry`, `GetSpellChainNode`). `Spell` objects are instantiated in `HandleTrainerBuySpellOpcode` to simulate the casting of the learning spell, ensuring visual effects and sound cues are triggered correctly.
*   **`ScriptMgr`**: Acts as a hook for custom scripts. `HandleGossipHelloOpcode` and `HandleGossipSelectOptionOpcode` delegate to `ScriptMgr` to allow custom C++ scripts to override default gossip behavior.
*   **`CharacterDatabaseCache`**: Used exclusively by Stable Master handlers to read/write pet data (`GetCharPetsMap`, `GetCharacterPetByOwner`) without direct SQL queries, ensuring performance and consistency.
*   **`ObjectMgr`**: Provides static data lookups for trainer greetings (`GetTrainerGreetingLocale`) and graveyard locations (`GetClosestGraveYard`).
*   **`WorldSession.ItemHandler`**: `HandleBankerActivateOpcode` calls `CheckBanker` from this sibling partial to validate banker interactions.
*   **`WorldSession.Main`**: Internal calls to `GetPlayer`, `SendPacket`, and `GetMangosString` facilitate session management and communication.

## Data Model

This unit does not perform direct SQL queries. It relies on in-memory caches and manager classes (`ObjectMgr`, `SpellMgr`, `CharacterDatabaseCache`) that abstract the underlying database tables. However, the logic implies interaction with the following conceptual tables:
*   **`npc_trainer` / `npc_trainer_template`**: Source of truth for spells offered by trainers.
*   **`character_pet`**: Source of truth for stabled pet data (accessed via `CharacterDatabaseCache`).
*   **`graveyard_shift`**: Source of truth for graveyard locations (accessed via `ObjectMgr`).
*   **`stable_slot_prices`**: Source of truth for stable slot costs (accessed via `sStableSlotPricesStore`).

No direct table columns are referenced in this unit's source code.

## Notable Implementation Details

1.  **Spell Casting for Learning**: In `HandleTrainerBuySpellOpcode`, the server creates a `Spell` object to "cast" the learning effect. This is crucial for client-side synchronization; if the spell fails to prepare (e.g., due to range or line-of-sight issues, though unlikely for self-casts), the money is *not* deducted. This prevents "double charging" bugs if the client sends multiple rapid requests.
2.  **Reputation Discounts**: Both `SendTrainerList` and `HandleTrainerBuySpellOpcode` apply `GetReputationPriceDiscount`. This ensures the displayed cost matches the actual charged amount, preventing discrepancies between UI and transaction.
3.  **Ghost Resurrection Logic**: `SendSpiritResurrect` contains complex logic to determine whether to teleport the player to the graveyard near their corpse or their ghost. This mimics the vanilla behavior where players might be resurrected at a different graveyard than where they died if they moved significantly while ghosted.
4.  **Stable Slot Management**: `HandleStablePet` manually scans for the first free slot in the `usedSlots` array derived from the database cache. This ensures pets are always placed in the lowest available slot index, maintaining consistency.
5.  **Gossip Code Validation**: `HandleGossipSelectOptionOpcode` explicitly checks if a gossip option is "coded" (requires a password). If the client sends an empty code for a coded option, the handler rejects it immediately, preventing unauthorized access to scripted gossip branches.
6.  **Stubbed Functionality**: `HandleStableRevivePet` is an empty function. This indicates that reviving dead stabled pets (a feature in some expansions or private server implementations) is not implemented in this version of the codebase.

## Member Reference

**HandleTabardVendorActivateOpcode**: Validates the target NPC as a Tabard Designer, interrupts channeling spells, and triggers the tabard vendor UI.

**SendTabardVendorActivate**: Sends the `MSG_TABARDVENDOR_ACTIVATE` packet to the client to open the tabard customization window.

**HandleBankerActivateOpcode**: Validates the banker NPC via `WorldSession.ItemHandler.CheckBanker`, removes feign death auras if present, and triggers the bank UI.

**SendShowBank**: Sends the `SMSG_SHOW_BANK` packet and stores the banker's GUID in the player's session state.

**HandleTrainerListOpcode**: Wrapper that calls `SendTrainerList` to display the trainer's spell list.

**SendTrainerSpellHelper**: Static helper that serializes individual spell data (cost, state, requirements) into the trainer list packet.

**SendTrainerList**: Validates the trainer, retrieves spell lists, applies discounts, filters spells by class/race, and sends the `SMSG_TRAINER_LIST` packet.

**SendTrainingSuccess**: Sends the `SMSG_TRAINER_BUY_SUCCEEDED` packet to confirm a spell purchase.

**SendTrainingFailure**: Sends the `SMSG_TRAINER_BUY_FAILED` packet to deny a spell purchase.

**HandleTrainerBuySpellOpcode**: Validates the spell purchase, checks funds, casts the learning spell, deducts gold, and sends success/failure feedback.

**HandleGossipHelloOpcode**: Initiates gossip, pauses NPC movement, checks for Spirit Guide status, and delegates to scripts or default gossip.

**HandleGossipSelectOptionOpcode**: Handles gossip option selection, validates codes, pauses NPC movement, and delegates to scripts or default gossip actions.

**HandleSpiritHealerActivateOpcode**: Validates the Spirit Healer NPC and triggers the resurrection sequence.

**SendSpiritResurrect**: Resurrects the player, applies durability loss, spawns corpse bones, and teleports the player to the appropriate graveyard.

**HandleBinderActivateOpcode**: Validates the Innkeeper NPC and triggers the homebind setting.

**SendBindPoint**: Prevents binding in instances, casts the Bind Sight spell, and closes gossip menus.

**HandleListStabledPetsOpcode**: Validates the Stable Master NPC and triggers the stable pet list UI.

**SendStablePet**: Sends the `MSG_LIST_STABLED_PETS` packet containing the player's current and stabled pets.

**SendStableResult**: Sends the `SMSG_STABLE_RESULT` packet with a success or error code for stable operations.

**CheckStableMaster**: Validates if the target is a Stable Master NPC or if the player is using a GM command.

**HandleStablePet**: Stables the player's current pet by finding a free slot and unsummoning the pet.

**HandleUnstablePet**: Unstables a pet from a specific slot by loading it from the database and summoning it.

**HandleBuyStableSlot**: Purchases an additional stable slot by checking funds and updating the player's slot count.

**HandleStableRevivePet**: Stubbed function; currently performs no action.

**HandleStableSwapPet**: Swaps the player's current pet with a stabled pet by unsummoning the current one and summoning the stabled one.

**HandleRepairItemOpcode**: Repairs either a specific item or all items, applying reputation discounts and validating the Repair vendor NPC.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.NPCHandler

*Source:* NPCHandler.cpp, NPCHandler.h, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleTabardVendorActivateOpcode | method | Log.Main/Out, ObjectGuid/GetString, Player.Main/GetNPCIfCanInteractWith, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/RemoveAurasWithInterruptFlags, WorldSession.Main/GetPlayer | — | — |
| SendTabardVendorActivate | method | ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Player.Main/OnGossipSelect | — |
| HandleBankerActivateOpcode | method | Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, WorldSession.ItemHandler/CheckBanker, WorldSession.Main/GetPlayer | — | — |
| SendShowBank | method | ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | ChatHandler.MiscCommands/HandleBankCommand, Player.Main/OnGossipSelect | — |
| HandleTrainerListOpcode | method | — | — | — |
| SendTrainerSpellHelper | function | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, SpellMgr/GetSpellChainNode, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsPrimaryProfessionFirstRankSpell | — | — |
| SendTrainerList | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/wpos, Creature.Main/GetCreatureInfo, Creature.Main/GetTrainerSpells, Creature.Main/GetTrainerTemplateSpells, Creature.Main/IsTrainerOf, Log.Main/Out, ObjectGuid/GetEntry, ObjectGuid/GetString, ObjectGuid/operator<<, ObjectMgr/GetTrainerGreetingLocale, Player.Main/GetFreePrimaryProfessionPoints, Player.Main/GetNPCIfCanInteractWith, Player.Main/GetReputationPriceDiscount, Player.Main/GetTrainerSpellState, Player.Main/IsSpellFitByClassAndRace, SpellCaster/InterruptSpellsWithChannelFlags, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/RemoveAurasWithInterruptFlags, WorldPacket/WorldPacket#4, WorldSession.Main/GetMangosString, WorldSession.Main/GetPlayer, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | Player.Main/OnGossipSelect | — |
| SendTrainingSuccess | method | ByteBuffer/operator<<#10, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendTrainingFailure | method | ByteBuffer/operator<<#10, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleTrainerBuySpellOpcode | method | Creature.Main/Find, Creature.Main/GetTrainerSpells, Creature.Main/GetTrainerTemplateSpells, Creature.Main/IsTrainerOf, Log.Main/Out, ObjectGuid/GetString, Player.Main/GetMoney, Player.Main/GetNPCIfCanInteractWith, Player.Main/GetReputationPriceDiscount, Player.Main/GetTrainerSpellState, Player.Main/ModifyMoney, Spell.Main/prepare, Spell.Main/Spell#2, Spell.Main/update, SpellCaster/InterruptSpellsWithChannelFlags, SpellCastTargetsInfo/setUnitTarget, SpellCastTargetsInfo/SpellCastTargets, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/RemoveSpellsCausingAura, WorldObject.Object/IsWithinLOSInMap, WorldSession.Main/GetPlayer | — | — |
| HandleGossipHelloOpcode | method | Creature.Main/GetDefaultGossipMenuId, Creature.Main/HasExtraFlag, Creature.Main/SendAreaSpiritHealerQueryOpcode, Creature.MotionMaster/PauseOutOfCombatMovement, Log.Main/Out, ObjectGuid/GetString, Player.Main/GetNPCIfCanInteractWith, Player.Main/PrepareGossipMenu, Player.Main/SendPreparedGossip, ScriptMgr/OnGossipHello, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/IsSpiritGuide, Unit.Main/RemoveAurasWithInterruptFlags, WorldSession.Main/GetPlayer | — | — |
| HandleGossipSelectOptionOpcode | method | Creature.Main/HasExtraFlag, Creature.MotionMaster/PauseOutOfCombatMovement, GossipDef/GossipOptionAction, GossipDef/GossipOptionCoded, GossipDef/GossipOptionSender, Log.Main/Out, ObjectGuid/GetString, ObjectGuid/IsAnyTypeCreature, ObjectGuid/IsGameObject, Player.Main/GetGameObjectIfCanInteractWith, Player.Main/GetNPCIfCanInteractWith, Player.Main/OnGossipSelect, ScriptMgr/OnGossipSelect, ScriptMgr/OnGossipSelect#2, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/RemoveAurasWithInterruptFlags, WorldSession.Main/GetPlayer | — | — |
| HandleSpiritHealerActivateOpcode | method | Log.Main/Out, ObjectGuid/GetString, Player.Main/GetNPCIfCanInteractWith, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/RemoveAurasWithInterruptFlags, WorldSession.Main/GetPlayer | — | — |
| SendSpiritResurrect | method | Camera/UpdateVisibilityForOwner, ObjectMgr/GetClosestGraveYard, ObjectMgr/GetWorldSafeLocFacing, Player.Main/DurabilityLossAll, Player.Main/GetCamera, Player.Main/GetCorpse, Player.Main/GetTeam, Player.Main/ResurrectPlayer, Player.Main/SpawnCorpseBones, Player.Main/TeleportTo, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/UpdateObjectVisibility | — | — |
| HandleBinderActivateOpcode | method | Log.Main/Out, Object/IsInWorld, ObjectGuid/GetString, Player.Main/GetNPCIfCanInteractWith, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/IsAlive, Unit.Main/RemoveAurasWithInterruptFlags, WorldSession.Main/GetPlayer | — | — |
| SendBindPoint | method | GossipDef/CloseGossip, Map.Main/Instanceable, SpellCaster/CastSpell#2, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | — | — |
| HandleListStabledPetsOpcode | method | Log.Main/Out, ObjectGuid/GetString, Player.Main/GetNPCIfCanInteractWith, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/RemoveAurasWithInterruptFlags, WorldSession.Main/GetPlayer | — | — |
| SendStablePet | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, ByteBuffer/operator<<#7, ByteBuffer/wpos, CharacterDatabaseCache/GetCharacterPetByOwner, CharacterDatabaseCache/GetCharPetsMap, CharacterDatabaseCache/instance, CharmInfo/GetPetNumber, Object/GetEntry, Object/GetGUIDLow, ObjectGuid/operator<<, Pet.Main/GetLoyaltyLevel, Pet.Main/GetName, Pet.Main/GetPetType, Unit.Main/GetCharmInfo, Unit.Main/GetLevel, Unit.Main/GetPet, Unit.Main/IsAlive, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | ChatHandler.MiscCommands/HandleStableCommand, Player.Main/OnGossipSelect | — |
| SendStableResult | method | ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| CheckStableMaster | method | ChatHandler.Chat/ChatHandler#2, ChatHandler.Chat/FindCommand#2, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/operator==, Player.Main/GetNPCIfCanInteractWith, WorldSession.Main/GetPlayer | — | — |
| HandleStablePet | method | CharacterDatabaseCache/GetCharPetsMap, CharacterDatabaseCache/instance, Object/GetGUIDLow, Pet.Main/GetPetType, Pet.Main/Unsummon, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/GetPet, Unit.Main/IsAlive, Unit.Main/RemoveAurasWithInterruptFlags, WorldSession.Main/GetPlayer | — | — |
| HandleUnstablePet | method | CharacterDatabaseCache/GetCharacterPetByOwner, CharacterDatabaseCache/GetCharacterPetCacheByOwnerAndId, CharacterDatabaseCache/instance, CreatureInfo/IsTameable, Object/GetGUIDLow, ObjectMgr/GetCreatureTemplate, Pet.Main/LoadPetFromDB, Pet.Main/Pet, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/GetPet, Unit.Main/RemoveAurasWithInterruptFlags, WorldSession.Main/GetPlayer | — | — |
| HandleBuyStableSlot | method | Player.Main/GetMoney, Player.Main/ModifyMoney, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/RemoveAurasWithInterruptFlags, WorldSession.Main/GetPlayer | — | — |
| HandleStableRevivePet | method | — | — | — |
| HandleStableSwapPet | method | CharacterDatabaseCache/GetCharacterPetCacheByOwnerAndId, CharacterDatabaseCache/instance, CreatureInfo/IsTameable, Object/GetGUIDLow, ObjectMgr/GetCreatureTemplate, Pet.Main/GetPetType, Pet.Main/LoadPetFromDB, Pet.Main/Pet, Pet.Main/Unsummon, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/GetPet, Unit.Main/IsAlive, Unit.Main/RemoveAurasWithInterruptFlags, WorldSession.Main/GetPlayer | — | — |
| HandleRepairItemOpcode | method | game_Objects_Item/GetPos, Log.Main/Out, ObjectGuid/GetString, Player.Main/DurabilityRepair, Player.Main/DurabilityRepairAll, Player.Main/GetItemByGuid, Player.Main/GetNPCIfCanInteractWith, Player.Main/GetReputationPriceDiscount, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/RemoveAurasWithInterruptFlags, WorldSession.Main/GetPlayer | — | — |

---

<!-- verify: boundary-bleed | foreign: SendTrainerList, WorldSession -->
