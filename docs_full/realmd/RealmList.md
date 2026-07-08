# RealmList

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# RealmList

## Purpose & Responsibilities

`RealmList` is the singleton authority for the server’s realm directory and client compatibility data within the `realmd` authentication service. It serves two distinct but related functions:

1.  **Realm Directory Management:** It loads, caches, and periodically refreshes the list of available game realms from the `realmlist` database table. This includes resolving hostnames to IP addresses, validating subnet masks for local network optimization, and filtering out offline realms.
2.  **Client Build Validation:** It maintains a whitelist of acceptable World of Warcraft client builds (`ExpectedRealmdClientBuilds`) loaded from the `allowed_clients` table. This allows the authentication socket to verify that connecting clients are running supported versions of the game.

The unit provides iterator-style access (`begin`, `end`, `size`) to the cached realm map, allowing other components (primarily `AuthSocket`) to enumerate realms for sending to clients. It also provides helper functions to resolve specific realm addresses based on the client's IP address (supporting local vs. external routing) and to determine realm category icons based on client build and zone.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`RealmList` (Constructor)**
Initializes the singleton instance. Sets the default update interval to 0 (disabled) and the next update time to the current epoch time.

**`~RealmList` (Destructor)**
Trivial destructor. No cleanup logic is performed; memory management relies on standard container destructors for `m_realms` and `ExpectedRealmdClientBuilds`.

**`Initialize`**
Called once during `realmd` startup via `realmd_Main/main`. It sets the configured update interval, triggers `LoadAllowedClients` to populate the client build whitelist, and calls `UpdateRealms(true)` to perform the initial load of the realm list from the database.

**`Instance`**
Returns a reference to the global `RealmList` singleton. Implemented using the Meyers Singleton pattern in the `.cpp` file.

### Realm Data Management

**`UpdateRealms`**
The core loading mechanism. It queries the `realmlist` table for all realms where `realmflags` does not include the `OFFLINE` bit (`(realmflags & 1) = 0`). For each row:
1.  It extracts fields including ID, name, addresses, port, icon, flags, timezone, security level, population, and supported builds.
2.  It validates `realmflags`, stripping any bits not explicitly allowed (`OFFLINE`, `NEW_PLAYERS`, `RECOMMENDED`, `SPECIFYBUILD`).
3.  It resolves `address` and `localAddress` strings to IPv4 addresses using `DNS::ResolveDomainSingle`. If resolution fails, the realm is skipped with an error log.
4.  It parses `localSubnetMask` from dotted-decimal notation (e.g., "255.255.255.0") into a CIDR prefix length (e.g., 24). It performs binary validation to ensure the mask is contiguous (no holes). If parsing or validation fails, the realm is skipped.
5.  It calls `UpdateRealm` to insert or update the realm in the internal `m_realms` map.

**`UpdateRealm`**
Updates the internal state for a single realm. It creates or retrieves a `Realm` object from `m_realms` keyed by name. It assigns basic properties (ID, icon, flags, etc.). It parses the space-separated `builds` string into a set of `uint32` build numbers. It determines the primary build info by looking up the lowest build number in `ExpectedRealmdClientBuilds` via `FindBuildInfo`. Finally, it constructs `IpEndpoint` objects for both external and local addresses.

**`UpdateIfNeed`**
Called periodically by `AuthSocket/_HandleRealmList`. If the update interval is non-zero and the current time exceeds `m_NextUpdateTime`, it clears the entire `m_realms` map and reloads it via `UpdateRealms(false)`. This ensures the realm list stays fresh without requiring a server restart.

**`begin`, `end`, `size`**
Standard STL map iterators and size accessor for `m_realms`. Used by `AuthSocket` to iterate over realms when constructing the realm list packet for clients.

### Client Build and Compatibility Helpers

**`LoadAllowedClients`**
Queries the `allowed_clients` table. For each row, it constructs a `RealmBuildInfo` structure, converting the hex string `integrity_hash` into a byte array. These structures are appended to the global `ExpectedRealmdClientBuilds` vector. This vector is sorted implicitly by insertion order (though comments suggest it should be sorted by build number for the lookup logic to work correctly as described below).

