# thousand_needles

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Thousand Needles Quest Scripts

**Purpose & Responsibilities**  
`thousand_needles.cpp` implements scripted behaviors for five distinct quest-related entities in the Thousand Needles zone. It provides:
1. Two escort quests (`npc_lakota_windsong` and `npc_paoka_swiftmountain`) that spawn enemies at specific waypoints and trigger quest completion events.
2. A gossip-based puzzle quest (`npc_plucky_johnson`) involving faction changes, spell effects, and emote interactions.
3. A game object interaction (`go_panther_cage`) that triggers combat with a nearby creature.
4. A wave-based boss encounter (`npc_grenka_bloodscreech`) with summoning mechanics, visibility toggling, and a flee behavior at low health.
5. An event handler (`ProcessEventId_event_test_of_endurance`) that spawns the boss for the "Test of Endurance" quest.

The unit contains no database queries or table interactions. All logic is driven by in-memory state, hardcoded coordinates, and engine API calls.

---

## Feature Breakdown

### 1. Lakota Windsong Escort (Quest 4904: "Free At Last")
**Members:** `npc_lakota_windsongAI`, `Reset#2`, `WaypointReached`, `JustRespawned`, `DoSpawnBandits`, `GetAI_npc_lakota_windsong`, `QuestAccept_npc_lakota_windsong`

This escort quest involves guiding Lakota Windsong through three ambush points. The AI inherits from `npc_escortAI` (defined in `ScriptedEscortAI`).

- **Initialization:** `QuestAccept_npc_lakota_windsong` is called when the player accepts quest 4904. It sets the creature’s faction to `FACTION_ESCORTEE` (33), removes the `UNIT_FLAG_IMMUNE_TO_NPC` flag, and starts the escort via `ScriptedEscortAI::Start`.
- **Waypoint Logic:** `WaypointReached` triggers at waypoints 8, 14, and 21 to spawn pairs of "Grim Bandits" (`NPC_GRIM_BANDIT`, entry 10758) using `DoSpawnBandits`. Coordinates are hardcoded in `m_afBanditLoc`. At waypoint 45, the quest completes via `Player.Main::GroupEventHappens`.
- **Respawn Safety:** `JustRespawned` sets `UNIT_FLAG_IMMUNE_TO_NPC` to prevent aggro during respawn, then calls the base `ScriptedEscortAI::JustRespawned`.
- **Enemy Spawning:** `DoSpawnBandits` summons two creatures at predefined offsets for each ambush ID (0, 2, 4), using `WorldObject.Object::SummonCreature#2` with a 20-second despawn timer.

**Cross-Unit Collaboration:**
- Calls `ScriptedEscortAI::GetPlayerForEscort` to retrieve the escorting player.
- Uses `ScriptMgr::DoScriptText` for dialogue.
- Triggers `Player.Main::GroupEventHappens` for quest completion.

---

### 2. Paoka Swiftmountain Escort (Quest 4770: "Homeward")
**Members:** `npc_paoka_swiftmountainAI`, `Reset#3`, `WaypointReached#2`, `JustRespawned#2`, `DoSpawnWyvern`, `GetAI_npc_paoka_swiftmountain`, `QuestAccept_npc_paoka_swiftmountain`

Similar to Lakota’s escort, but spawns wyverns instead of bandits.

- **Initialization:** `QuestAccept_npc_paoka_swiftmountain` sets the faction to `FACTION_ESCORT_H_NEUTRAL_ACTIVE`, removes immunity flags, and starts the escort.
- **Waypoint Logic:** At waypoint 15, `DoSpawnWyvern` summons three wyverns (`NPC_WYVERN`, entry 4107) from `m_afWyvernLoc`. At waypoint 26, dialogue plays. At waypoint 27, `Player.Main::GroupEventHappens` completes quest 4770.
- **Respawn Safety:** Identical to Lakota’s `JustRespawned`.
- **Enemy Spawning:** `DoSpawnWyvern` iterates over `m_afWyvernLoc` to summon creatures with a 20-second despawn timer.

**Cross-Unit Collaboration:**
- Same pattern as Lakota: uses `ScriptedEscortAI::GetPlayerForEscort`, `ScriptMgr::DoScriptText`, and `Player.Main::GroupEventHappens`.

---

