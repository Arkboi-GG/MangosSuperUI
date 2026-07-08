# custom_creatures

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# custom_creatures

## Purpose & Responsibilities

`custom_creatures.cpp` implements a collection of non-standard, server-side scripts for specific NPC entities within the WoWVMaNGOS emulator. These scripts provide quality-of-life features, administrative tools, and testing utilities that are not part of the default vanilla World of Warcraft gameplay. The unit defines gossip handlers for teleportation, item enchantment, profession learning, and premade gear/spec application, as well as AI behaviors for a combat training dummy and a stress-test summoning creature.

The unit does not interact with any database tables directly; all data is derived from in-memory stores (DBC files, object managers) or hardcoded values.

## Feature Subsystems

### Teleportation NPC
This subsystem provides a hierarchical gossip menu allowing players to teleport to various locations across Azeroth. It distinguishes between Horde and Alliance factions for city selection but shares instance, raid, and zone lists.

*   **Menu Structure:**
    *   **Main Menu:** Offers categories: Major Cities, Starting Areas, Instances, Raids, Gurubashi Arena, Zones (Kalimdor/Eastern Kingdoms).
    *   **Cities:** Faction-specific lists (e.g., Orgrimmar/Undercity/Thunderbluff for Horde; Stormwind/Ironforge/Darnassus for Alliance).
    *   **Starting Areas:** Faction-specific starting zones.
    *   **Instances/Raids/Zones:** Comprehensive lists of dungeons, raids, and open-world zones.
*   **Behavior:**
    *   `GossipHello_TeleportNPC` initializes the main menu based on the player's team (`HORDE` or `ALLIANCE`).
    *   `GossipSelect_TeleportNPC` acts as a router, passing the selected action ID to `SendDefaultMenu_TeleportNPC`.
    *   `SendDefaultMenu_TeleportNPC` handles the bulk of the logic via a large `switch` statement. It either displays a sub-menu (for categories like "Instances") or executes a teleport (for specific destinations).
    *   Teleportation uses `Player.Main/TeleportTo` with hardcoded coordinates. Upon successful selection, the gossip menu is closed.

### Enchantment NPC
This subsystem allows players to apply permanent, custom enchantments to equipped items via a gossip interface. It bypasses standard enchanting mechanics (no reagents, no skill check).

*   **Menu Structure:**
    *   **Level 1:** Select equipment slot (Chest, Cloak, Bracers, Gloves, Boots, Mainhand, Offhand).
    *   **Level 2:** Select specific enchant type for that slot (e.g., "Greater Stats" for Chest, "Crusader" for Mainhand).
*   **Validation:**
    *   `GossipSelect_EnchantNPC` retrieves the item from the specified equipment slot using `Player.Main/GetItemByPos`.
    *   For two-handed weapons, it validates that the item subclass matches axe, mace, sword, polearm, or staff.
    *   For offhand slots, it validates that the item is a shield.
    *   If validation fails, a notification is sent, and the menu closes.
*   **Application:**
    *   The `Enchant` helper function clears any existing permanent enchantment on the item and applies the new one using `game_Objects_Item/SetEnchantment`.
    *   Enchantment IDs are hardcoded integers mapped to specific stat bonuses (e.g., `WEP_CRUSADER` maps to ID 1900).

### Profession NPC
This subsystem allows players to instantly learn all recipes and max out the skill level for a chosen profession.

*   **Menu Structure:**
    *   Lists primary professions (Alchemy, Blacksmithing, etc.), gathering skills (Herbalism, Mining, Skinning), and secondary skills (Cooking, Fishing, First Aid).
*   **Logic:**
    *   `GossipSelect_ProfessionNPC` checks if the player already has the skill via `Player.Main/HasSkill`.
    *   If not, it calls `CompleteLearnProfession`, which verifies primary profession limits (max 2) unless the skill is Cooking, First Aid, or a gathering skill.
    *   `LearnAllRecipesInProfession` sets the skill value to 300 (max) and calls `LearnSkillRecipesHelper`.
    *   `LearnSkillRecipesHelper` iterates through the `SkillLineAbility` store, filtering by class mask, race mask, and valid spell entries, then teaches the spells via `Player.Main/LearnSpell`.

### Premade Gear & Spec NPCs
These subsystems apply pre-configured gear sets or talent specifications to a player based on their class.

