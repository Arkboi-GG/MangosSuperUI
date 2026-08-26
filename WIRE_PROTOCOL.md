# BotBridge Wire Protocol — v2

**Transport:** TCP on `127.0.0.1:3444`  
**Encoding:** UTF-8, newline-delimited JSON (one JSON object per `\n`)  
**Direction:** C++ (AiBotAI) is the TCP CLIENT → C# (BotBridgeService) is the TCP SERVER  
**Last audited:** AiBotAI.cpp, BotBridgeService.cs, BotBridgeHub.cs

---

## Connection Lifecycle

1. `mangosd` starts → AiBotAI spawns → `OnSessionLoaded()` fires
2. AiBotAI opens TCP connection to `127.0.0.1:3444`
3. After `m_initialized` is true, AiBotAI sends `HELLO` with identity + initial position
4. AiBotAI sends `STATE` every `BRIDGE_STATE_INTERVAL` ms (default 5000)
5. AiBotAI sends `EVENT` messages on discrete occurrences
6. C# sends commands asynchronously at any time
7. On disconnect, C# marks bot as `DISCONNECTED` but retains state for UI display
8. C++ reconnects with exponential backoff: `BRIDGE_RECONNECT_BASE` → max `BRIDGE_RECONNECT_MAX`

---

## Message Envelope

Every line is a JSON object with exactly two fields:

```json
{"type":"MESSAGE_TYPE","payload":{...}}
```

C++ uses `snprintf` for outbound, `JsonExtractString/Int/Float` helpers for inbound (no JSON library). All payload fields are flat within the `payload` object.

---

## Inbound Messages (C++ → C#)

### HELLO
Sent once after initialization. Registers the bot with the bridge.

```json
{
  "type": "HELLO",
  "payload": {
    "guid": 12345,
    "name": "Edageq",
    "race": 1,
    "classId": 1,
    "level": 5,
    "mapId": 0,
    "zoneId": 12,
    "x": -8949.95,
    "y": -132.493,
    "z": 83.5312
  }
}
```

### STATE
Periodic heartbeat with full state snapshot.

```json
{
  "type": "STATE",
  "payload": {
    "guid": 12345,
    "health": 180,
    "maxHealth": 200,
    "mana": 0,
    "maxMana": 0,
    "level": 5,
    "mapId": 0,
    "zoneId": 12,
    "x": -8949.95,
    "y": -132.493,
    "z": 83.5312,
    "inCombat": false,
    "isDead": false,
    "targetGuid": 0,
    "taskState": "IDLE"
  }
}
```

**taskState values currently emitted by C++:**

| Value | Condition |
|-------|-----------|
| `IDLE` | Default — not dead, not in combat, no active task |
| `MOVING` | `m_currentTask.type == TASK_MOVE_TO` or `me->IsMoving()` |
| `COMBAT` | `me->IsInCombat()` |
| `DEAD` | `me->IsDead()` |

> **Note:** `EXECUTING` and `WAITING` are reserved for Phase 3 task states but not yet emitted by C++.

### EVENT
Discrete events. The `event` field determines which additional payload fields are present.

**Common envelope:**
```json
{
  "type": "EVENT",
  "payload": {
    "guid": 12345,
    "event": "EVENT_NAME",
    "data": "optional free-text"
  }
}
```

**Events currently sent by C++:**

| Event | When Fired | Extended Payload Fields | C++ Sender |
|-------|-----------|------------------------|------------|
| `KILL` | Target creature dies in combat | `creature_entry`, `creature_guid` | `SendKillEvent()` |
| `QUEST_UPDATE` | Quest accepted, rewarded, or abandoned | `quest_id`, `status` | `SendQuestUpdateEvent()` |
| `LEVEL_UP` | Bot level increased | `new_level` | `SendLevelUpEvent()` |
| `CHAT_RECV` | Incoming whisper/say intercepted via EVENT path | `sender`, `message`, `chat_type` | `SendChatRecvEvent()` |
| `TASK_COMPLETE` | `MovementInform(AIBOT_POINT_TASK_DEST)` fires | `data` = `"MOVE_TO arrived"` | `BridgeSendEvent()` |
| `DEATH` | `me->IsDead()` transition detected | (none) | `BridgeSendEvent()` |
| `RESPAWN` | Self-revive completes | (none) | `BridgeSendEvent()` |
| `NPC_INTERACT` | INTERACT_NPC reaches the NPC (≤10yd) | `data` = creature name | `BridgeSendEvent()` |
| `QUEST_FAILED` | Quest command validation fails | `data` = reason string | `BridgeSendEvent()` |
| `COMBAT_LOADOUT_ACK` | A correlated combat-loadout request succeeds or is rejected | `data` = pipe-delimited final runtime state and result | `BridgeHandleApplyCombatLoadout()` |