### 3. Plucky Johnson Puzzle (Quest 1950: "Scoop")
**Members:** `npc_plucky_johnsonAI`, `Reset#4`, `ReceiveEmote`, `UpdateAI#2`, `GetAI_npc_plucky_johnson`, `GossipHello_npc_plucky_johnson`, `GossipSelect_npc_plucky_johnson`

This NPC behaves as a chicken until specific emotes are performed, then transforms into a humanoid for gossip interaction.

- **State Management:** The AI tracks `m_uiNormFaction` (original faction) and `m_uiResetTimer` (120 seconds). On `Reset#4`, it reverts to chicken form (`SPELL_PLUCKY_CHICKEN`, 9220), restores normal faction, and removes the gossip flag.
- **Emote Handling:** `ReceiveEmote` checks for `TEXTEMOTE_BECKON` or `TEXTEMOTE_CHICKEN`. If the player has quest 1950 incomplete, it transforms to human (`SPELL_PLUCKY_HUMAN`, 9192), sets faction to friendly (35), adds the gossip flag, and waves.
- **Combat & Reset:** `UpdateAI#2` handles melee attacks if hostile. If the gossip flag is set, it decrements `m_uiResetTimer`; if expired and no victim, it evades. Otherwise, it removes the gossip flag.
- **Gossip Interface:** `GossipHello_npc_plucky_johnson` offers a menu item if quest 1950 is incomplete. `GossipSelect_npc_plucky_johnson` completes the quest via `Player.Main::AreaExploredOrEventHappens`.

**Cross-Unit Collaboration:**
- Uses `ScriptedAI::EnterEvadeMode` for reset.
- Calls `Unit.Main::HandleEmoteCommand` for wave animation.
- Gossip functions use `GossipDef::AddMenuItem#4` and `GossipDef::SendGossipMenu`.

---

### 4. Panther Cage Interaction (Quest 5151)
**Member:** `go_panther_cage`

A game object script that triggers combat with an enraged panther.

- **Logic:** When interacted, if the player has quest 5151 incomplete, it finds the nearest `ENRAGED_PANTHER` (entry 10992) within 5 yards. It removes spawning/immunity flags and starts combat via `CreatureAI::AttackStart`.
- **Return Value:** Returns `false` to allow the cage to open visually.

**Cross-Unit Collaboration:**
- Uses `WorldObject.Object::FindNearestCreature` and `CreatureAI::AttackStart`.

---

### 5. Grenka Bloodscreech Boss Encounter
**Members:** `npc_grenka_bloodscreechAI`, `Reset`, `DoSummon`, `JustSummoned`, `SummonedCreatureJustDied`, `UpdateAI`, `GetAI_npc_grenka_bloodscreech`, `ProcessEventId_event_test_of_endurance`

A wave-based boss fight with summoning mechanics and a flee behavior.

- **Initialization:** The constructor sets the creature to invisible, pacified, and immune. `m_uiWave` starts at 0, `m_uiTimer` at 5 seconds.
- **Wave Mechanics:** `UpdateAI` summons harpies (`NPC_SCREECHING_HARPY`, entry 4100) in waves:
  - Wave 0: 1 harpy.
  - Wave 1: 2 harpies.
  - Wave 2: 1 harpy, then Grenka becomes visible and attacks the player stored in `m_PlayerGuid`.
- **Player Tracking:** `ProcessEventId_event_test_of_endurance` spawns Grenka and assigns the triggering player’s GUID to `m_PlayerGuid`.
- **Combat Behavior:** `JustSummoned` makes summoned harpies attack the tracked player. `SummonedCreatureJustDied` clears loot. `UpdateAI` also triggers `Creature.Main::DoFlee` if Grenka’s health drops below 15% and she hasn’t fled yet.
- **Reset:** `Reset` only clears `m_bHasFled`.

**Cross-Unit Collaboration:**
- Uses `WorldObject.Object::SummonCreature#2` for summons.
- Calls `CreatureAI::AttackStart` for combat initiation.
- `Map.Main::GetPlayer` retrieves the tracked player.
- `Loot::clear` cleans up dead summons.

---

## Notable Implementation Details