*   **Premade Gear:**
    *   `GossipHello_PremadeGearNPC` queries `ObjectMgr/GetPlayerPremadeGearTemplates` and filters templates matching the player's class.
    *   `GossipSelect_PremadeGearNPC` triggers `ObjectMgr/ApplyPremadeGearTemplateToPlayer` and plays a visual spell effect (`SPELL_LIGHTNING_VISUAL`).
*   **Premade Spec:**
    *   Similar to gear, `GossipHello_PremadeSpecNPC` filters `ObjectMgr/GetPlayerPremadeSpecTemplates` by class.
    *   `GossipSelect_PremadeSpecNPC` triggers `ObjectMgr/ApplyPremadeSpecTemplateToPlayer` and plays the visual effect.

### Training Dummy AI (`npc_training_dummyAI`)
A specialized AI for a training dummy that manages threat and combat engagement uniquely.

*   **Behavior:**
    *   **No Aggro:** `AttackStart` and `Aggro` are overridden to prevent standard aggro mechanics. `Aggro` disables combat movement.
    *   **Attacker Tracking:** `DamageTaken` and `SpellHit` record the GUID of any unit dealing damage or casting spells on the dummy into an internal `attackers` map, timestamping the interaction.
    *   **Threat Management:** In `UpdateAI`, if the dummy is in combat, it periodically checks the attacker list. If an attacker hasn't interacted for 10 seconds, they are removed from the threat list (`_removeAttacker` and `modifyThreatPercent`).
    *   **Evade:** If the threat list becomes empty, the dummy enters evade mode (stops fighting).

### Summon Debug AI (`npc_summon_debugAI`)
A stress-testing AI that continuously summons creatures until a limit is reached.

*   **Behavior:**
    *   **Summons:** In `UpdateAI`, if the creature has a victim and hasn't reached `m_maxSummonCount` (200), it summons creature entry 12458 at its current position.
    *   **Cleanup:** `Reset` and `JustDied` iterate through the stored summon pointers and call `TemporarySummon/UnSummon` to despawn them.

## Cross-Unit Boundaries

*   **Gossip System:** All gossip functions (`GossipHello_*`, `GossipSelect_*`) rely heavily on `GossipDef` methods (`AddMenuItem`, `SendGossipMenu`, `CloseGossip`) and `PlayerMenu/GetGossipMenu` to construct and display menus. They use `Object/GetGUID` to bind the menu to the NPC.
*   **Player State:** Functions frequently access `Player.Main` methods for team affiliation (`GetTeam`), inventory (`GetItemByPos`), skills (`HasSkill`, `SetSkill`, `LearnSpell`), and session notifications (`GetSession` -> `SendNotification`).
*   **Object Manager:** `LearnSkillRecipesHelper` and Premade NPCs query `ObjectMgr` for skill line abilities, spell entries, and premade templates.
*   **Item Modification:** The Enchant system uses `game_Objects_Item` methods to clear and set enchantments.
*   **AI Framework:** The AI classes inherit from `ScriptedAI` and use its base functionality (`EnterEvadeMode`, `SetCombatMovement`). They interact with `ThreatManager` and `Unit.Main` for combat state management.
*   **Script Registration:** `AddSC_custom_creatures` registers these scripts with the global `ScriptMgr` via `Script/RegisterSelf`.

## Data Model

This unit does not access any database tables. All configuration (teleport coordinates, enchant IDs, profession skills) is hardcoded in the source or loaded from DBC files via the Object Manager.

## Notable Implementation Details

*   **Hardcoded Coordinates:** Teleport destinations are defined by static float coordinates. Maintenance requires updating these values if map geometry changes.
*   **Enchantment Validation:** The enchantment system performs basic subclass checks for weapons and shields but does not verify if the item is actually equippable by the player or if the enchantment is compatible with the item type beyond subclass.
*   **Threat Decay Logic:** The training dummy's `UpdateAI` uses `std::time(nullptr)` for timestamping, which has second-level granularity. The 10-second decay check is approximate.
*   **Memory Management in Summon Debug:** `npc_summon_debugAI` stores raw pointers to summoned creatures in a fixed-size array. It relies on `Reset` being called to clean up these pointers to prevent dangling references or memory leaks if the creature dies or resets.
*   **Faction-Specific Menus:** The teleport NPC duplicates menu construction logic for Horde and Alliance in `GossipHello_TeleportNPC` and `SendDefaultMenu_TeleportNPC` (case 100). This leads to code duplication but ensures faction-specific options (like cities) are handled correctly.
*   **Premade Template Filtering:** The premade gear/spec NPCs filter templates by `requiredClass`. If no templates match the player's class, the gossip menu will be empty except for the default text.

