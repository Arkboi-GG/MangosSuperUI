# PlayerInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerInfo

**PlayerInfo** is a lightweight, aggregate `struct` defined within `Channel.h` that encapsulates the membership state of a single player within a specific chat channel. It stores the player’s unique identifier (`ObjectGuid`) and a bitmask of permissions and restrictions (`uint8 flags`).

This unit serves as the value type for the `Channel` class’s internal member list (`std::map<ObjectGuid, PlayerInfo>`). It provides no persistence logic, network serialization, or complex validation; its sole responsibility is to hold the current authorization state (Owner, Moderator, Muted, etc.) for a player in memory while they are connected to the channel.

## Purpose & Responsibilities

The primary purpose of **PlayerInfo** is to provide a clean, encapsulated interface for managing the bitwise flags associated with a channel member. Instead of exposing the raw `uint8 flags` variable directly to the rest of the `Channel` class, **PlayerInfo** offers boolean getters and setters that abstract the bit manipulation.

Key responsibilities include:
1.  **Identity Storage:** Holding the `ObjectGuid` of the player.
2.  **State Management:** Maintaining the `flags` byte, which encodes roles such as Owner, Moderator, and Mute status.
3.  **Access Control Abstraction:** Providing methods like `IsOwner()`, `IsModerator()`, and `IsMuted()` to query state, and `SetOwner()`, `SetModerator()`, and `SetMuted()` to modify state safely.

## Member-by-Member Behavior

The members of **PlayerInfo** are grouped by the aspect of channel membership they manage.

### Identity and Generic Flag Management

*   **`player`**: A public member variable of type `ObjectGuid`. It identifies the specific player instance associated with this channel membership record.
*   **`flags`**: A public member variable of type `uint8`. It stores the bitwise combination of `ChannelMemberFlags` (e.g., `MEMBER_FLAG_OWNER`, `MEMBER_FLAG_MODERATOR`).
*   **`HasFlag(uint8 flag)`**: Returns `true` if the specified `flag` bit is set in the `flags` member. It performs a bitwise AND operation (`flags & flag`).
*   **`SetFlag(uint8 flag)`**: Sets the specified `flag` bit in the `flags` member. It first checks if the flag is already set using `HasFlag`; if not, it performs a bitwise OR operation (`flags |= flag`) to add it. This prevents redundant operations but does not remove existing flags.

### Ownership State

*   **`IsOwner()`**: Returns `true` if the `MEMBER_FLAG_OWNER` bit is set. This indicates the player has ultimate control over the channel.
*   **`SetOwner(bool state)`**: Sets or clears the `MEMBER_FLAG_OWNER` bit.
    *   If `state` is `true`, it sets the bit (`flags |= MEMBER_FLAG_OWNER`).
    *   If `state` is `false`, it clears the bit (`flags &= ~MEMBER_FLAG_OWNER`).
    *   This method allows toggling ownership status explicitly.

### Moderation State

*   **`IsModerator()`**: Returns `true` if the `MEMBER_FLAG_MODERATOR` bit is set. Moderators typically have permissions to kick, ban, or mute other users, depending on channel settings.
*   **`SetModerator(bool state)`**: Sets or clears the `MEMBER_FLAG_MODERATOR` bit.
    *   If `state` is `true`, it sets the bit.
    *   If `state` is `false`, it clears the bit.

### Mute State

*   **`IsMuted()`**: Returns `true` if the `MEMBER_FLAG_MUTED` bit is set. A muted player cannot send messages to the channel.
*   **`SetMuted(bool state)`**: Sets or clears the `MEMBER_FLAG_MUTED` bit.
    *   If `state` is `true`, it sets the bit.
    *   If `state` is `false`, it clears the bit.

## Cross-Unit Boundaries

**PlayerInfo** is a nested struct within the `Channel` class definition in `Channel.h`. It does not define any external dependencies itself; all interactions occur through the `Channel` class methods that manipulate instances of `PlayerInfo`.

### Called By (Other Units)

The following members of the **Channel** class (defined in `Channel.cpp` or inline in `Channel.h`) interact with **PlayerInfo** members:

*   **`game_Chat_Channel/Leave`**: Calls **`IsOwner`**.
    *   *Context:* When a player leaves a channel, the system needs to determine if the leaving player was the owner. If so, ownership must be transferred to another member before the player is removed.
*   **`game_Chat_Channel/SetOwner#2`**: Calls **`SetOwner`**.
    *   *Context:* This overload of `SetOwner` likely handles the transfer of ownership from one player to another. It updates the `PlayerInfo` of the new owner to mark them as the owner.
