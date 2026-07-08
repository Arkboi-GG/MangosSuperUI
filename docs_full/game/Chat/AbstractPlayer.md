<!-- provenance: verbose -->
# AbstractPlayer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`AbstractPlayer` defines a pure virtual interface for player-like entities in the chat and channel subsystems. `PlayerWrapper<T>` is a template adapter that implements this interface by holding a reference to a concrete `Player` or `MasterPlayer` object. This design allows `game_Chat_Channel`, `WorldSession` handlers, and `ChannelMgr` to operate on both human players and AI bots uniformly without branching on type. The unit provides no data persistence; it operates entirely on in-memory objects.

## Member-by-Member Behavior

### Abstract Interface (`AbstractPlayer`)
Defined in `AbstractPlayer.h`, this class contains only pure virtual functions serving as the contract for the wrapper.
*   **Identity & State**: Pure virtual accessors for GUID, name, team, zone/area IDs, class, race, level, guild ID, AFK/DND status, GM status, chat tag, session, and social list.
*   **Lifecycle**: Pure virtual callbacks `JoinedChannel` and `LeftChannel` for notifying the underlying object of channel membership changes.
*   **Casting & Validation**: Pure virtual `ToPlayer` and `ToMasterPlayer` for downcasting, and `ok` for validity checks.

### Concrete Adapter (`PlayerWrapper<T>`)
Implemented in `AbstractPlayer.cpp`, this template class holds a reference `T& player` and delegates all calls to it.
*   **Constructors**:
    *   `PlayerWrapper(T&)` and `PlayerWrapper(T*)`: Initialize the internal reference from a valid object or pointer.
    *   `PlayerWrapper()`: Default constructor initializes the reference to a dereferenced null pointer (`*((T*)nullptr)`), creating an invalid state detectable by `ok()`.
    *   `PlayerWrapper(const PlayerWrapper<T>&)`: Copy constructor copies the internal reference.
*   **Delegation**: All getter methods (`GetObjectGuid`, `GetName`, etc.) and lifecycle methods (`JoinedChannel`, `LeftChannel`) forward directly to the underlying `player` reference.
*   **Specialized Casting**: `ToPlayer` and `ToMasterPlayer` are explicitly specialized. `PlayerWrapper<Player>::ToPlayer()` returns the object address; `ToMasterPlayer()` returns `nullptr`. Conversely, `PlayerWrapper<MasterPlayer>::ToMasterPlayer()` returns the address, and `ToPlayer()` returns `nullptr`. This enforces strict type separation.
*   **Validity**: `ok()` returns `true` if the address of the internal reference is non-null.

## Cross-Unit Boundaries

### Called By (Consumers)
*   **`game_Chat_Channel`**: Uses `AbstractPlayer` for channel operations. It calls `GetObjectGuid`, `GetName`, `GetTeam`, `GetGuildId`, `GetSocial`, `GetSession`, and `ToPlayer`/`ToMasterPlayer` for invites, joins, and list displays. It uses `IsGameMaster`, `GetTeam`, and `GetSession` for permission checks (kick, ban, mute). It calls `JoinedChannel` and `LeftChannel` to update player state.
*   **`WorldSession.ChannelHandler`**: Handles channel opcodes. It retrieves `AbstractPlayer` instances to perform checks (e.g., `HandleChannelBanOpcode` checks `IsGameMaster`/`GetTeam`) and executes actions (e.g., `HandleJoinChannelOpcode` calls `JoinedChannel`).
*   **`WorldSession.ChatHandler`**: Uses `AbstractPlayer` for general chat handling, checking `GetLevel` and `GetName` for logging/formatting.
*   **`World/LogChat`**: Uses `GetName` and `GetObjectGuid` for logging.
*   **`ChannelMgr`**: Uses `GetSession` to retrieve channels.

### Calls Out (Dependencies)
`PlayerWrapper<T>` delegates to `Player` or `MasterPlayer` objects. It depends on `Channel`, `WorldSession`, `PlayerSocial`, `ObjectGuid`, and `Team` types for arguments and return values.

## Data Model

This unit does not interact with database tables. It operates entirely on in-memory `Player` and `MasterPlayer` objects.

## Notable Implementation Details

1.  **Undefined Behavior in Default Constructor**: `PlayerWrapper()` initializes the reference via `*((T*)nullptr)`. This is technically undefined behavior. The `ok()` method checks `(&player) != nullptr`, relying on the compiler/address representation to return false for this null-initialized reference. Accessing members on a default-constructed wrapper causes crashes.
2.  **No Ownership**: `PlayerWrapper` holds a reference, not a pointer. It does not manage the lifetime of the underlying `Player` or `MasterPlayer`. If the underlying object is destroyed while a wrapper exists (e.g., in a channel list), the wrapper becomes dangling. `ok()` does not detect this; it only detects the default-constructed null state.
3.  **Explicit Instantiation**: `template class PlayerWrapper<Player>;` and `template class PlayerWrapper<MasterPlayer>;` in `.cpp` ensure compiled code is available for linkers.
4.  **`PlayerExtraFlags` Enum**: Defined in the header but unused by `AbstractPlayer` or `PlayerWrapper`. Likely provided for convenience to other units including this header.