**KILL example:**
```json
{
  "type": "EVENT",
  "payload": {
    "guid": 12345,
    "event": "KILL",
    "creature_entry": 257,
    "creature_guid": 54321
  }
}
```

**QUEST_UPDATE example:**
```json
{
  "type": "EVENT",
  "payload": {
    "guid": 12345,
    "event": "QUEST_UPDATE",
    "quest_id": 6,
    "status": "accepted"
  }
}
```

**QUEST_UPDATE `status` values:** `accepted`, `rewarded`, `abandoned`

**QUEST_FAILED `data` values:** `"quest not found"`, `"requirements not met"`, `"quest log full"`, `"quest not in log"`

**LEVEL_UP example:**
```json
{
  "type": "EVENT",
  "payload": {
    "guid": 12345,
    "event": "LEVEL_UP",
    "new_level": 6
  }
}
```

### CHAT_RECV (standalone message type)
Also exists as a separate top-level message type in addition to the EVENT path above. C# `BotBridgeService` handles both — `HandleChatAsync()` for this type, and `HandleEventAsync()` case `"CHAT_RECV"` for the EVENT-wrapped version.

```json
{
  "type": "CHAT_RECV",
  "payload": {
    "guid": 12345,
    "senderName": "Nico",
    "message": "Hey, want to group up?",
    "chatType": 7
  }
}
```

**chatType values:** `0` = SAY, `1` = PARTY, `6` = YELL, `7` = WHISPER

> **Implementation note:** C++ `OnPacketReceived()` intercepts `SMSG_MESSAGECHAT` and routes through `SendChatRecvEvent()` which uses the EVENT envelope. The standalone CHAT_RECV type handler exists in C# but the C++ currently sends via EVENT. Both paths produce identical UI output.

---

## Combat Loadout Runtime Contract — Implemented

This contract lets SuperUI display the combat policy the core is actually using and change a managed bot's talent profile, active role, and combat rotation as one guarded core operation. It is deliberately separate from direct character-database editing: talent spells, runtime caches, pets, and rotation pointers must be changed on the live bot's world thread.

### Runtime fields on `HELLO` and `STATE`

Both `HELLO` and every `STATE` include the following fields in addition to their existing payloads:

| Field | Type | Meaning |
|-------|------|---------|
| `specTab` | uint8 | Class-local talent profile slot: `0`, `1`, or `2`; `255` means unassigned. |
| `specProfile` | string | Stable manifest id for `specTab`, such as `warrior_arms`; `unassigned` when no valid manifest profile resolves. |
| `activeRole` | uint8 | `1` melee DPS, `2` ranged DPS, `3` tank, or `4` healer. |
| `talentProfileState` | string | Runtime compatibility state: `unchecked`, `usable`, `conflict`, `invalid`, `disabled`, `error`, or `unavailable`. |
| `rotationSource` | string | Effective in-combat dispatcher: `builtin_spec`, `custom`, or `legacy_class`. |
| `rotationProfile` | string | Custom profile id, built-in spec profile id, or `legacy_class`, according to `rotationSource`. |
| `rotationInstructionCount` | uint32 | Number of instructions in the installed custom slate. Hardcoded rotations report `0`. |
| `rotationCastableCount` | uint32 | Installed custom instructions that resolved to spells the bot knows. Hardcoded rotations report `0`. |
| `combatConfigRevision` | uint32 | Live-session optimistic-concurrency token. A successful loadout change increments it. |

Example fragment:

```json
{
  "specTab": 2,
  "specProfile": "warrior_protection",
  "activeRole": 3,
  "talentProfileState": "usable",
  "rotationSource": "builtin_spec",
  "rotationProfile": "warrior_protection",
  "rotationInstructionCount": 0,
  "rotationCastableCount": 0,
  "combatConfigRevision": 4
}
```

These are core-runtime facts, not a reconstruction from `characters.playerbot` or SuperUI's custom-rotation assignment file. In particular, `specTab` alone does not prove that the built-in specialization policy is running. A conflicting, invalid, disabled, or errored talent profile falls through to `legacy_class` unless a custom slate is installed.

