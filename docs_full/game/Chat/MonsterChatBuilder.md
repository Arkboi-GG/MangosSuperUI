<!-- provenance: failed-members -->
# MonsterChatBuilder

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MonsterChatBuilder

## Purpose & Responsibilities

`MonsterChatBuilder` is a functor class within the `MaNGOS` namespace responsible for constructing network packets for chat messages originating from live `WorldObject` instances (typically monsters or NPCs). It encapsulates the logic required to resolve localized text strings and serialize them into the binary protocol expected by the World of Warcraft client.

The class is designed to be instantiated once with the necessary message context—source object, message type, text identifier, language, and optional target—and then invoked multiple times with different locale indices. This design allows the same message content to be efficiently packaged for multiple players in their respective languages without repeatedly querying the source object’s properties or re-evaluating the message structure.

## Member-by-Member Behavior

### Construction

**`MonsterChatBuilder`**
The constructor initializes the builder by capturing the static context required for the chat message:
*   `i_source`: A `const` reference to the `WorldObject` acting as the sender.
*   `i_msgtype`: The `ChatMsg` enumeration value defining the nature of the message (e.g., SAY, YELL).
*   `i_textId`: The identifier for the text content. Positive IDs typically refer to dynamic broadcast texts, while non-positive IDs refer to static system strings.
*   `i_language`: The language code associated with the message.
*   `i_target`: A pointer to the `Unit` targeted by the message, or `nullptr` if the message has no specific target.

### Packet Generation

**`operator()`**
This method serializes the chat message into a `WorldPacket` for a specific locale index (`loc_idx`). The process involves two main steps:
1.  **Text Resolution**:
    *   If `i_textId` is greater than 0, the method retrieves localized broadcast text by calling `sObjectMgr.GetBroadcastText`, passing the locale index and the source object's gender (`i_source.GetGender()`).
    *   If `i_textId` is 0 or negative, it retrieves a static string via `sObjectMgr.GetMangosString`.
2.  **Serialization**:
    *   The resolved text is passed to `ChatHandler::BuildChatPacket` to perform the actual binary serialization.
    *   Sender details (GUID and localized name) are extracted from `i_source`.
    *   Target details are extracted from `i_target` if it is not null; otherwise, an empty `ObjectGuid` and an empty string are used for the target fields.

## Cross-Unit Boundaries

### Called By
The `MonsterChatBuilder` constructor is invoked by high-level monster communication methods in `WorldObject.Object` and map-wide broadcasting logic in `Map.Main`:
*   `WorldObject.Object/MonsterSay#2`, `WorldObject.Object/MonsterYell#2`, `WorldObject.Object/MonsterTextEmote#2`, `WorldObject.Object/MonsterScriptToZone`, and `WorldObject.Object/MonsterYellToZone`: These methods instantiate the builder to prepare message data before it is distributed to relevant players.
*   `Map.Main/SendMonsterTextToMap`: Uses the builder to format text for broadcast to all players within a specific map zone.

### Calls Out
*   **`sObjectMgr`**: The global object manager is queried to fetch text strings (`GetBroadcastText` and `GetMangosString`) based on the text ID and locale.
*   **`ChatHandler`**: The static method `ChatHandler::BuildChatPacket` is called to serialize the final chat data into the `WorldPacket` buffer.

## Data Model

This unit does not directly access database tables. All text data is retrieved from memory caches managed by `sObjectMgr`, which presumably loads this data from the database during server startup or hot-reload events.

## Notable Implementation Details

*   **Live Object Dependency**: Unlike the `StaticMonsterChatBuilder` class defined in the same header, `MonsterChatBuilder` requires a live `WorldObject` reference. It relies on this live object to dynamically determine gender and name at the time of packet construction. It cannot be used for static creature data alone.
*   **No Variable Arguments**: This specific builder class does not support `va_list` formatting. It assumes the text ID resolves to a complete string or a string that does not require runtime argument substitution. For messages requiring variable argument formatting, the `MonsterChatBuilderFormat` class (also in this header) is used instead.
*   **Const Correctness**: The `operator()` method is marked `const`, ensuring that the builder instance remains immutable after construction. This allows the same builder object to be safely reused across multiple locale iterations without side effects.

## Member Reference

**MonsterChatBuilder**
Constructor that captures the source object, message type, text ID, language, and target to prepare the context for subsequent packet generation.

**operator()**
Method that resolves localized text using `sObjectMgr` and serializes the chat message into a `WorldPacket` via `ChatHandler::BuildChatPacket`, incorporating sender and target details.

---

<!-- machine-true, projected from graph.json -->

## Map — MonsterChatBuilder

*Source:* MonsterChatBuilder.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MonsterChatBuilder | ctor | — | Map.Main/SendMonsterTextToMap, WorldObject.Object/MonsterSay#2, WorldObject.Object/MonsterScriptToZone, WorldObject.Object/MonsterTextEmote#2, WorldObject.Object/MonsterYell#2, WorldObject.Object/MonsterYellToZone | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