**`FindBuildInfo` (overload 1)**
Takes a `uint16` build number. It checks if the build is greater than or equal to the first entry in `ExpectedRealmdClientBuilds` (assumed to be the lowest supported build). If so, it returns that first entry. Otherwise, it linearly searches the rest of the vector for an exact match. Returns `nullptr` if not found.

**`FindBuildInfo` (overload 2)**
Takes build, OS, and platform. It performs a linear search through `ExpectedRealmdClientBuilds` returning pointers to all entries that match all three criteria.

**`GetRealmCategoryIdByBuildAndZone`**
Determines the icon/category ID for a realm based on the client's build and the realm's zone. It uses a static lookup table `RealmCategoryIdsByRealmZoneByMajorVersion`. It first finds the `RealmBuildInfo` for the given build. If the major version is less than 4 (Classic/TBC/WotLK eras), it indexes into the table. For newer builds (major version >= 4), it defaults to returning the zone index itself.

### Address Resolution

**`GetAddressForClient`**
A method on the `Realm` struct (declared in header, defined in cpp). Given a client's IP address, it decides whether to return the `externalAddress` or `localAddress`.
1.  If the client IP is not IPv4, it returns `externalAddress`.
2.  It checks if the client IP is in the same subnet as the realm's `localAddress` using the stored `localSubnetMaskCidr`.
3.  If yes, it returns `localAddress` (optimizing traffic for LAN clients). Otherwise, it returns `externalAddress`.

## Cross-Unit Boundaries

### Collaboration with `AuthSocket`

*   **`AuthSocket/LoadRealmlistAndWriteIntoBuffer`**: Calls `RealmList::Instance`, `begin`, `end`, `size`, `FindBuildInfo`, `GetRealmCategoryIdByBuildAndZone`, and `Realm::GetAddressForClient`. This is the primary consumer of the realm list. It iterates over the realms to construct the `SMSG_REALM_LIST` packet. For each realm, it uses `GetAddressForClient` to determine the correct IP to send to the specific client (handling LAN vs WAN routing). It uses `FindBuildInfo` and `GetRealmCategoryIdByBuildAndZone` to determine display icons and compatibility flags.
*   **`AuthSocket/_HandleRealmList`**: Calls `UpdateIfNeed`. This triggers the periodic refresh of the realm list.
*   **`AuthSocket/VerifyVersion`**: Calls `FindBuildInfo#2`. This verifies if the client's specific build/OS/platform combination is allowed.
*   **`AuthSocket/_HandleLogonProof__PostRecv`**: Calls `FindBuildInfo`. Likely used for final validation or logging of the client build during login.

### Collaboration with `realmd_Main`

*   **`realmd_Main/main`**: Calls `Initialize` to start the realm list system and `size` likely for logging or health checks.

### Collaboration with Networking and Utilities

*   **`DNS/ResolveDomainSingle`**: Called by `UpdateRealms` to convert hostname strings from the database into `IpAddress` objects.
*   **`IpAddress/TryParseFromString` & `_getInternalIPv4ReprAsUint32`**: Called by `UpdateRealms` to parse and validate subnet masks.
*   **`shared_Util/StrSplit`**: Called by `UpdateRealm` to split the space-separated build list string.
*   **`shared_Util/HexStrToByteArray`**: Called by `LoadAllowedClients` to convert the integrity hash hex string to bytes.
*   **`shared_IO_Networking_Utils/IsInSameSubnet`**: Called by `GetAddressForClient` to perform the subnet check.
*   **`IpEndpoint/IpEndpoint#2`**: Called by `UpdateRealm` to construct endpoint objects from IP and port.

### Collaboration with Database and Logging

*   **`Database/Query`**: Called by `UpdateRealms` and `LoadAllowedClients` to fetch data.
*   **`Field/Get...`**: Various getters used to extract typed data from query results.
*   **`Log.Main/Out`**: Used extensively for logging errors (failed DNS, invalid masks) and informational messages (realm added, list updated).