The effective in-combat precedence is:

1. `custom` when a non-empty custom slate is installed;
2. `builtin_spec` when no custom slate is installed and `talentProfileState` is `usable`;
3. `legacy_class` otherwise.

`custom` overrides in-combat casting only. Out-of-combat buffing, recovery, forms, pet preparation, and other maintenance continue to use the usable built-in spec policy, with legacy class behavior as its fallback. Therefore `rotationSource` describes the selected combat dispatcher, not every behavior the bot performs outside combat.

`combatConfigRevision` is runtime/session state, not a durable database version. It survives a TCP bridge reconnect to the same AI instance, but a bot relog creates a new AI instance. SuperUI re-observes `HELLO`, re-pushes any persisted custom assignment, and then treats the resulting `HELLO`/`STATE`/ACK values as authoritative.

### `APPLY_COMBAT_LOADOUT` (C# → C++)

This is the only supported bridge operation for a web-driven talent/profile change. SuperUI must not implement a specialization change by separately editing `playerbot`, wiping talent spells, and sending `LOAD_ROTATION`.

Canonical built-in-spec request:

```json
{
  "type": "APPLY_COMBAT_LOADOUT",
  "payload": {
    "requestId": "d679d16c82844a98abddc51ba68a1476",
    "expectedRevision": 4,
    "specTab": 2,
    "activeRole": 3,
    "resetTalents": true,
    "rotationMode": "SPEC",
    "rotationProfile": "",
    "rotationData": ""
  }
}
```

Canonical custom-rotation request:

```json
{
  "type": "APPLY_COMBAT_LOADOUT",
  "payload": {
    "requestId": "36b742d7f1784638add8d1753d864710",
    "expectedRevision": 5,
    "specTab": 2,
    "activeRole": 3,
    "resetTalents": false,
    "rotationMode": "CUSTOM",
    "rotationProfile": "warrior_protection_safe_v1",
    "rotationData": "355:10:1:0:100:0:0|6572:20:1:0:100:0:0"
  }
}
```

All payload keys are required, including the empty strings used by `SPEC`:

| Key | Type | Contract |
|-----|------|----------|
| `requestId` | string | GUID in 32-character `N` form. It is echoed in the ACK and provides session-level duplicate suppression. |
| `expectedRevision` | uint32 | Must exactly match the latest runtime `combatConfigRevision`; otherwise the core returns `stale_revision`. |
| `specTab` | int | Requested class-local profile slot, `0..2`. The profile must exist for the bot's class. |
| `activeRole` | int | Requested allowed role, `1..4`. The selected profile's role policy is enforced by both SuperUI and the core. |
| `resetTalents` | bool | When true, freely wipes talents and purchases the selected level-appropriate manifest prefix. |
| `rotationMode` | string | Canonical bridge values are `SPEC` and `CUSTOM`. The public web model uses `spec_default` and `custom`; SuperUI translates it before sending. |
| `rotationProfile` | string | Empty for `SPEC`; required safe profile token for `CUSTOM`. |
| `rotationData` | string | Empty for `SPEC`; required instruction slate for `CUSTOM`. |

Custom instructions use the existing pipe format:

```text
spellId:priority:target:hpMin:hpMax:auraId:auraPresent|...
```

`target` is `0` self, `1` current target, or `2` lowest-health party member. Health bounds are inclusive percentages from `0` through `100`; `auraPresent` is `0` or `1`. SuperUI sends instructions in priority order. The core accepts at most 64 instructions, validates every spell/aura id and condition, and rejects the whole request if any instruction is malformed or remains unlearned after a requested talent rebuild.

#### Reset rules

- Changing `specTab` requires `resetTalents: true`; otherwise the core returns `reset_required` and changes nothing.
- Rebuilding the current specialization also uses `resetTalents: true`.
- A same-spec role change, switch to a compatible custom rotation, or return to the built-in spec rotation may use `resetTalents: false`.
- Selecting `SPEC` clears the custom slate. The resulting source is `builtin_spec` when the talent profile is usable, not automatically `legacy_class`.
- The public HTTP request must include the `expectedRevision` shown by its read model. SuperUI rejects a missing or stale revision before creating the bridge request, while the core independently enforces the same revision on the wire.
- The public HTTP request additionally requires explicit reset confirmation when `resetTalents` is true. That confirmation is enforced before the bridge command and is not duplicated on the wire.

#### Online-only and safety semantics

