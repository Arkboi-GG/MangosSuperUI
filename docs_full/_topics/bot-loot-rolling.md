<!-- provenance: invented-config -->
# Bot Loot Rolling Behavior

<!-- aliases: bot loot rolls, bots roll need, make bots roll need on everything, bot need greed, bot looting -->
<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Bot Loot Rolling Behavior

When a SuperUiBot kills a creature, the server-side AI automates the entire looting sequence without requiring client interaction or manual input. The process begins when the bot’s main update loop (`AiBotAI.Main/UpdateAI`) detects a completed kill and invokes `AiBotAI.Loot/DoAutoLoot`. This method acts as the central orchestrator for loot acquisition, handling item generation, group distribution rules, gold splitting, inventory management, and equipment upgrades.

The flow proceeds as follows:

1.  **Loot Generation**: `DoAutoLoot` verifies the creature is dead and generates loot via `Creature.Main/GenerateLootForBody`. If no loot recipient is set, it forces the bot as the recipient.
2.  **Group Loot Rules**: If the bot is in a group, `DoAutoLoot` checks the group's loot method. For `GROUP_LOOT` or `NEED_BEFORE_GREED`, it calls `Group.GroupLoot` or `Group.NeedBeforeGreed` respectively. These methods iterate through generated items and initiate rolls for items meeting the quality threshold.
3.  **Gold Distribution**: Gold is split among all group members within loot range (`WorldObject.Object/IsWithinLootXPDist`) or taken entirely by the solo bot. Each member receives their share via `Player.Main/ModifyMoney` and `Player.Main/LootMoney`.
4.  **Item Storage**: Items are automatically moved to the bot's inventory using `Player.Main/AutoStoreLoot`.
5.  **Auto-Equipment**: After looting, `AiBotAI.Loot/TryAutoEquipBags` and `AiBotAI.Loot/TryAutoEquip` scan the inventory for better bags and gear, equipping them if they improve the bot's score. This behavior is governed by the `PartyBot.AutoEquip` and `BattleBot.AutoEquip` configuration keys.
6.  **Cleanup**: The corpse is marked as looted, and decay timers are adjusted via `Creature.Main/AllLootRemovedFromCorpse`.

Crucially, the bot does **not** manually click "Need" or "Greed" in a UI sense. Instead, the server-side group loot methods (`Group.GroupLoot`, `Group.NeedBeforeGreed`) determine eligibility based on item quality thresholds. If an item meets the threshold, a roll is started. The bot's participation in these rolls is implicit in the group loot logic; however, the specific *decision* to roll Need vs. Greed is not exposed as a tunable parameter for individual items in the current code. The bot effectively "rolls need" on all items that trigger a roll in `NeedBeforeGreed` mode because the server initiates the roll on behalf of eligible members, and the bot's inventory management (`AutoStoreLoot`) accepts the awarded items.

## How to Modify

### Config

Three configuration keys influence bot loot behavior, primarily regarding automatic equipment:

*   **`PartyBot.AutoEquip`** (default: `1`): When enabled, bots in party/doctrine modes will automatically equip better gear found in loot. Disabling this prevents `AiBotAI.Loot/TryAutoEquip` from swapping items.
*   **`BattleBot.AutoEquip`** (default: `1`): Similar to above, but applies to battle bots. Controls whether `AiBotAI.Loot/TryAutoEquip` runs for these bot types.
*   **`PartyBot.RandomGearLevelDifference`** (default: `10`): Used in gear scoring logic to determine if an item is significantly better. Higher values make bots less likely to swap gear for minor upgrades.

There is **no dedicated config key** to force bots to "roll need on everything" or to disable rolling entirely. The rolling behavior is tied to the group loot method (`NeedBeforeGreed` vs `GroupLoot`) and item quality thresholds defined in the group object.

### Database

No specific database tables or columns are provided in the schema for tuning bot loot rolling behavior. The loot templates themselves (defined in `creature_loot_template`, etc.) determine what items drop, but the *bot's reaction* to those drops (rolling/equipping) is handled by the C++ AI logic and config keys above. To change *what* drops, modify the standard loot tables. To change *how* the bot reacts, use Config or Code modifications.

### Code

To fundamentally change how bots interact with loot rolls (e.g., forcing Need on all items, disabling rolls, or changing the threshold), you must edit the C++ source:

