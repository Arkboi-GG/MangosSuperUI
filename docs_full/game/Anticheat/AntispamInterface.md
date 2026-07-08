# AntispamInterface

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`AntispamInterface` is a base class defining the contract for anti-spam logic in `wowvmangos`. All methods provide empty default implementations, making the class a **no-op stub**. This allows the server to compile and run without an active anti-spam module; if a concrete subclass is not linked, spam protections are silently disabled. The interface abstracts message normalization, filtering, history tracking, and account muting, enabling core chat and petition handlers to invoke anti-spam checks without hard dependencies on specific implementations.

## Member-by-Member Behavior

### Initialization
*   **`loadData`** / **`loadConfig`**: Empty stubs intended for loading persistent state (e.g., ban lists) and runtime configuration (e.g., thresholds). Currently perform no action.

### Message Processing
*   **`normalizeMessage`**: Returns the input string unchanged. Intended to preprocess text (e.g., stripping formatting, lowercasing) for analysis.
*   **`filterMessage`**: Always returns `false` (allowed). Intended to detect prohibited content or spam patterns.
*   **`addMessage`**: Discards all inputs. Intended to record message history for rate-limiting analysis.

### Muting Management
*   **`isMuted`**: Always returns `false`. Intended to check if an account is muted, optionally filtered by chat type.
*   **`mute`** / **`unmute`**: Empty stubs to apply or remove manual mutes on an account ID.
*   **`showMuted`**: Empty stub to display muted accounts to an administrator.

## Cross-Unit Boundaries

`AntispamInterface` is passive; it receives calls from other units but initiates none.

*   **`WorldSession.ChatHandler/HandleChatMessageOpcode`**: Calls `normalizeMessage`, `addMessage`, and `isMuted` to process incoming chat.
*   **`game_Guild_Guild/Create#2`** and **`WorldSession.PetitionsHandler/HandlePetitionBuyOpcode`**: Call `filterMessage` to validate guild/petition names and descriptions.
*   **`ChatHandler.AccountCommands/HandleSpamerMute`**, **`HandleSpamerUnmute`**, and **`HandleSpamerList`**: Call `mute`, `unmute`, and `showMuted` respectively for administrative control.

## Data Model

This unit contains no database queries or table references. Persistence is handled by external concrete implementations.

## Notable Implementation Details

*   **Silent Failure**: Because all methods are no-ops, missing anti-spam modules result in zero enforcement rather than errors.
*   **Decoupled Manager**: `AnticheatManager` (defined in the same header) returns `nullptr` for `GetAntispam()` and always allows whispers via `CanWhisper()`, confirming that anti-spam is not integrated into the central anti-cheat manager in this build.
*   **Thread Safety**: The interface imposes no synchronization. Concrete implementations must handle concurrency, as chat handlers execute per-session.

## Member Reference

*   **`~AntispamInterface`**: Virtual destructor for safe polymorphic deletion.
*   **`loadData`**: Stub to load persistent anti-spam data; currently does nothing.
*   **`loadConfig`**: Stub to load configuration; currently does nothing.
*   **`normalizeMessage`**: Returns input unchanged; intended to preprocess text.
*   **`filterMessage`**: Always returns `false`; intended to detect spam.
*   **`addMessage`**: Discards input; intended to record history.
*   **`isMuted`**: Always returns `false`; intended to check mute status.
*   **`mute`**: Stub to apply a mute; currently does nothing.
*   **`unmute`**: Stub to remove a mute; currently does nothing.
*   **`showMuted`**: Stub to list muted accounts; currently does nothing.

---

<!-- machine-true, projected from graph.json -->

## Map — AntispamInterface

*Source:* Anticheat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~AntispamInterface | dtor | — | — | — |
| loadData | method | — | — | — |
| loadConfig | method | — | — | — |
| normalizeMessage | method | — | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| filterMessage | method | — | game_Guild_Guild/Create#2, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode | — |
| addMessage | method | — | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| isMuted | method | — | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| mute | method | — | ChatHandler.AccountCommands/HandleSpamerMute | — |
| unmute | method | — | ChatHandler.AccountCommands/HandleSpamerUnmute | — |
| showMuted | method | — | ChatHandler.AccountCommands/HandleSpamerList | — |