Combat-loadout mutation is online-only, and SuperUI never edits `character_spell` directly. A direct apply rejects an offline, dead, or in-combat bot before sending. An online bot that is temporarily unsafe may instead have one durable pending build in `vmangos_admin.bot_combat_loadout_queue`; the pending build is only an intent and does not change core or character state until dispatch. If a direct request loses a race with a verified pre-mutation safety gate (for example, the bot begins casting or enters combat after the page refresh), SuperUI converts that same intent into a queued build. Connection failures are never converted automatically because a post-write disconnect may have an unknown outcome. The core independently rejects a bot that is unmanaged, uninitialized, possessed, dead, in combat, casting, teleporting, on a taxi, or in a battleground.

The web queue is deliberately one-deep and same-session:

- queueing performs the same catalog, profile, role, reset-confirmation, rotation, and revision validation as a direct apply;
- custom rotation wire content is fingerprinted when queued; hot-editing that profile fails the pending item for review instead of silently changing what will run;
- an unsent `waiting` or `failed` item can be replaced or cancelled from the same workshop;
- replace operations must echo the latest `queueId` as `expectedQueueId`; cancel and dismiss must echo both `expectedQueueId` and the last observed `expectedStatus`, so a stale browser or future MSUIClient cannot overwrite newer intent or dismiss a row that became `uncertain` after the user clicked;
- the worker waits for fresh bridge state, an alive bot, and an out-of-combat state, then revalidates immediately before claiming the row;
- persisted rotation replay on HELLO is registered before its exact TCP connection is published; loadout validation waits that exact connection's replay, and an older replay is never redirected through a guid to a newer socket;
- a reconnect or live revision change fails the item for explicit review instead of rebasing an old reset intent;
- an item expires after 15 minutes if no safe opportunity occurs;
- the row is changed to `dispatching`, assigned the exact wire `requestId`, and given an instance-owned 45-second claim lease before the TCP write;
- an expired dispatch claim is reconciled to `uncertain`, never back to `waiting`;
- only explicit pre-mutation safety rejections return to `waiting`; an ACK timeout, disconnect during dispatch, host interruption, failed rollback, mismatched final ACK state, or post-ACK persistence failure becomes `uncertain` and is never retried automatically;
- an operator may dismiss an `uncertain` record after inspecting the bot's live state. Dismissal only clears the queue interlock; it never attempts to undo core state.

Offline bots cannot accept new queue entries because there is no live session/revision to bind safely. A previously saved entry may remain visible for review after a session change, but it must be replaced or cancelled before anything else is sent.

Queue records are scoped to the World State snapshot because `vmangos_admin` is part of the world bundle. Before a world restore replaces `mangos` and `vmangos_admin`, SuperUI closes a shared maintenance gate: new queue reads and mutations fail with HTTP 503 / `queue_unavailable`, while already admitted operations drain through their correlated core acknowledgement and terminal database write. The restore then imports the snapshot's queue rows and immediately reruns admin-schema migrations before reopening the gate. This prevents a returned 202/direct-apply result from being erased by a concurrent database replacement.

Direct apply uses the same durable table as the waiting queue: after read-only validation, SuperUI writes an instance-owned `dispatching` journal row with the exact request id and lease before any TCP write. The caller may cancel before that claim; after it exists, SuperUI drives the operation to a durable `applied`, `failed`, or `uncertain` result even if the HTTP request disappears. `bot_offline` means the bridge proved the request had not begun writing, but it is still conservatively journaled after a direct claim. Once writing starts, caller cancellation no longer removes the correlated waiter: SuperUI waits for the bounded ACK window. A write failure, post-send disconnect, or ACK from a superseded connection returns HTTP 504 with `errorCode: "outcome_unknown"`; the UI refreshes live state, and the durable `uncertain` row blocks another mutation until an operator reviews and dismisses it.

The core processes the command on the bot's world thread as one operation:

1. validate the request, revision, profile, role, rotation mode, and complete custom slate;
2. snapshot talents, persisted profile metadata, role, runtime talent state, and the current rotation;
3. clear live rotation pointers before any talent wipe;
4. reset and rebuild talents when requested, then refresh spell caches, lifecycle spells, reagents, skills, and pet state;
5. resolve the entire custom slate against the post-change known-spell set;
6. on failure after mutation begins, restore the captured talent and rotation state, then report the final state;
7. on success, save the character, install the new runtime rotation, increment `combatConfigRevision`, send the correlated ACK, and immediately push `STATE`.