*   **`game_Chat_Channel/Announce`**, **`KickOrBan`**, **`Moderate`**, **`Password`**, **`Say`**, **`SetMode`**, **`UnBan`**: Call **`IsModerator`**.
    *   *Context:* These actions require moderator privileges. Before executing commands like kicking a user, changing the password, or enabling moderation mode, the `Channel` class checks if the requesting player’s `PlayerInfo` has the `MEMBER_FLAG_MODERATOR` bit set.
*   **`game_Chat_Channel/Join`**, **`SetOwner`**: Call **`SetModerator`**.
    *   *Context:* When a player joins a channel, they may be automatically granted moderator status (e.g., if they are the first member or if the channel policy dictates). Similarly, when ownership is set, the new owner is often implicitly made a moderator.
*   **`game_Chat_Channel/Say`**: Calls **`IsMuted`**.
    *   *Context:* Before broadcasting a message to the channel, the system checks if the sender is muted. If `IsMuted()` returns `true`, the message is rejected or ignored.

### Calls Out (Other Units)

**PlayerInfo** does not call any other units. It is a pure data structure with inline helper methods that operate solely on its own member variables.

## Data Model

**PlayerInfo** does not interact directly with any database tables. It is an in-memory representation of a player's state within a channel session. The `Channel` class may persist channel data to the database upon shutdown or specific events, but **PlayerInfo** itself contains no SQL queries, table references, or schema definitions.

## Notable Implementation Details

1.  **Bitwise Flag Management**: All state changes rely on bitwise operations. The `SetFlag` method includes a guard (`if (!HasFlag(flag))`) to avoid unnecessary writes, though this is largely a micro-optimization for a single-byte variable.
2.  **No Validation Logic**: **PlayerInfo** does not validate whether a player *can* be an owner or moderator. It blindly sets or clears bits based on the boolean arguments passed to `SetOwner`, `SetModerator`, and `SetMuted`. The enforcement of rules (e.g., "only one owner allowed") is handled by the calling `Channel` methods, not by **PlayerInfo**.
3.  **Public Members**: Both `player` and `flags` are public. This allows direct access if needed, but the provided getter/setter methods are preferred for consistency and potential future encapsulation changes.
4.  **Inline Implementation**: All methods are implemented inline within the header file. This ensures zero overhead for these simple checks and updates, which are called frequently during chat processing.
5.  **Dependency on Enumerations**: The behavior of the flag methods depends entirely on the `ChannelMemberFlags` enumeration defined in the same header. Specifically:
    *   `MEMBER_FLAG_OWNER` = `0x01`
    *   `MEMBER_FLAG_MODERATOR` = `0x02`
    *   `MEMBER_FLAG_MUTED` = `0x08`
    Misalignment between these enum values and the bits checked in **PlayerInfo** would break functionality, but since both are in the same header, this risk is minimal.

## Member Reference

*   **HasFlag**: Checks if a specific bit is set in the `flags` member using a bitwise AND operation.
*   **SetFlag**: Sets a specific bit in the `flags` member if it is not already set, using a bitwise OR operation.
*   **IsOwner**: Returns `true` if the `MEMBER_FLAG_OWNER` bit is set in `flags`.
*   **SetOwner**: Sets or clears the `MEMBER_FLAG_OWNER` bit in `flags` based on the boolean argument.
*   **IsModerator**: Returns `true` if the `MEMBER_FLAG_MODERATOR` bit is set in `flags`.
*   **SetModerator**: Sets or clears the `MEMBER_FLAG_MODERATOR` bit in `flags` based on the boolean argument.
*   **IsMuted**: Returns `true` if the `MEMBER_FLAG_MUTED` bit is set in `flags`.
*   **SetMuted**: Sets or clears the `MEMBER_FLAG_MUTED` bit in `flags` based on the boolean argument.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerInfo

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HasFlag | method | — | — | — |
| SetFlag | method | — | — | — |
| IsOwner | method | — | game_Chat_Channel/Leave | — |
| SetOwner | method | — | game_Chat_Channel/SetOwner#2 | — |
| IsModerator | method | — | game_Chat_Channel/Announce, game_Chat_Channel/KickOrBan, game_Chat_Channel/Moderate, game_Chat_Channel/Password, game_Chat_Channel/Say, game_Chat_Channel/SetMode, game_Chat_Channel/UnBan | — |
| SetModerator | method | — | game_Chat_Channel/Join, game_Chat_Channel/SetOwner | — |
| IsMuted | method | — | game_Chat_Channel/Say | — |
| SetMuted | method | — | — | — |
