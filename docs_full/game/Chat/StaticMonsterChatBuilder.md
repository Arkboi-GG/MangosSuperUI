<!-- provenance: failed-members -->
# StaticMonsterChatBuilder

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# StaticMonsterChatBuilder

## Purpose & Responsibilities

`StaticMonsterChatBuilder` is a functor class within the `MaNGOS` namespace designed to construct `WorldPacket` instances for chat messages originating from creatures that do not have a live `Creature` object instance in memory. While related classes like `MonsterChatBuilder` operate on existing `WorldObject` instances, `StaticMonsterChatBuilder` synthesizes the sender's identity—specifically the GUID, name, and gender—directly from `CreatureInfo` template data. This capability allows the server to broadcast valid chat packets for scripted events, remote triggers, or pre-spawn announcements without the overhead of instantiating a full creature entity.

## Member-by-Member Behavior

### Construction: `StaticMonsterChatBuilder`

The constructor initializes the builder with the metadata required to generate a chat packet for a static source:
1.  **Sender Identity**: It accepts a pointer to `CreatureInfo` and generates a synthetic `ObjectGuid` using `i_cInfo->GetObjectGuid(senderLowGuid)`. The `senderLowGuid` parameter defaults to 0. Although a low GUID of 0 is typically unused for live objects, the client accepts this value for static sources, allowing the packet to be processed correctly without a valid high-guid assignment.
2.  **Gender Resolution**: It determines the sender's gender by querying `sObjectMgr.GetCreatureDisplayInfoAddon` for the first display ID defined in the creature template (`cInfo->display_id[0]`). If the addon data exists, the gender is extracted and stored; otherwise, it defaults to `GENDER_NONE`. This gender value is critical for resolving gender-specific text substitutions in broadcast texts.
3.  **Metadata Storage**: It stores the chat message type (`msgtype`), text ID (`textId`), language (`language`), and an optional target `Unit` pointer.

### Packet Generation: `operator()`

The `operator()` method populates a provided `WorldPacket` with chat data tailored for a specific locale index (`loc_idx`):
1.  **Text Retrieval**: If `i_textId` is positive, it retrieves the text string via `sObjectMgr.GetBroadcastText`, passing `i_senderGender` to handle gender-specific substitutions. If `i_textId` is non-positive, it retrieves the string via `sObjectMgr.GetMangosString`.
2.  **Name Resolution**: Because no live object exists to provide a name, the builder manually resolves the sender's name. It first attempts to find localized name overrides by calling `sObjectMgr.GetCreatureLocale` with the creature entry. If a `CreatureLocale` structure is found, it checks if the `loc_idx` is within bounds and if the corresponding name string is non-empty. If a valid localized name is found, it is used; otherwise, the builder falls back to the default name stored in `i_cInfo->name`.
3.  **Packet Construction**: It delegates the final packet assembly to `ChatHandler::BuildChatPacket`. It passes the resolved text, language, the synthetic sender GUID, the resolved sender name, and the target's GUID and name (if a target was provided during construction).

## Cross-Unit Boundaries

### Called By: `Map.Main/SendMonsterTextToMap`

The `Map` module instantiates and invokes `StaticMonsterChatBuilder` when broadcasting monster text to players on a map. This typically occurs when the monster is not fully instantiated or is referenced remotely. The `Map` unit supplies the `CreatureInfo`, message details, and target, receiving the populated `WorldPacket` via the functor call for subsequent transmission to connected clients.

### Calls Out: None

The unit relies on global managers (`sObjectMgr`, `ChatHandler`) for data retrieval and packet building but does not call out to other distinct architectural units as defined in the map.

## Data Model

This unit does not execute SQL queries directly. It accesses in-memory caches managed by `sObjectMgr`:
*   **Broadcast Texts**: Retrieved via `GetBroadcastText` (derived from the `broadcast_text` table).
*   **Mangos Strings**: Retrieved via `GetMangosString` (derived from the `mangos_string` table).
*   **Creature Locale**: Retrieved via `GetCreatureLocale` (derived from the `creature_locale` table) for localized name overrides.
*   **Creature Display Info Addon**: Retrieved via `GetCreatureDisplayInfoAddon` (derived from the `creature_display_info_addon` table) for gender data.
*   **Creature Info**: Passed as `CreatureInfo const*` (derived from the `creature_template` table).

## Notable Implementation Details

1.  **Synthetic GUID Handling**: The builder creates a GUID using `i_cInfo->GetObjectGuid(senderLowGuid)`. The source code comments explicitly note that a low GUID of 0 is accepted by the client, which avoids the performance cost of spawning a temporary `Creature` object solely for chat purposes.
2.  **Gender Derivation Limitation**: Gender is derived exclusively from the first display ID (`display_id[0]`). For creatures that change appearance (phase changes) and thus might use different display IDs, this static lookup may not reflect the current visual state, though it is sufficient for most static announcement contexts.
3.  **Locale Safety Checks**: The name resolution logic includes explicit bounds checking (`cl->Name.size() > (size_t)loc_idx`) and emptiness checks (`!cl->Name[loc_idx].empty()`) to prevent accessing invalid indices or using empty strings, ensuring a fallback to the default template name is always available.
4.  **Non-Const Functor**: Unlike `MonsterChatBuilder::operator()`, `StaticMonsterChatBuilder::operator()` is not marked `const`. This is notable because the method performs no visible state mutation, suggesting a potential inconsistency in the interface design between the two builder classes.

## Member Reference

**StaticMonsterChatBuilder**
Constructor initializing the builder with `CreatureInfo`, chat type, text ID, language, and optional target. It generates a synthetic sender GUID and resolves sender gender from the primary display info addon.

**operator()**
Functor method that populates a `WorldPacket` with chat data for a given locale. It retrieves text (handling gender substitution), resolves the sender's name from locale data or the default template, and builds the packet via `ChatHandler::BuildChatPacket` including target details if present.

---

<!-- machine-true, projected from graph.json -->

## Map — StaticMonsterChatBuilder

*Source:* MonsterChatBuilder.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| StaticMonsterChatBuilder | ctor | — | Map.Main/SendMonsterTextToMap | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