1. **Hardcoded Coordinates:** All summon locations (`m_afBanditLoc`, `m_afWyvernLoc`, `Harpies`) are hardcoded floats. Changes require recompilation.
2. **Immunity Flags:** Both escorts use `UNIT_FLAG_IMMUNE_TO_NPC` on respawn to prevent accidental aggro. This is removed on quest accept.
3. **Plucky Johnson’s Timer:** The 120-second reset timer in `UpdateAI#2` ensures the NPC reverts to chicken form if ignored. The evasion check prevents stuck states.
4. **Grenka’s Flee Mechanic:** The `m_bHasFled` flag ensures Grenka flees only once. The health check (`< 15.0f`) is a hard-coded threshold.
5. **Event Handler Safety:** `ProcessEventId_event_test_of_endurance` returns `true` early if `pSource` or `pTarget` is null, or if Grenka already exists nearby. This prevents duplicate spawns.
6. **No Database Interaction:** All quest IDs, NPC entries, and spell IDs are hardcoded. No SQL queries are present.

---

## Member Reference

**npc_lakota_windsongAI** (ctor): Initializes the escort AI by calling `Reset()`. Inherits from `npc_escortAI` (`ScriptedEscortAI`).

**Reset#2**: Empty override; no custom reset logic.

**WaypointReached**: Handles waypoints 8, 14, 21 (spawns bandits via `DoSpawnBandits`) and 45 (completes quest 4904 via `Player.Main::GroupEventHappens`). Uses `ScriptMgr::DoScriptText` for dialogue.

**JustRespawned**: Sets `UNIT_FLAG_IMMUNE_TO_NPC` and calls `ScriptedEscortAI::JustRespawned`.

**DoSpawnBandits**: Summons two `NPC_GRIM_BANDIT` creatures at hardcoded coordinates for the given ambush ID. Uses `WorldObject.Object::SummonCreature#2`.

**GetAI_npc_lakota_windsong**: Factory function returning a new `npc_lakota_windsongAI` instance.

**QuestAccept_npc_lakota_windsong**: Triggers escort start for quest 4904. Sets faction, removes immunity, and calls `ScriptedEscortAI::Start`.

**npc_paoka_swiftmountainAI** (ctor): Initializes the escort AI by calling `Reset()`. Inherits from `npc_escortAI` (`ScriptedEscortAI`).

**Reset#3**: Empty override; no custom reset logic.

**WaypointReached#2**: Handles waypoints 15 (spawns wyverns via `DoSpawnWyvern`), 26 (dialogue), and 27 (completes quest 4770 via `Player.Main::GroupEventHappens`). Uses `ScriptMgr::DoScriptText`.

**JustRespawned#2**: Sets `UNIT_FLAG_IMMUNE_TO_NPC` and calls `ScriptedEscortAI::JustRespawned`.

**DoSpawnWyvern**: Summons three `NPC_WYVERN` creatures at hardcoded coordinates. Uses `WorldObject.Object::SummonCreature#2`.

**GetAI_npc_paoka_swiftmountain**: Factory function returning a new `npc_paoka_swiftmountainAI` instance.

**QuestAccept_npc_paoka_swiftmountain**: Triggers escort start for quest 4770. Sets faction, removes immunity, and calls `ScriptedEscortAI::Start`.

**npc_plucky_johnsonAI** (ctor): Initializes the AI, stores original faction, and calls `Reset()`. Inherits from `ScriptedAI`.

**Reset#4**: Resets faction, removes gossip flag, casts chicken spell, and sets `m_uiResetTimer` to 120 seconds.

**ReceiveEmote**: Transforms NPC to human form if beckon/chicken emote is received and quest 1950 is incomplete. Adds gossip flag and casts human spell.

**UpdateAI#2**: Handles melee combat and timer-based reset. Evades if timer expires and no victim. Removes gossip flag if in combat.

**GetAI_npc_plucky_johnson**: Factory function returning a new `npc_plucky_johnsonAI` instance.

**GossipHello_npc_plucky_johnson**: Offers gossip menu item if quest 1950 is incomplete. Uses `GossipDef::AddMenuItem#4` and `GossipDef::SendGossipMenu`.

**GossipSelect_npc_plucky_johnson**: Completes quest 1950 via `Player.Main::AreaExploredOrEventHappens` and closes gossip menu.

**go_panther_cage**: Triggers combat with nearest enraged panther if quest 5151 is incomplete. Uses `WorldObject.Object::FindNearestCreature` and `CreatureAI::AttackStart`. Returns `false` to open cage.

**npc_grenka_bloodscreechAI** (ctor): Sets creature to invisible/pacified/immune, initializes wave/timer variables, and calls `Reset()`. Inherits from `ScriptedAI`.

**Reset**: Clears `m_bHasFled` flag.

