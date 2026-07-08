<!-- provenance: failed-members -->
# MonsterChatBuilderFormat

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MonsterChatBuilderFormat

## Purpose & Responsibilities

`MonsterChatBuilderFormat` is a functor class within the `MaNGOS` namespace designed to construct chat packets for `WorldObject`s that require variable-argument string formatting. It serves as a specialized builder for scenarios where an NPC or world object speaks a message containing placeholders (e.g., `%s`, `%d`) that must be resolved at runtime using a `va_list`.

Unlike the simpler `MonsterChatBuilder` (which handles static strings) or `StaticMonsterChatBuilder` (which handles static creature info), this class is instantiated when dynamic content interpolation is needed. It is primarily used by `WorldObject.Object/PMonsterSay#2` and `WorldObject.Object/PMonsterYell#2` when those methods are invoked with variadic arguments.

## Member-by-Member Behavior

### Construction
The constructor `MonsterChatBuilderFormat` initializes the builder by capturing all necessary context for packet generation:
- **Source**: A constant reference to the speaking `WorldObject` (`i_source`).
- **Message Type**: The specific chat message type (`i_msgtype`, such as SAY or YELL).
- **Text ID**: An identifier for the text string. Positive IDs indicate lookup in `broadcast_text`; non-positive IDs indicate lookup in `mangos_string` (`i_textId`).
- **Language**: The language code for the message (`i_language`).
- **Target**: An optional pointer to the `Unit` being addressed (`i_target`).
- **Arguments**: A pointer to a `va_list` containing the arguments for string formatting (`i_vaList`).

### Packet Construction (`operator()`)
The `operator()` method generates the chat packet data for a specific locale index (`loc_idx`). Its execution flow is as follows:

1.  **Text Retrieval**: It determines the raw text template. If `i_textId` is positive, it calls `sObjectMgr.GetBroadcastText` using the source object's gender; otherwise, it calls `sObjectMgr.GetMangosString`.
2.  **String Formatting**: It creates a copy of the provided `va_list` and uses `vsnprintf` to format the template into a local 2048-byte character buffer named `textFinal`.
3.  **Packet Serialization**: It invokes `ChatHandler::BuildChatPacket` to populate the `WorldPacket`.

**Notable Implementation Detail**: There is a significant logical defect in this method. Although the code correctly formats the string into `textFinal`, it passes the original, unformatted `text` pointer (returned by the manager) to `ChatHandler::BuildChatPacket`. Consequently, the formatted result is discarded, and clients receive the raw format string with unresolved placeholders (e.g., `"Hello %s"` instead of `"Hello Player"`).

## Cross-Unit Boundaries

### Called By
-   **`WorldObject.Object/PMonsterSay#2`**: Instantiates this builder to handle "say" messages that include variadic arguments.
-   **`WorldObject.Object/PMonsterYell#2`**: Instantiates this builder to handle "yell" messages that include variadic arguments.

### Calls Out
-   The MAP indicates no explicit calls to other documented units. However, the implementation relies on global managers (`sObjectMgr`) and utility classes (`ChatHandler`) to perform lookups and packet construction. These are treated as internal dependencies within this scope.

## Data Model

This unit does not perform direct SQL queries or interact with database tables. It accesses text data through `sObjectMgr`, which caches content from tables such as `broadcast_text` and `mangos_string`. No schema details are relevant to this unit's direct operation.

## Notable Implementation Details

-   **Formatting Bug**: As detailed in the member behavior, `operator()` computes a formatted string into `textFinal` but ignores it, sending the unformatted template instead. This is a critical bug affecting all formatted monster chat.
-   **Buffer Size**: The formatted output is constrained to a 2048-byte stack buffer.
-   **Gender Handling**: When retrieving broadcast text, the builder uses `i_source.GetGender()` to select the appropriate grammatical form, ensuring consistency with the speaker's definition.
-   **Va List Safety**: The code correctly uses `va_copy` and `va_end` to manage the variable argument list, preventing corruption of the original list passed by the caller.

## Member Reference

**MonsterChatBuilderFormat**
Constructor that initializes the builder with a reference to the source `WorldObject`, the chat message type, the text ID, the language, an optional target `Unit`, and a pointer to a `va_list` for argument formatting.

**operator()**
Method that retrieves the text template based on the text ID and locale, formats it using the stored `va_list` into a local buffer, and then calls `ChatHandler::BuildChatPacket`. Note: It incorrectly passes the unformatted template string to the packet builder, resulting in raw format codes being sent to clients.

---

<!-- machine-true, projected from graph.json -->

## Map — MonsterChatBuilderFormat

*Source:* MonsterChatBuilder.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MonsterChatBuilderFormat | ctor | — | WorldObject.Object/PMonsterSay#2, WorldObject.Object/PMonsterYell#2 | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