## Member Reference

**GossipHello_TeleportNPC**: Initializes the teleport gossip menu. Checks player team (`HORDE`/`ALLIANCE`) to determine city options. Adds menu items for categories (Cities, Starting Areas, Instances, Raids, Zones) and sends the menu.

**SendDefaultMenu_TeleportNPC**: Handles submenu navigation and teleport execution. Uses a large `switch` on `action` ID. For category actions, it adds sub-items and resends the menu. For destination actions, it closes the menu and teleports the player to hardcoded coordinates.

**GossipSelect_TeleportNPC**: Router function. Checks if `sender` is `GOSSIP_SENDER_MAIN` and delegates to `SendDefaultMenu_TeleportNPC`.

**Enchant**: Helper function to apply an enchantment. Validates item existence and enchant ID. Clears existing permanent enchantment and sets the new one. Sends success notification.

**GossipHello_EnchantNPC**: Initializes the enchantment gossip menu. Adds items for each equipment slot (Chest, Cloak, Bracers, Gloves, Boots, Mainhand, Offhand) and sends the menu.

**GossipSelect_EnchantNPC**: Handles enchantment selection. If action < 20, shows sub-menu for the selected slot. If action >= 20, retrieves the item from the corresponding slot, validates item type (e.g., 2H weapon, Shield), determines the enchantment ID, calls `Enchant`, and closes the menu.

**LearnSkillRecipesHelper**: Iterates through `SkillLineAbility` entries. Filters by skill ID, class mask, race mask, and validity. Teaches valid spells to the player.

**LearnAllRecipesInProfession**: Sets player skill to 300. Calls `LearnSkillRecipesHelper` to teach all associated recipes. Sends notification.

**GossipHello_ProfessionNPC**: Initializes the profession gossip menu. Adds items for all primary, gathering, and secondary skills. Sends the menu.

**CompleteLearnProfession**: Checks if player has free primary profession points (unless skill is Cooking/First Aid/Gathering). Calls `LearnAllRecipesInProfession` if valid.

**GossipSelect_ProfessionNPC**: Routes profession selection. Checks if player already has the skill. If not, calls `CompleteLearnProfession`. Closes menu.

**GossipHello_PremadeGearNPC**: Queries premade gear templates from `ObjectMgr`. Filters by player class. Adds matching templates to gossip menu. Sends menu.

**GossipSelect_PremadeGearNPC**: Applies selected premade gear template via `ObjectMgr`. Plays visual spell effect. Closes menu.

**GossipHello_PremadeSpecNPC**: Queries premade spec templates from `ObjectMgr`. Filters by player class. Adds matching templates to gossip menu. Sends menu.

**GossipSelect_PremadeSpecNPC**: Applies selected premade spec template via `ObjectMgr`. Plays visual spell effect. Closes menu.

**npc_training_dummyAI (ctor)**: Constructs the AI, calling `Reset` to initialize timers and attacker map.

**Reset#2**: Resets combat timer to 15000ms and clears the attacker map.

**AttackStart**: Empty override. Prevents standard attack start behavior.

**Aggro**: Disables combat movement for the dummy.

**AddAttackerToList**: Adds or updates the timestamp for an attacker's GUID in the internal map.

**DamageTaken**: Records the damaging unit as an attacker.

**SpellHit**: Records the spell caster as an attacker.

**UpdateAI#2**: Manages combat loop. If in combat, checks attacker timestamps. Removes attackers inactive for >10s from threat list. Enters evade mode if threat list is empty.

**GetAI_npc_training_dummy**: Factory function returning a new `npc_training_dummyAI` instance.

**npc_summon_debugAI (ctor)**: Constructs the AI, initializes summon count and array, calls `Reset`.

**Reset**: Clears summon count. Iterates through summon array, unsummons any existing temporary summons, and nullifies pointers.

