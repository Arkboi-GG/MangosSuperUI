# FriendInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# FriendInfo

**Purpose & Responsibilities**

`FriendInfo` is a lightweight data structure (POD-like `struct`) defined in `SocialMgr.h` that encapsulates the transient status information of a single friend or ignored player within the social system. It does not manage relationships, persistence, or network communication; rather, it serves as a value object passed between the social management subsystem (`SocialMgr`) and the client-facing packet generation logic.

Its primary responsibility is to hold the current state of a specific player relative to the observer, including whether they are online, their character class and level, their current area (zone/map), and any social flags (such as being muted or ignored). This structure allows the server to aggregate necessary display data before serializing it into a `WorldPacket` for transmission to the client.

**Member-by-Member Behavior**

The `FriendInfo` struct contains two constructors, both of which initialize the five member variables (`Status`, `Flags`, `Area`, `Level`, `Class`). The difference lies in how the `Flags` field is initialized, reflecting two distinct usage contexts within the `SocialMgr` unit.

1.  **Default Initialization (Offline/Unknown)**: The default constructor initializes all fields to zero or offline states. This represents a baseline state where a friend is known to exist in the list but has no active session data (offline) or no specific metadata is currently available.
2.  **Flag-Specific Initialization**: The second constructor accepts a `uint32 flags` argument. It initializes `Status` to `FRIEND_STATUS_OFFLINE` and sets `Flags` to the provided value, while keeping `Area`, `Level`, and `Class` at zero. This is used when the social relationship itself carries specific attributes (e.g., "this person is on my ignore list") but the target player is not currently online to provide dynamic status data.

**Cross-Unit Boundaries**

`FriendInfo` is purely a data carrier. It does not call out to other units. Its lifecycle is managed entirely by `SocialMgr`:

*   **Called by `SocialMgr/AddToSocialList`**: When a player adds a new friend or ignore entry, `SocialMgr` likely constructs a `FriendInfo` instance (using the default constructor) to populate the initial entry in the player's social map. Since the newly added friend is not necessarily online, the default offline state is appropriate.
*   **Called by `SocialMgr/SendFriendStatus`**: When sending status updates to a client, `SocialMgr` populates a `FriendInfo` structure with the latest known data for a specific friend. This populated structure is then used to build the outgoing network packet. The constructor choice here depends on whether the friend is online (dynamic data) or offline (static flags).
*   **Called by `SocialMgr/LoadFromDB`**: When loading a player's social list from the database, `SocialMgr` creates `FriendInfo` instances for each entry. Since database entries typically store static relationship data (like notes or flags) but not real-time online status, these instances are initialized with the flags stored in the DB, while status remains offline until the player logs in.

**Data Model**

`FriendInfo` itself does not interact with the database. However, the data it holds corresponds to columns in the `character_social` table (implied by `SocialMgr/LoadFromDB` and standard MaNGOS architecture). Specifically:
*   `Flags` maps to the `flags` column in `character_social`.
*   `Status`, `Area`, `Level`, and `Class` are transient runtime values derived from the `characters` table or the active session state of the target player, not stored persistently in `character_social`.

**Notable Implementation Details**

*   **No Dynamic Updates**: `FriendInfo` has no methods to update its fields after construction. It is immutable in practice once created. If a friend's status changes (e.g., goes online), a new `FriendInfo` object is constructed with the updated data rather than modifying the existing one. This simplifies concurrency handling since `SocialMgr` can replace entries in the `PlayerSocialMap` without worrying about partial updates.
*   **Zero-Initialization Safety**: Both constructors explicitly set all members. This prevents undefined behavior if a `FriendInfo` is copied or moved, ensuring that any unused fields (like `Area` for an offline player) are always zero.
*   **Flag Semantics**: The `Flags` field uses bitmasks defined in `SocialFlag` (`SOCIAL_FLAG_FRIEND`, `SOCIAL_FLAG_IGNORED`, `SOCIAL_FLAG_MUTED`). The second constructor allows setting these flags independently of status, which is crucial for distinguishing between a "friend who is offline" and an "ignored player who is offline."

## Member Reference

**FriendInfo** (default ctor): Initializes a `FriendInfo` instance with `Status` set to `FRIEND_STATUS_OFFLINE`, `Flags` to 0, and `Area`, `Level`, `Class` to 0. Used when creating a new social entry where no specific flags are set initially, such as when adding a new friend via `SocialMgr/AddToSocialList`.

**FriendInfo#2** (explicit ctor): Initializes a `FriendInfo` instance with `Status` set to `FRIEND_STATUS_OFFLINE`, `Flags` set to the provided `uint32` argument, and `Area`, `Level`, `Class` to 0. Used when loading social data from the database (`SocialMgr/LoadFromDB`) or when sending status packets (`SocialMgr/SendFriendStatus`) where the relationship flags (e.g., ignored, muted) are known but the player is offline.

---

<!-- machine-true, projected from graph.json -->

## Map — FriendInfo

*Source:* SocialMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FriendInfo | ctor | — | SocialMgr/AddToSocialList, SocialMgr/SendFriendStatus | — |
| FriendInfo#2 | ctor | — | SocialMgr/LoadFromDB | — |