1.  **`AiBotAILoot.cpp`**:
    *   **`AiBotAI.Loot/DoAutoLoot`**: This is the entry point. To prevent bots from participating in group rolls entirely, you could remove the calls to `group->GroupLoot()` and `group->NeedBeforeGreed()` and instead directly call `me->AutoStoreLoot()` on all items. However, this bypasses group fairness rules.
    *   **`AiBotAI.Loot/TryAutoEquip`**: To change the criteria for auto-equipping, modify the `ScoreItem` logic or the comparison `if (newScore <= oldScore)`.

2.  **`Group.cpp`**:
    *   **`Group.GroupLoot`** and **`Group.NeedBeforeGreed`**: These methods control *who* rolls and *when*. They check `itemProto->Quality >= uint32(m_lootThreshold)`. To make bots roll on lower-quality items, you would need to inject bot-specific logic here or modify the threshold dynamically for groups containing bots. Currently, the threshold is uniform for all group members.

3.  **`AiBotDoctrine.cpp`**:
    *   While not directly handling loot, the doctrine determines if the bot is in a group context. Ensure `ResolveDoctrine` correctly identifies group status if you are modifying group-loot-specific behavior.

## Path Reference

**AiBotAI.Loot/DoAutoLoot** (AiBotAILoot.cpp)
The primary method executing the bot's looting routine. It generates loot, handles group distribution, splits gold, stores items, and triggers auto-equipment.

**AiBotDoctrine/ResolveDoctrine** (AiBotDoctrine.cpp)
Determines the bot's current engagement doctrine (Solo, TeamAuto, Directed), which influences whether the bot operates in a group context relevant to loot sharing.

**AiBotDoctrine/MakeDoctrine** (AiBotDoctrine.cpp)
Factory function that creates the specific doctrine instance based on the resolved kind.

**World/LoadConfigSettings** (World.cpp)
Loads configuration keys including `PartyBot.AutoEquip` and `BattleBot.AutoEquip`, which gate the auto-equipment behavior post-loot.

**AiBotAI.Bridge/BridgeSendEvent** (AiBotAIBridge.cpp)
Sends loot and equipment events to the external C# bridge service for logging or external decision-making.

**AiBotAI.Main/RefreshDoctrine** (AiBotAIMain.cpp)
Updates the bot's doctrine during the main AI loop, ensuring group status changes are reflected before loot decisions are made.

**AiBotAI.Main/UpdateAI** (AiBotAIMain.cpp)
The main AI loop that eventually triggers `DoAutoLoot` upon kill completion.

**AiBotDoctrineDirected/MakeDirectedDoctrine** (AiBotDoctrineDirected.cpp)
Creates the Directed doctrine instance.

**AiBotDoctrineSolo/MakeSoloDoctrine** (AiBotDoctrineSolo.cpp)
Creates the Solo doctrine instance.

**AiBotDoctrineTeam/MakeTeamDoctrine** (AiBotDoctrineTeam.cpp)
Creates the TeamAuto doctrine instance.

**Creature.Main/GetOriginalLootRecipient** (Creature.cpp)
Returns the original player tapped for the kill, used to verify loot rights.

**Creature.Main/GetGroupLootRecipient** (Creature.cpp)
Returns the group associated with the loot recipient, crucial for applying group loot rules.

**Creature.Main/SetLootRecipient** (Creature.cpp)
Sets the player/group responsible for looting the creature.

**Creature.Main/GenerateLootForBody** (Creature.cpp)
Generates the actual loot items and gold for the corpse based on templates.

**Creature.Main/AllLootRemovedFromCorpse** (Creature.cpp)
Handles corpse decay and cleanup after all loot has been taken.

**game_Group_Group/GroupLoot** (Group.cpp)
Implements the Group Loot rule, initiating rolls for items above the quality threshold.

**game_Group_Group/NeedBeforeGreed** (Group.cpp)
Implements the Need Before Greed rule, initiating rolls for items above the quality threshold.

**Loot/clear** (LootMgr.h)
Clears the loot data structure after items have been distributed.

**Player.Main/ModifyMoney** (Player.h)
Adjusts the player's (bot's) gold balance.

**Player.Main/AutoStoreLoot** (Player.cpp)
Automatically moves looted items from the loot window into the player's inventory.