**JustDied**: Calls `Reset` to clean up summons upon death.

**UpdateAI**: If creature has a victim and summon count < 200, summons creature 12458 at current position and increments count.

**GetAI_custom_summon_debug**: Factory function returning a new `npc_summon_debugAI` instance.

**AddSC_custom_creatures**: Registers all custom scripts (Teleport, Enchant, Profession, Premade Gear, Premade Spec, Training Dummy, Summon Debug) with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — custom_creatures

*Source:* custom_creatures.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GossipHello_TeleportNPC | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetTeam, PlayerMenu/GetGossipMenu | — | — |
| SendDefaultMenu_TeleportNPC | function | GossipDef/AddMenuItem#4, GossipDef/CloseGossip, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetTeam, Player.Main/TeleportTo, PlayerMenu/GetGossipMenu | — | — |
| GossipSelect_TeleportNPC | function | — | — | — |
| Enchant | function | game_Objects_Item/ClearEnchantment, game_Objects_Item/GetProto, game_Objects_Item/SetEnchantment, Player.Main/GetSession, WorldSession.Main/SendNotification | — | — |
| GossipHello_EnchantNPC | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetGossipTextId, PlayerMenu/GetGossipMenu | — | — |
| GossipSelect_EnchantNPC | function | game_Objects_Item/GetProto, GossipDef/AddMenuItem#4, GossipDef/CloseGossip, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetItemByPos, Player.Main/GetSession, PlayerMenu/GetGossipMenu, WorldSession.Main/SendNotification | — | — |
| LearnSkillRecipesHelper | function | ObjectMgr/GetMaxSkillLineAbilityId, ObjectMgr/GetSkillLineAbility, Player.Main/LearnSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsSpellValid, Unit.Main/GetClassMask | — | — |
| LearnAllRecipesInProfession | function | Log.Main/Out, Player.Main/GetSession, Player.Main/SetSkill, World/GetDefaultDbcLocale, WorldSession.Main/SendNotification | — | — |
| GossipHello_ProfessionNPC | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetGossipTextId, PlayerMenu/GetGossipMenu | — | — |
| CompleteLearnProfession | function | Player.Main/GetFreePrimaryProfessionPoints, Player.Main/GetSession, WorldSession.Main/SendNotification | — | — |
| GossipSelect_ProfessionNPC | function | GossipDef/CloseGossip, Player.Main/HasSkill | — | — |
| GossipHello_PremadeGearNPC | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, ObjectMgr/GetPlayerPremadeGearTemplates, Player.Main/GetGossipTextId, PlayerMenu/GetGossipMenu, Unit.Main/GetClass | — | — |
| GossipSelect_PremadeGearNPC | function | GossipDef/CloseGossip, ObjectMgr/ApplyPremadeGearTemplateToPlayer, Unit.Main/SendSpellGo | — | — |
| GossipHello_PremadeSpecNPC | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, ObjectMgr/GetPlayerPremadeSpecTemplates, Player.Main/GetGossipTextId, PlayerMenu/GetGossipMenu, Unit.Main/GetClass | — | — |
| GossipSelect_PremadeSpecNPC | function | GossipDef/CloseGossip, ObjectMgr/ApplyPremadeSpecTemplateToPlayer, Unit.Main/SendSpellGo | — | — |
| npc_training_dummyAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| AttackStart | method | — | — | — |
| Aggro | method | CreatureAI/SetCombatMovement | — | — |
| AddAttackerToList | method | Object/GetObjectGuid | — | — |
| DamageTaken | method | — | — | — |
| SpellHit | method | Unit.Main/ToUnit | — | — |
| UpdateAI#2 | method | Map.Main/GetUnit, Object/IsInWorld, ScriptedAI/EnterEvadeMode, ThreatManager/isThreatListEmpty, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/IsInCombat, Unit.Main/_removeAttacker, WorldObject.Object/GetMap | — | — |
| GetAI_npc_training_dummy | function | — | — | — |
| npc_summon_debugAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | TemporarySummon/UnSummon | — | — |
| JustDied | method | — | — | — |
| UpdateAI | method | Unit.Main/GetVictim, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_custom_summon_debug | function | — | — | — |
| AddSC_custom_creatures | function | Script/Script, ScriptMgr/RegisterSelf | custom/AddSC_zero_scripts | — |