## Member Reference

**PlayerWrapper<T>#3** (ctor): Constructs from a `T&` reference.

**PlayerWrapper<T>#4** (ctor): Constructs from a `T*` pointer, dereferencing it.

**PlayerWrapper<T>** (ctor): Default constructor; initializes reference to dereferenced null pointer.

**PlayerWrapper<T>#2** (ctor): Copy constructor; copies internal reference.

**GetObjectGuid** (function): Delegates to `player.GetObjectGuid()`.

**GetTeam** (function): Delegates to `player.GetTeam()`.

**GetName** (function): Delegates to `player.GetName()`.

**GetZoneId** (function): Delegates to `player.GetZoneId()`.

**GetAreaId** (function): Delegates to `player.GetAreaId()`.

**GetClass** (function): Delegates to `player.GetClass()`.

**GetRace** (function): Delegates to `player.GetRace()`.

**GetLevel** (function): Delegates to `player.GetLevel()`.

**IsAFK** (function): Delegates to `player.IsAFK()`.

**IsDND** (function): Delegates to `player.IsDND()`.

**~AbstractPlayer** (dtor): Virtual destructor.

**GetObjectGuid#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::GetObjectGuid`. Called by `game_Chat_Channel` and `WorldSession.ChannelHandler`.

**IsGameMaster** (function): Delegates to `player.IsGameMaster()`.

**GetTeam#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::GetTeam`. Called by `game_Chat_Channel` and `WorldSession.ChannelHandler`.

**GetChatTag** (function): Delegates to `player.GetChatTag()`.

**GetName#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::GetName`. Called by `game_Chat_Channel` and `World/LogChat`.

**GetZoneId#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::GetZoneId`.

**GetAreaId#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::GetAreaId`.

**GetGuildId** (function): Delegates to `player.GetGuildId()`.

**GetClass#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::GetClass`.

**GetRace#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::GetRace`.

**GetSession** (function): Delegates to `player.GetSession()`.

**GetLevel#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::GetLevel`. Called by `WorldSession.ChannelHandler` and `WorldSession.ChatHandler`.

**GetGuildId#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::GetGuildId`. Called by `game_Chat_Channel`.

**GetSocial** (function): Delegates to `player.GetSocial()`.

**IsAFK#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::IsAFK`.

**IsDND#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::IsDND`.

**JoinedChannel** (function): Delegates to `player.JoinedChannel(c)`.

**IsGameMaster#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::IsGameMaster`.

**GetChatTag#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::GetChatTag`. Called by `game_Chat_Channel`.

**LeftChannel** (function): Delegates to `player.LeftChannel(c)`.

**GetSession#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::GetSession`. Called by `ChannelMgr` and `game_Chat_Channel`.

**GetSocial#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::GetSocial`. Called by `game_Chat_Channel`.

**ok** (function): Returns `true` if internal reference address is non-null.

**JoinedChannel#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::JoinedChannel`.

**LeftChannel#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::LeftChannel`. Called by `game_Chat_Channel`.

**ToPlayer#2** (method): Specialization for `PlayerWrapper<Player>`; returns `&player`.

**ToPlayer#3** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper` specializations. Called by `game_Chat_Channel`.

**ToMasterPlayer#3** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper` specializations. Called by `game_Chat_Channel`.

**ok#2** (decl): Pure virtual in `AbstractPlayer`; implemented by `PlayerWrapper<T>::ok`.

**ToMasterPlayer#2** (method): Specialization for `PlayerWrapper<MasterPlayer>`; returns `&player`.

**ToPlayer** (method): Specialization for `PlayerWrapper<MasterPlayer>`; returns `nullptr`.

**ToMasterPlayer** (method): Specialization for `PlayerWrapper<Player>`; returns `nullptr`.

**ToPlayer#4** (decl): Pure virtual in `AbstractPlayer`.

**ToMasterPlayer#4** (decl): Pure virtual in `AbstractPlayer`.

---

<!-- machine-true, projected from graph.json -->

## Map — AbstractPlayer