**Player.Main/LootMoney** (Player.cpp)
Logs and processes the gold received from a loot source.

**WorldObject.Object/IsWithinLootXPDist** (Object.cpp)
Checks if a group member is close enough to receive their share of the gold.

**AiBotAI.Combat/HandleCombatStalemate** (AiBotAICombat.cpp)
Handles combat deadlocks; unrelated to loot but part of the broader AI state machine.

**AiBotAI.Loot/TryAutoEquipBags** (AiBotAILoot.cpp)
Scans inventory for larger bags and equips them to increase capacity.

**AiBotAI.Loot/TryAutoEquip** (AiBotAILoot.cpp)
Scans inventory for better gear and equips it if it scores higher than current equipment.

**AiBotAI.Main/MovementInform** (AiBotAIMain.cpp)
Handles movement completion; unrelated to loot.

**AiBotAI.Movement/MoveToDestination** (AiBotAIMovement.cpp)
Core navigation method; unrelated to loot.

**azshara/Reset** (azshara.cpp)
Example script resetting a creature; unrelated to bot loot.

**boss_bug_trio/JustDied** (boss_bug_trio.cpp)
Example script preventing loot on specific bosses; unrelated to bot loot.

**LootMgr/FillLoot** (LootMgr.cpp)
Populates the loot structure with items from templates.

**LootMgr/GenerateMoneyLoot** (LootMgr.cpp)
Calculates the gold amount for the loot.

**Object/IsPet** (Pet.h)
Checks if an object is a pet; used in loot recipient determination.

**ObjectAccessor/FindPlayer** (ObjectAccessor.cpp)
Finds a player by GUID, used to verify loot recipients.

**ObjectMgr/GetGroupById** (ObjectMgr.cpp)
Retrieves a group object by ID, used to access group loot settings.

**Player.Main/SendLoot** (Player.cpp)
Standard player loot window opening; bots bypass this via `DoAutoLoot`.

**Player.Main/IsAllowedToLoot** (Player.cpp)
Checks if a player has permission to loot a corpse; bots typically force this via `SetLootRecipient`.

**Unit.Main/DealDamage** (Unit.cpp)
Processes damage; sets loot recipients during combat.

**Unit.Main/Kill** (Unit.cpp)
Handles unit death; triggers loot generation and group rewards.

**Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself** (Unit.cpp)
Identifies the player controlling the killer unit, used for loot assignment.

**WorldSession.LootHandler/DoLootRelease** (LootHandler.cpp)
Handles loot release for human players; bots do not use this path.

---

<!-- machine-true, projected from graph.json -->

## Map — Bot Loot Rolling Behavior

*Source:* AiBotAILoot.cpp, AiBotDoctrine.cpp, World.cpp, AiBotAIBridge.cpp, AiBotAIMain.cpp, AiBotDoctrineDirected.cpp, AiBotDoctrineSolo.cpp, AiBotDoctrineTeam.cpp, Creature.cpp, Group.cpp, LootMgr.h, Player.h, Player.cpp, Object.cpp, AiBotAICombat.cpp, AiBotAIMovement.cpp, azshara.cpp, boss_bug_trio.cpp, LootMgr.cpp, Pet.h +4 more
*Config keys:* PartyBot.AutoEquip (default 1), PartyBot.RandomGearLevelDifference (default 10), BattleBot.AutoEquip (default 1)
*Tables:* —