**DoSummon**: Summons a harpy or Grenka at predefined coordinates. Uses `WorldObject.Object::SummonCreature#2`.

**JustSummoned**: Makes summoned creature attack the tracked player (`m_PlayerGuid`) if alive. Uses `Map.Main::GetPlayer` and `CreatureAI::AttackStart`.

**SummonedCreatureJustDied**: Clears loot from dead summoned creature via `Loot::clear`.

**UpdateAI**: Manages wave summoning, visibility toggle, and flee behavior. Calls `ScriptedAI::UpdateAI` for base logic. Uses `Creature.Main::DoFlee` at <15% health.

**GetAI_npc_grenka_bloodscreech**: Factory function returning a new `npc_grenka_bloodscreechAI` instance.

**ProcessEventId_event_test_of_endurance**: Spawns Grenka for the "Test of Endurance" quest. Assigns player GUID to AI. Uses `WorldObject.Object::SummonCreature#2`.

**AddSC_thousand_needles**: Registers all scripts with `ScriptMgr::RegisterSelf`. Called by `ScriptLoader::AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — thousand_needles

*Source:* thousand_needles.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_lakota_windsongAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#2 | method | — | — | — |
| WaypointReached | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText | — | — |
| JustRespawned | method | ScriptedEscortAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| DoSpawnBandits | method | WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_npc_lakota_windsong | function | — | — | — |
| QuestAccept_npc_lakota_windsong | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, WorldObject.Object/RemoveFlag | — | — |
| npc_paoka_swiftmountainAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#3 | method | — | — | — |
| WaypointReached#2 | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText | — | — |
| JustRespawned#2 | method | ScriptedEscortAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| DoSpawnWyvern | method | WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_npc_paoka_swiftmountain | function | — | — | — |
| QuestAccept_npc_paoka_swiftmountain | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, Unit.Main/SetFactionTemplateId, WorldObject.Object/RemoveFlag | — | — |
| npc_plucky_johnsonAI | ctor | ScriptedAI/ScriptedAI, Unit.Main/GetFactionTemplateId | — | — |
| Reset#4 | method | Object/HasFlag, SpellCaster/CastSpell#2, Unit.Main/GetFactionTemplateId, Unit.Main/SetFactionTemplateId, WorldObject.Object/RemoveFlag | — | — |
| ReceiveEmote | method | Object/HasFlag, Player.Main/GetQuestStatus, SpellCaster/CastSpell#2, Unit.Main/HandleEmoteCommand, Unit.Main/SetFactionTemplateId, WorldObject.Object/SetFlag | — | — |
| UpdateAI#2 | method | CreatureAI/DoMeleeAttackIfReady, Object/HasFlag, ScriptedAI/EnterEvadeMode, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/RemoveFlag | — | — |
| GetAI_npc_plucky_johnson | function | — | — | — |
| GossipHello_npc_plucky_johnson | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetQuestStatus, PlayerMenu/GetGossipMenu | — | — |
| GossipSelect_npc_plucky_johnson | function | GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/AreaExploredOrEventHappens | — | — |
| go_panther_cage | function | Creature.Main/AI, CreatureAI/AttackStart, Player.Main/GetQuestStatus, WorldObject.Object/FindNearestCreature, WorldObject.Object/RemoveFlag | — | — |
| npc_grenka_bloodscreechAI | ctor | ScriptedAI/ScriptedAI, Unit.Main/SetVisibility, WorldObject.Object/SetFlag | — | — |
| Reset | method | — | — | — |
| DoSummon | method | WorldObject.Object/SummonCreature#2 | — | — |
| JustSummoned | method | Creature.Main/AI, CreatureAI/AttackStart, Map.Main/GetPlayer, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| SummonedCreatureJustDied | method | Loot/clear | — | — |
| UpdateAI | method | BasicAI/UpdateAI, Creature.Main/AI, Creature.Main/DoFlee, CreatureAI/AttackStart, Map.Main/GetPlayer, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SetVisibility, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag | — | — |
| GetAI_npc_grenka_bloodscreech | function | — | — | — |
| ProcessEventId_event_test_of_endurance | function | Creature.Main/AI, Object/GetObjectGuid, Object/ToGameObject, Object/ToPlayer, WorldObject.Object/FindNearestCreature, WorldObject.Object/SummonCreature#2 | — | — |
| AddSC_thousand_needles | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