This is an application-level runtime consistency guarantee, not a cross-process ACID transaction. SuperUI persists its desired custom-rotation assignment only after a successful core ACK. If that separate persistence step fails, the API reports the split outcome explicitly and does not repeat the destructive reset automatically.

Two additional retry rules protect destructive operations:

- Re-sending the most recently completed `requestId` to the same live AI returns its cached ACK without applying the reset again.
- SuperUI never blindly retries after an ACK timeout or disconnect. A late ACK may still refresh runtime state; the operator must refresh before deciding whether another change is necessary.

### `COMBAT_LOADOUT_ACK` (`EVENT`, C++ → C#)

Every syntactically valid apply request receives a correlated event. The common `EVENT.payload.data` string contains these exact pipe-delimited keys:

```text
requestId=d679d16c82844a98abddc51ba68a1476|status=ok|code=ok|revision=5|specTab=2|profile=warrior_protection|role=3|talentState=usable|learned=51|rotationSource=builtin_spec|rotationProfile=warrior_protection|loaded=0|skipped=0|reset=1
```

Full envelope:

```json
{
  "type": "EVENT",
  "payload": {
    "guid": 12345,
    "event": "COMBAT_LOADOUT_ACK",
    "data": "requestId=d679d16c82844a98abddc51ba68a1476|status=ok|code=ok|revision=5|specTab=2|profile=warrior_protection|role=3|talentState=usable|learned=51|rotationSource=builtin_spec|rotationProfile=warrior_protection|loaded=0|skipped=0|reset=1"
  }
}
```

| ACK key | Meaning |
|---------|---------|
| `requestId` | Correlates exactly one waiting HTTP operation with this bot and request. |
| `status` | `ok` on success; `error` on rejection or failed application. |
| `code` | Machine-readable outcome. `ok` indicates success; error codes are listed below. |
| `revision` | Final runtime revision. It increments only after a successful runtime apply. |
| `specTab` / `profile` / `role` | Final runtime talent-profile metadata, including restored values after rollback. |
| `talentState` | Final runtime talent compatibility state. |
| `learned` | Points learned during this attempt. On an error they may have been rolled back; use `status` and final state, not this count alone. |
| `rotationSource` / `rotationProfile` | Final effective in-combat rotation after success, rejection, or rollback. |
| `loaded` / `skipped` | Final installed custom slate's castable and non-castable instruction counts. Built-in rotations report zero. |
| `reset` | Echo of the request's `resetTalents` flag. It is not proof of success; consult `status` and `code`. |

Current core error codes are:

| Code | Meaning |
|------|---------|
| `invalid_request` | A required field, strict string, boolean, or safe request token was missing or malformed. The request id may be blank and therefore uncorrelated. |
| `stale_revision` | `expectedRevision` did not match current runtime state. Refresh before trying again. |
| `not_managed` | The target is not an initialized managed AiBot. |
| `invalid_profile` | `specTab` is out of range or not valid for the bot's class. |
| `invalid_role` | The requested role is not allowed by the selected profile. |
| `reset_required` | A specialization change was requested without a talent reset. |
| `invalid_rotation_mode` | The rotation mode is not a supported `SPEC`/`CUSTOM` value. |
| `invalid_rotation` | The custom profile/data is empty, unsafe, malformed, or references an invalid spell/aura. |
| `rotation_too_large` | The custom slate exceeds 64 instructions. |
| `rotation_spell_unlearned` | A custom instruction names a spell absent from the final target build; any started mutation is rolled back. |
| `bot_possessed` | A human currently controls the bot. |
| `bot_dead` | The bot is dead. |
| `bot_in_combat` | The bot is in combat. |
| `bot_casting` | The bot is currently casting. |
| `bot_teleporting` | A teleport is in progress. |
| `bot_on_taxi` | The bot is taxi-flying. |
| `bot_in_battleground` | Loadout mutation is disabled in battlegrounds. |
| `snapshot_failed` | The core could not capture a rollback snapshot before mutation. |
| `catalog_disabled` | The compiled talent catalog failed DBC validation, so spending is disabled. |
| `apply_failed` | Talent application failed and the original snapshot was restored. |
| `rollback_failed` | Talent application or custom resolution failed and exact restoration also failed. This requires operator inspection. |