## Data Model

The unit interacts with two database tables:

### `realmlist`
Used to store the configuration of each game realm.
*   **Columns Accessed:** `id`, `name`, `address`, `localAddress`, `localSubnetMask`, `port`, `icon`, `realmflags`, `timezone`, `allowedSecurityLevel`, `population`, `realmbuilds`.
*   **Usage:** `UpdateRealms` selects rows where `(realmflags & 1) = 0` (not offline). The `address` and `localAddress` are resolved to IPs. `localSubnetMask` is parsed to CIDR. `realmbuilds` is split into individual build numbers.

### `allowed_clients`
Used to define which client builds are permitted to connect.
*   **Columns Accessed:** `major_version`, `minor_version`, `bugfix_version`, `hotfix_version`, `build`, `os`, `platform`, `integrity_hash`.
*   **Usage:** `LoadAllowedClients` reads all rows to populate `ExpectedRealmdClientBuilds`. The `integrity_hash` is converted from hex to binary.

## Notable Implementation Details

### Subnet Mask Parsing and Validation
`UpdateRealms` contains custom logic to parse `localSubnetMask` from dotted-decimal format (e.g., "255.255.255.0") to a CIDR integer (e.g., 24).
1.  It uses `IpAddress::TryParseFromString` to get an IP object.
2.  It extracts the raw 32-bit integer representation.
3.  It validates the mask using a bitwise trick: `((~mask) & ((~mask) + 1)) != 0`. This checks if the inverted mask is a power of two minus one, ensuring the mask is contiguous (no holes like "255.0.255.0").
4.  It counts the set bits to determine the CIDR length.
*Gotcha:* The code comment notes that the database *should* store numeric subnet masks, but currently stores strings. This parsing step is fragile and adds overhead.

### Realm Flag Filtering
`UpdateRealms` enforces strict flag validation. Any bits in `realmflags` other than `OFFLINE` (0x01), `NEW_PLAYERS` (0x20), `RECOMMENDED` (0x40), or `SPECIFYBUILD` (0x04) are stripped. This prevents unknown or future flags from causing undefined behavior in the client or server logic.

### Build Lookup Logic
`FindBuildInfo(uint16 build)` assumes `ExpectedRealmdClientBuilds` is sorted or at least that the first element is the minimum supported build.
*   If `build >= ExpectedRealmdClientBuilds[0].build`, it returns the first element. This implies that any build *newer* than the oldest supported one is accepted by default, mapping to the oldest supported build's info.
*   For exact matches of older builds, it scans the rest of the vector.
*   *Risk:* If `ExpectedRealmdClientBuilds` is not populated correctly or is empty, accessing `[0]` will crash. The code does not check for emptiness before accessing `[0]`.

### Local vs. External Address Routing
`Realm::GetAddressForClient` implements a simple LAN optimization. If a client connects from an IP within the realm's configured local subnet, the server sends the local IP address instead of the public one. This reduces latency and avoids NAT traversal issues for local players. It only applies to IPv4 clients.

### Periodic Refresh
`UpdateIfNeed` clears the entire `m_realms` map before reloading. This means there is a brief moment where the realm list is empty during a refresh. If `AuthSocket` iterates during this window, it may send an empty list to clients. However, since `UpdateIfNeed` is called from `_HandleRealmList`, which is likely triggered by client requests, the impact might be limited to clients requesting a refresh at the exact moment of update.

## Member Reference

**FindBuildInfo#2**
Linear search through `ExpectedRealmdClientBuilds` for entries matching build, OS, and platform. Returns a vector of pointers to matching `RealmBuildInfo` structs.

**FindBuildInfo**
Looks up a `RealmBuildInfo` by build number. Returns the first entry if the build is newer than the minimum supported build, otherwise searches for an exact match. Returns `nullptr` if not found.

**GetRealmCategoryIdByBuildAndZone**
Determines the realm category icon ID based on the client's build major version and the realm's zone. Uses a static lookup table for legacy versions (major < 4).

**~RealmList**
Trivial destructor.

**begin**
Returns a const iterator to the beginning of the `m_realms` map.