| Member | Kind | Source | Role |
|---|---|---|---|
| AiBotAI.Loot/DoAutoLoot | method | AiBotAILoot.cpp:110-254 | seed — AiBotAI.*/*Loot* |
| AiBotDoctrine/ResolveDoctrine | function | AiBotDoctrine.cpp:21-44 | seed — AiBotDoctrine/* |
| AiBotDoctrine/MakeDoctrine | function | AiBotDoctrine.cpp:46-55 | seed — AiBotDoctrine/* |
| World/LoadConfigSettings | method | World.cpp:440-1245 | seed — reads config PartyBot.AutoEquip |
| AiBotAI.Bridge/BridgeSendEvent | method | AiBotAIBridge.cpp:410-422 | related — 1 hop from a seed |
| AiBotAI.Main/RefreshDoctrine | method | AiBotAIMain.cpp:443-456 | related — 1 hop from a seed |
| AiBotAI.Main/UpdateAI | method | AiBotAIMain.cpp:458-1274 | related — 1 hop from a seed |
| AiBotDoctrineDirected/MakeDirectedDoctrine | function | AiBotDoctrineDirected.cpp:75-78 | related — 1 hop from a seed |
| AiBotDoctrineSolo/MakeSoloDoctrine | function | AiBotDoctrineSolo.cpp:92-95 | related — 1 hop from a seed |
| AiBotDoctrineTeam/MakeTeamDoctrine | function | AiBotDoctrineTeam.cpp:278-281 | related — 1 hop from a seed |
| Creature.Main/GetOriginalLootRecipient | method | Creature.cpp:1462-1465 | related — 1 hop from a seed |
| Creature.Main/GetGroupLootRecipient | method | Creature.cpp:1470-1474 | related — 1 hop from a seed |
| Creature.Main/SetLootRecipient | method | Creature.cpp:1512-1541 | related — 1 hop from a seed |
| Creature.Main/GenerateLootForBody | method | Creature.cpp:1564-1580 | related — 1 hop from a seed |
| Creature.Main/AllLootRemovedFromCorpse | method | Creature.cpp:3286-3332 | related — 1 hop from a seed |
| game_Group_Group/GroupLoot | method | Group.cpp:846-867 | related — 1 hop from a seed |
| game_Group_Group/NeedBeforeGreed | method | Group.cpp:869-890 | related — 1 hop from a seed |
| Loot/clear | method | LootMgr.h:303-334 | related — 1 hop from a seed |
| Player.Main/ModifyMoney | method | Player.h:1021-1027 | related — 1 hop from a seed |
| Player.Main/AutoStoreLoot | method | Player.cpp:20514-20543 | related — 1 hop from a seed |
| Player.Main/LootMoney | method | Player.cpp:21835-21852 | related — 1 hop from a seed |
| WorldObject.Object/IsWithinLootXPDist | method | Object.cpp:1459-1480 | related — 1 hop from a seed |
| AiBotAI.Combat/HandleCombatStalemate | method | AiBotAICombat.cpp:235-435 | related — 2 hops from a seed |
| AiBotAI.Loot/TryAutoEquipBags | method | AiBotAILoot.cpp:257-440 | related — 2 hops from a seed |
| AiBotAI.Loot/TryAutoEquip | method | AiBotAILoot.cpp:606-742 | related — 2 hops from a seed |
| AiBotAI.Main/MovementInform | method | AiBotAIMain.cpp:332-408 | related — 2 hops from a seed |
| AiBotAI.Movement/MoveToDestination | method | AiBotAIMovement.cpp:350-718 | related — 2 hops from a seed |
| azshara/Reset | method | azshara.cpp:188-205 | related — 2 hops from a seed |
| boss_bug_trio/JustDied | method | boss_bug_trio.cpp:89-108 | related — 2 hops from a seed |
| LootMgr/FillLoot | method | LootMgr.cpp:496-517 | related — 2 hops from a seed |
| LootMgr/GenerateMoneyLoot | method | LootMgr.cpp:742-753 | related — 2 hops from a seed |
| Object/IsPet | method | Pet.h:289-292 | related — 2 hops from a seed |
| ObjectAccessor/FindPlayer | method | ObjectAccessor.cpp:84-91 | related — 2 hops from a seed |
| ObjectMgr/GetGroupById | method | ObjectMgr.cpp:950-957 | related — 2 hops from a seed |
| Player.Main/SendLoot | method | Player.cpp:7768-8155 | related — 2 hops from a seed |
| Player.Main/IsAllowedToLoot | method | Player.cpp:15374-15430 | related — 2 hops from a seed |
| Unit.Main/DealDamage | method | Unit.cpp:640-954 | related — 2 hops from a seed |
| Unit.Main/Kill | method | Unit.cpp:956-1262 | related — 2 hops from a seed |
| Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself | method | Unit.cpp:5054-5061 | related — 2 hops from a seed |
| WorldSession.LootHandler/DoLootRelease | method | LootHandler.cpp:390-614 | related — 2 hops from a seed |

---

<!-- verify: invented-config | keys: Group.GroupLoot, Group.NeedBeforeGreed -->