An error ACK always describes the core's final known runtime state. Early validation errors leave the build unchanged. Once mutation has begun, the core attempts exact rollback before acknowledging. SuperUI copies the ACK's final fields into its live state before waking the waiting HTTP request, so its runtime revision and effective rotation agree with the ACK immediately.

`PExecute` and `Player::SaveToDB` enqueue character-database work on VMaNGOS's asynchronous, GUID-serialized queue. Therefore an `ok` ACK confirms the live runtime apply plus persistence enqueue; it does not claim that another database connection can already observe every `playerbot` and `character_spell` row. After a successful ACK, SuperUI holds the per-bot operation lock and polls fresh read-only projections for at most five seconds. It never resends the mutation. A converged HTTP response returns `status: "applied"`, `readModelConverged: true`, and a consistent `current` model. If the bound expires, the operation still returns success as `status: "applied_read_model_pending"`, `readModelConverged: false`, and `current: null`, with explicit instructions to refresh and not repeat a destructive reset.

---

## Outbound Commands (C# → C++)

### Phase 1 — Implemented and Tested ✓

#### MOVE_TO
Walk to coordinates using pathfinding.

```json
{
  "type": "MOVE_TO",
  "payload": {
    "guid": 12345,
    "mapId": 0,
    "x": -8950.0,
    "y": -130.0,
    "z": 83.0
  }
}
```

**C++ behavior:**
- Rejected if `me->IsInCombat()` (logged, deferred)
- Rejected if `mapId != me->GetMapId()` (cross-map not supported)
- Calls `StopMoving()` then `MovePoint(AIBOT_POINT_TASK_DEST, x, y, z, MOVE_PATHFINDING)`
- Sets `m_currentTask.type = TASK_MOVE_TO`
- Fires `TASK_COMPLETE` event on arrival via `MovementInform()`

#### SAY_TEXT
Make the bot speak or yell.

```json
{
  "type": "SAY_TEXT",
  "payload": {
    "guid": 12345,
    "text": "Looking for group!",
    "chatType": 0
  }
}
```

**chatType:** `0` = `me->Say()`, `6` = `me->Yell()`. Whisper (7) is NOT implemented outbound.

#### PING
Keepalive — no-op on C++ side.

```json
{"type":"PING","payload":{}}
```

### Phase 2.5 — Implemented, Testing In Progress

#### ACCEPT_QUEST

```json
{
  "type": "ACCEPT_QUEST",
  "payload": { "quest_id": 6 }
}
```

**C++ behavior:**
- Validates `CanTakeQuest()` + `CanAddQuest()`
- Calls `me->AddQuest(pQuest, nullptr)`
- Auto-calls `CompleteQuest()` if objectives already satisfied
- Fires `QUEST_UPDATE` with status `"accepted"` on success
- Fires `QUEST_FAILED` with reason on failure

#### COMPLETE_QUEST

```json
{
  "type": "COMPLETE_QUEST",
  "payload": { "quest_id": 6 }
}
```

**C++ behavior:**
- Validates quest is in log (`GetQuestStatus != QUEST_STATUS_NONE`)
- Calls `me->FullQuestComplete(questId)` — awards items, XP, reputation, money
- Fires `QUEST_UPDATE` with status `"rewarded"`

> **Note:** `FullQuestComplete` does NOT require proximity to the quest giver. This is by design for bot automation.

#### ABANDON_QUEST

```json
{
  "type": "ABANDON_QUEST",
  "payload": { "quest_id": 6 }
}
```

**C++ behavior:**
- Sets `QuestStatus` to `QUEST_STATUS_NONE`
- Fires `QUEST_UPDATE` with status `"abandoned"`

#### LEARN_SPELL

```json
{
  "type": "LEARN_SPELL",
  "payload": { "spell_id": 133 }
}
```

**C++ behavior:**
- Calls `me->LearnSpell(spellId, false)` directly
- Silently skips if bot already knows the spell
- Does NOT deduct gold — cost tracking is C#'s responsibility via `bot_training_log`

#### ATTACK_TARGET

```json
{
  "type": "ATTACK_TARGET",
  "payload": { "guid": 54321 }
}
```

**C++ behavior:**
- `guid` is the creature's `GetGUIDLow()` counter value
- Looks up creature on current map via `ObjectGuid(HIGHGUID_UNIT, uint32(guidLow))`
- Validates `IsValidHostileTarget()`
- Calls `AttackStart(pCreature)` — handles role-aware chase distance
- Once engaged, autonomous combat rotation (`UpdateInCombatAI_*`) takes over entirely