*Source:* AbstractPlayer.cpp, AbstractPlayer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerWrapper<T>#3 | ctor | — | — | — |
| PlayerWrapper<T>#4 | ctor | — | — | — |
| PlayerWrapper<T> | ctor | — | — | — |
| PlayerWrapper<T>#2 | ctor | — | — | — |
| GetObjectGuid | function | — | — | — |
| GetTeam | function | — | — | — |
| GetName | function | — | — | — |
| GetZoneId | function | — | — | — |
| GetAreaId | function | — | — | — |
| GetClass | function | — | — | — |
| GetRace | function | — | — | — |
| GetLevel | function | — | — | — |
| IsAFK | function | — | — | — |
| IsDND | function | — | — | — |
| ~AbstractPlayer | dtor | — | — | — |
| GetObjectGuid#2 | decl | — | game_Chat_Channel/Invite, game_Chat_Channel/List, game_Chat_Channel/SetMode, World/LogChat, WorldSession.ChannelHandler/HandleChannelAnnouncementsOpcode, WorldSession.ChannelHandler/HandleChannelBanOpcode, WorldSession.ChannelHandler/HandleChannelInviteOpcode, WorldSession.ChannelHandler/HandleChannelKickOpcode, WorldSession.ChannelHandler/HandleChannelModerateOpcode, WorldSession.ChannelHandler/HandleChannelModeratorOpcode, WorldSession.ChannelHandler/HandleChannelMuteOpcode, WorldSession.ChannelHandler/HandleChannelOwnerOpcode, WorldSession.ChannelHandler/HandleChannelPasswordOpcode, WorldSession.ChannelHandler/HandleChannelSetOwnerOpcode, WorldSession.ChannelHandler/HandleChannelUnbanOpcode, WorldSession.ChannelHandler/HandleChannelUnmoderatorOpcode, WorldSession.ChannelHandler/HandleChannelUnmuteOpcode, WorldSession.ChannelHandler/HandleJoinChannelOpcode, WorldSession.ChannelHandler/HandleLeaveChannelOpcode, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| IsGameMaster | function | — | — | — |
| GetTeam#2 | decl | — | game_Chat_Channel/Invite, game_Chat_Channel/SetMode, game_Chat_Channel/SetOwner, WorldSession.ChannelHandler/HandleChannelAnnouncementsOpcode, WorldSession.ChannelHandler/HandleChannelBanOpcode, WorldSession.ChannelHandler/HandleChannelInviteOpcode, WorldSession.ChannelHandler/HandleChannelKickOpcode, WorldSession.ChannelHandler/HandleChannelListOpcode, WorldSession.ChannelHandler/HandleChannelModerateOpcode, WorldSession.ChannelHandler/HandleChannelModeratorOpcode, WorldSession.ChannelHandler/HandleChannelMuteOpcode, WorldSession.ChannelHandler/HandleChannelOwnerOpcode, WorldSession.ChannelHandler/HandleChannelPasswordOpcode, WorldSession.ChannelHandler/HandleChannelSetOwnerOpcode, WorldSession.ChannelHandler/HandleChannelUnbanOpcode, WorldSession.ChannelHandler/HandleChannelUnmoderatorOpcode, WorldSession.ChannelHandler/HandleChannelUnmuteOpcode, WorldSession.ChannelHandler/HandleJoinChannelOpcode, WorldSession.ChannelHandler/HandleLeaveChannelOpcode, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| GetChatTag | function | — | — | — |
| GetName#2 | decl | — | game_Chat_Channel/Invite, World/LogChat | — |
| GetZoneId#2 | decl | — | — | — |
| GetAreaId#2 | decl | — | — | — |
| GetGuildId | function | — | — | — |
| GetClass#2 | decl | — | — | — |
| GetRace#2 | decl | — | — | — |
| GetSession | function | — | — | — |
| GetLevel#2 | decl | — | WorldSession.ChannelHandler/HandleChannelInviteOpcode, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| GetGuildId#2 | decl | — | game_Chat_Channel/Join | — |
| GetSocial | function | — | — | — |
| IsAFK#2 | decl | — | — | — |
| IsDND#2 | decl | — | — | — |
| JoinedChannel | function | — | — | — |
| IsGameMaster#2 | decl | — | — | — |
| GetChatTag#2 | decl | — | game_Chat_Channel/Say | — |
| LeftChannel | function | — | — | — |
| GetSession#2 | decl | — | ChannelMgr/GetChannel, game_Chat_Channel/Announce, game_Chat_Channel/Join, game_Chat_Channel/KickOrBan, game_Chat_Channel/Leave, game_Chat_Channel/Moderate, game_Chat_Channel/Password, game_Chat_Channel/Say, game_Chat_Channel/SendToAll, game_Chat_Channel/SendToOne, game_Chat_Channel/SetMode, game_Chat_Channel/SetOwner, game_Chat_Channel/SetOwner#2, game_Chat_Channel/UnBan, WorldSession.ChannelHandler/HandleJoinChannelOpcode, WorldSession.ChannelHandler/HandleLeaveChannelOpcode | — |
| GetSocial#2 | decl | — | game_Chat_Channel/Invite, game_Chat_Channel/SendToAll | — |
| ok | function | — | — | — |
| JoinedChannel#2 | decl | — | — | — |
| LeftChannel#2 | decl | — | game_Chat_Channel/Leave | — |
| ToPlayer#2 | method | — | — | — |
| ToPlayer#3 | decl | — | game_Chat_Channel/Join, game_Chat_Channel/List, game_Chat_Channel/Say | — |
| ToMasterPlayer#3 | decl | — | game_Chat_Channel/List | — |
| ok#2 | decl | — | — | — |
| ToMasterPlayer#2 | method | — | — | — |
| ToPlayer | method | — | — | — |
| ToMasterPlayer | method | — | — | — |
| ToPlayer#4 | decl | — | — | — |
| ToMasterPlayer#4 | decl | — | — | — |