**end**
Returns a const iterator to the end of the `m_realms` map.

**size**
Returns the number of realms in `m_realms`.

**RealmList**
Constructor. Initializes update interval to 0 and next update time to current time.

**Instance**
Returns the global singleton instance of `RealmList`.

**Initialize**
Sets the update interval, loads allowed clients, and performs the initial realm list load from the database.

**UpdateRealm**
Inserts or updates a single realm in `m_realms`. Parses build strings, resolves build info, and constructs IP endpoints.

**UpdateIfNeed**
Checks if the update interval has elapsed. If so, clears `m_realms` and reloads the realm list from the database.

**UpdateRealms**
Queries the `realmlist` table, validates and parses each row (DNS resolution, subnet mask parsing), and calls `UpdateRealm` for each valid realm.

**LoadAllowedClients**
Queries the `allowed_clients` table and populates the global `ExpectedRealmdClientBuilds` vector with `RealmBuildInfo` structs.

**GetAddressForClient**
Method on `Realm`. Returns `localAddress` if the client IP is in the same subnet as the realm's local address (IPv4 only), otherwise returns `externalAddress`.

---

<!-- machine-true, projected from graph.json -->

## Map — RealmList

*Source:* RealmList.cpp, RealmList.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FindBuildInfo#2 | function | — | AuthSocket/VerifyVersion | — |
| FindBuildInfo | function | — | AuthSocket/LoadRealmlistAndWriteIntoBuffer, AuthSocket/_HandleLogonProof__PostRecv | — |
| GetRealmCategoryIdByBuildAndZone | function | — | AuthSocket/LoadRealmlistAndWriteIntoBuffer | — |
| ~RealmList | dtor | — | — | — |
| begin | method | — | AuthSocket/LoadRealmlistAndWriteIntoBuffer | — |
| end | method | — | AuthSocket/LoadRealmlistAndWriteIntoBuffer | — |
| size | method | — | AuthSocket/LoadRealmlistAndWriteIntoBuffer, realmd_Main/main | — |
| RealmList | ctor | — | — | — |
| Instance | method | — | AuthSocket/LoadRealmlistAndWriteIntoBuffer, AuthSocket/_HandleRealmList, realmd_Main/main | — |
| Initialize | method | — | realmd_Main/main | — |
| UpdateRealm | method | IpEndpoint/IpEndpoint#2, shared_Util/StrSplit | — | — |
| UpdateIfNeed | method | — | AuthSocket/_HandleRealmList | — |
| UpdateRealms | method | Database/Query, DNS/ResolveDomainSingle, Field/GetCppString, Field/GetFloat, Field/GetString, Field/GetUInt32, Field/GetUInt8, IpAddress/TryParseFromString, IpAddress/_getInternalIPv4ReprAsUint32, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow | — | realmlist |
| LoadAllowedClients | method | Database/Query, Errors/PrintStacktraceAndThrow, Field/GetCppString, Field/GetUInt16, Field/GetUInt8, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow, shared_Util/HexStrToByteArray | — | allowed_clients |
| GetAddressForClient | method | IpAddress/GetType, shared_IO_Networking_Utils/IsInSameSubnet | AuthSocket/LoadRealmlistAndWriteIntoBuffer | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `allowed_clients`: major_version tinyint(3) unsigned, minor_version tinyint(3) unsigned, bugfix_version tinyint(3) unsigned, hotfix_version char(1), build mediumint(8) unsigned, os char(50), platform char(50), integrity_hash varchar(40)
- `realmlist`: id int(11) unsigned PK, name varchar(32), address varchar(32), localAddress varchar(255), localSubnetMask varchar(255), port int(11), icon tinyint(3) unsigned, realmflags tinyint(3) unsigned, timezone tinyint(3) unsigned, allowedSecurityLevel tinyint(3) unsigned, population float unsigned, gamebuild_min int(11) unsigned, gamebuild_max int(11) unsigned, flag tinyint(3) unsigned, realmbuilds varchar(64)

*`?` = nullable, `PK` = primary key column.*