#### INTERACT_NPC

```json
{
  "type": "INTERACT_NPC",
  "payload": { "guid": 54321 }
}
```

**C++ behavior:**
- `guid` is the NPC's `GetGUIDLow()` counter value
- If distance > 10yd: moves to contact point via `MovePoint()`, defers interaction
- If distance ≤ 10yd: `SetFacingToObject(pCreature)`, fires `NPC_INTERACT` event
- Does NOT open vendor/trainer/quest UI — those require separate commands

### Phase 3 — Planned, Need C++ Handlers

#### High Priority (blocking domain functionality)

| Command | Payload | What It Would Do | Needed By |
|---------|---------|-------------------|-----------|
| `STOP` | (none) | `StopMoving()` — cancel all movement | All domains |
| `EAT_DRINK` | (none) | Call `DrinkAndEat()` on demand | CombatDomain post-fight |
| `USE_MOUNT` | (none) | Call `UseMount()` — race/class/level aware | Explore, Questing |
| `DISMOUNT` | (none) | `RemoveSpellsCausingAura(SPELL_AURA_MOUNTED)` | Combat entry, NPC interact |
| `SELL_ITEM` | `item_id`, `count`, `vendor_guid` | Sell from inventory to vendor | EconomyDomain |
| `BUY_ITEM` | `item_id`, `count`, `vendor_guid` | Buy from vendor | EconomyDomain |
| `LOOT_CORPSE` | `corpse_guid` | Loot a killed creature's corpse | CombatDomain → EconomyDomain |
| `WHISPER` | `target_name`, `text` | `me->Whisper()` to a player | SocialDomain, Ollama chat |

#### Medium Priority

| Command | Payload | What It Would Do | Needed By |
|---------|---------|-------------------|-----------|
| `CAST_SPELL` | `spell_id`, `target_guid` (0=self) | Generic `me->CastSpell()` | Utility buffs, professions |
| `USE_ITEM` | `item_id` | Use a consumable or quest item | Economy, Questing |
| `EQUIP_ITEM` | `item_id`, `slot` | Equip gear from inventory | EconomyDomain |
| `SET_TASK_STATE` | `state` string | Override `taskState` for C# coordination | All domains |
| `EMOTE` | `emote_id` | `HandleEmoteCommand()` | SocialDomain |

#### Low Priority

| Command | Payload | What It Would Do | Needed By |
|---------|---------|-------------------|-----------|
| `JOIN_GROUP` | `target_guid` | Accept/send group invite | SocialDomain |
| `LEAVE_GROUP` | (none) | Leave current party | SocialDomain |
| `FOLLOW_PLAYER` | `target_guid`, `distance` | `MoveFollow()` | SocialDomain |
| `TAXI` | `taxi_path_id` | `ActivateTaxiPathTo()` | QuestingDomain |
| `SET_SHEATH` | `sheath_state` | Visual weapon display (0/1/2) | SocialDomain (RP) |

---

## Autonomous C++ Behaviors (NOT bridge-controlled)

These run automatically in `UpdateAI()`. C# does not control them — the bridge commands operate *alongside* these behaviors:

| Behavior | Method | When | Notes |
|----------|--------|------|-------|
| Combat rotation | `UpdateInCombatAI_*()` | `me->IsInCombat()` | 9 class-specific rotations (verbatim from BattleBotAI) |
| Self-buff/prep | `UpdateOutOfCombatAI_*()` | Out of combat | Auras, weapon buffs, pet summon, stance |
| Target selection | `SelectAttackTarget()` | Out of combat, idle | Threat list → party assist → nearby hostile |
| Eat/drink | `DrinkAndEat()` | Out of combat, not full HP/mana | Uses AB_SPELL_FOOD (1131) / AB_SPELL_DRINK (1137) |
| Mounting | `UseMount()` | Out of combat, idle, level ≥ 40 | Race/class-aware mount spell. Rogues excluded |
| Random wander | `DoRandomWander()` | Idle, no task, no target | 15yd radius, 10-20s timer |
| Self-revive | Death handler | `GetDeathState() == DEAD` | `ResurrectPlayer(0.5f)` — revives at 50% HP |
| CC break | `BreakCrowdControlEffects()` | Has CC aura | Inherited from CombatBotBaseAI |
| Unreachable target | `CheckForUnreachableTarget()` | Chase unreachable | Includes `NearTeleportTo` cheat for stuck pathing |
| Level-up refresh | Level detection | `GetLevel() > m_lastKnownLevel` | Re-runs `PopulateSpellData()`, `UpdateSkillsToMaxSkillsForLevel()` |
| Ammo replenish | `AddHunterAmmo()` | Auto shot NEED_AMMO | Hunter only |
| Pet management | `SummonPetIfNeeded()` | Out of combat, no pet | Hunter/Warlock |
| Stealth detection | Victim visibility check | Each tick | Stops chasing stealthed targets |

> **Design principle:** C# controls *strategic* decisions (where to go, which quest, when to train). C++ handles *tactical* execution (combat rotation, target selection, eat/drink, movement mechanics). The bridge is the interface between strategy and tactics.

---

## STATE Packet — Future Expansion

Fields available in C++ but not yet in the STATE packet. Add to `BridgeSendState()` as domains need them:

| Field | C++ Source | Type | Needed By |
|-------|-----------|------|-----------|
| `isMounted` | `me->IsMounted()` | bool | All travel domains |
| `copper` | `me->GetMoney()` | uint32 | EconomyDomain |
| `powerType` | `me->GetPowerType()` | int | UI (rage/energy display) |
| `power` | `me->GetPower(powerType)` | int | Warrior rage, Rogue energy |
| `maxPower` | `me->GetMaxPower(powerType)` | int | Percentage calculation |
| `comboPoints` | `me->GetComboPoints()` | int | Rogue/Druid |
| `shapeshiftForm` | `me->GetShapeshiftForm()` | int | Druid form tracking |
| `isMoving` | `me->IsMoving()` | bool | Movement state |
| `orientation` | `me->GetOrientation()` | float | Facing direction |
| `standState` | `me->GetStandState()` | int | Sitting/standing |
| `petGuid` | pet->GetGUIDLow() | int | Pet tracking |
| `petHealthPct` | pet health % | float | Pet monitoring |
| `isStealthed` | `HasAuraType(SPELL_AURA_MOD_STEALTH)` | bool | Rogue/Druid |

---

## Future Events — Need C++ Senders

Events C++ should send but doesn't yet. Add as domains require them:

| Event | Trigger Point | Payload | Needed By |
|-------|--------------|---------|-----------|
| `COMBAT_START` | `AttackStart()` | `target_guid`, `target_entry`, `target_level` | CombatDomain |
| `COMBAT_END` | Last attacker dies / evade | `duration_ms`, `kills` | CombatDomain |
| `INVENTORY_UPDATE` | Item gained/lost/equipped | `item_id`, `count`, `action` | EconomyDomain |
| `MONEY_UPDATE` | Gold changed | `copper_total` | EconomyDomain |
| `QUEST_OBJECTIVE` | Kill/item progress | `quest_id`, `obj_index`, `current`, `required` | QuestingDomain |
| `ZONE_CHANGE` | Zone transition | `old_zone`, `new_zone` | All domains |
| `REACHED_NPC` | Contact point reached | `npc_guid`, `npc_entry` | QuestingDomain |
| `LOOT_RECEIVED` | Loot from corpse | `item_id`, `count`, `creature_entry` | EconomyDomain |
| `SPELL_LEARNED` | LearnSpell completed | `spell_id` | TrainingDomain |
| `MOUNT_STATE` | Mounted/dismounted | `mounted` bool | State tracking |

---

## C++ Implementation Notes

The AiBotAI TCP client:
1. Uses a **non-blocking socket** — `fcntl(O_NONBLOCK)` on Linux, `ioctlsocket(FIONBIO)` on Windows
2. Buffers incoming data in `m_bridgeRecvBuf` (fixed `BRIDGE_RECV_BUF_SIZE`), splits on `\n`
3. Uses hand-rolled `JsonExtractString/Int/Float` — no JSON library dependency
4. `BridgeSend()` writes JSON + newline synchronously (messages are small, TCP buffering sufficient)
5. Reconnects on disconnect with exponential backoff
6. `BridgeRecv()` called every `UpdateAI()` tick — non-blocking recv processes all available data

## Error Handling

- Malformed JSON lines: logged and skipped (no disconnect)
- Unknown message types: logged and skipped
- Payload parse failures: message dropped with warning
- TCP disconnect: C# marks bot `DISCONNECTED`, C++ attempts reconnect
- Buffer overflow: buffer cleared, logged
- MOVE_TO while in combat: deferred with log message
- Quest validation failures: `QUEST_FAILED` event returned to C# with reason string
