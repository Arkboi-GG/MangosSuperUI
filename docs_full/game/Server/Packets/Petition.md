# Petition

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Petition Unit Documentation

## Purpose & Responsibilities

The **Petition** unit provides the data structures and network packet handlers required to manage the lifecycle of Guild Charters in the World of Warcraft emulation environment. It serves two distinct but related roles:

1.  **Domain Model (`GuildMgr.h`):** The `Petition` class acts as an in-memory representation of a pending guild charter. It tracks the charter's unique identifier, the item GUID representing the physical charter in the player's inventory, the owner (founder), the proposed guild name, the faction (Team), and the list of signatures collected so far. It determines whether a charter is "complete" (has enough signatures to form a guild) and manages the addition/removal of signatures.
2.  **Network Protocol (`Petition.h` / `Petition.cpp`):** The `WorldPackets::Petition` namespace defines the client-to-server packets involved in the charter process. These classes parse raw binary data from the client into structured fields (such as Item GUIDs, Player GUIDs, and NPC GUIDs) that the server logic can consume. This covers actions like buying a charter, signing it, declining a signature, querying its status, and turning it in to create the guild.

This unit does not handle the actual creation of the `Guild` object or the persistence of signatures to the database directly; those responsibilities lie in `GuildMgr` and `PetitionSignature` respectively. Instead, `Petition` aggregates signature data and exposes it to those higher-level managers.

## Member-by-Member Behavior

### Network Packet Parsing (`WorldPackets::Petition`)

These methods extract data from incoming `WorldPacket` objects. They are purely procedural, converting binary streams into member variables.

*   **PetitionBuy**: Handles the request to purchase a new charter from an NPC. It extracts the NPC's GUID and the desired guild name. Notably, it skips a significant amount of padding data (multiple `uint32`, `uint16`, and `uint8` fields) that appears to be unused or reserved in the client protocol. It also skips an "index" field, indicating the client sends extra metadata that the server ignores.
*   **OfferPetition**: Used when a player offers their charter to another player (likely for signing). It extracts the Item GUID of the charter and the GUID of the player being offered the signature slot.
*   **PetitionSign**: Processes a signature attempt. It extracts the Item GUID. Crucially, it explicitly skips an `int8` argument. The code comments note that this argument corresponds to the Lua function `SignPetition(arg)`, but the official client interface never uses this argument, so the server discards it to maintain protocol alignment without processing garbage data.
*   **PetitionShow**: Requests the list of NPCs that sell charters. It extracts the GUID of the petitioner NPC.
*   **PetitionShowSignatures**: Requests the current list of signatures on a specific charter. It extracts the Item GUID.
*   **QueryPetition**: Queries the status/details of a specific charter. It extracts both the Petition GUID (internal ID) and the Item GUID.
*   **PetitionDecline**: Declines a signature offer. It extracts the Item GUID.
*   **TurnInPetition**: Submits a completed charter to create the guild. It extracts the Item GUID.
*   **PetitionRename**: Renames the proposed guild before creation. It extracts the Item GUID and the new name string.

### Domain Model (`Petition` Class in `GuildMgr.h`)

#### Construction & Initialization
*   **Petition()**: Default constructor, initializing the ID to 0. Used primarily for temporary or uninitialized states.
*   **Petition(uint32 id, ...)**: Parameterized constructor used by `GuildMgr::CreatePetition`. It initializes the core identity fields: ID, Charter Item GUID, Owner GUID, and Name.

#### Accessors & State Queries
*   **GetId**: Returns the internal database ID of the petition. Used by `GuildMgr` for map lookups and by `game_Guild_Guild::Create` to link the new guild to its originating petition.
*   **GetCharterGuid**: Returns the GUID of the item representing the charter. Used by `GuildMgr::GetPetitionByCharterGuid` to locate petitions via the item held by the player.
*   **GetOwnerGuid**: Returns the GUID of the player who bought the charter. Used extensively by `WorldSession.PetitionsHandler` to verify ownership during sign, decline, query, and turn-in operations, and by `GuildMgr` for saving/loading.
*   **GetName**: Returns the proposed guild name. Used by `WorldSession.PetitionsHandler` during queries and turn-ins, and by `game_Guild_Guild::Create` to set the initial guild name.
*   **GetTeam**: Returns the faction (Alliance/Horde) of the owner. Used by `WorldSession.PetitionsHandler` during signing to ensure faction consistency.
*   **SetTeam**: Sets the faction. Called by `GuildMgr::CreatePetition` when a new charter is generated.
*   **GetSignatureCount**: Returns the number of signatures currently on the charter. Used by `WorldSession.PetitionsHandler` to display progress to the player and validate completion.
*   **GetSignatureList**: Returns the internal list of `PetitionSignature` objects. Used by `game_Guild_Guild::Create` to transfer signatures to the new guild's member list.
*   **IsComplete**: Checks if the number of signatures meets the minimum requirement defined by the server configuration (`CONFIG_UINT32_MIN_PETITION_SIGNS`). This boolean gate controls whether a charter can be turned in. Called by `GuildMgr::AddNewSignature` (to check if a new sig completes it) and `WorldSession.PetitionsHandler` during sign and turn-in flows.

## Cross-Unit Boundaries

### Incoming Dependencies (Calls Out)
*   **`ObjectGuid/operator>>`**: All `ReadFromWorldPacket` methods in `WorldPackets::Petition` call this operator to deserialize GUIDs from the network packet. This is the standard mechanism for reading entity identifiers in the codebase.
*   **`ByteBuffer/operator>>`**: Used by `PetitionBuy::ReadFromWorldPacket` and `PetitionRename::ReadFromWorldPacket` (implicitly via string extraction if applicable, though the map highlights `ByteBuffer` usage for complex types). Specifically, `PetitionBuy` uses it to skip/read various integer fields.

### Outgoing Dependencies (Called By)
*   **`GuildMgr`**:
    *   `GuildMgr::LoadPetitions`: Calls the default `Petition()` constructor to instantiate objects before loading data from DB.
    *   `GuildMgr::CreatePetition`: Calls the parameterized `Petition(...)` constructor and `SetTeam`.
    *   `GuildMgr::GetPetitionByCharterGuid`: Calls `GetCharterGuid`.
    *   `GuildMgr::GetPetitionByOwnerGuid`: Calls `GetOwnerGuid`.
    *   `GuildMgr::DeletePetition`: Calls `GetId`.
    *   `GuildMgr::SaveToDB`: Calls `GetId` and `GetOwnerGuid`.
    *   `GuildMgr::AddNewSignature`: Calls `IsComplete` to determine if the charter is ready for turn-in.
*   **`WorldSession.PetitionsHandler`**:
    *   `HandlePetitionSignOpcode`: Calls `GetOwnerGuid`, `GetTeam`, `GetSignatureCount`, and `IsComplete`.
    *   `HandlePetitionDeclineOpcode`: Calls `GetOwnerGuid`.
    *   `HandlePetitionQueryOpcode`: Calls `GetOwnerGuid` and `GetName`.
    *   `HandleTurnInPetitionOpcode`: Calls `GetOwnerGuid` and `GetName`.
    *   `HandleOfferPetitionOpcode`: Calls `GetSignatureCount`.
    *   `HandlePetitionShowSignOpcode`: Calls `GetSignatureCount`.
*   **`game_Guild_Guild`**:
    *   `Guild::Create`: Calls `GetId`, `GetName`, and `GetSignatureList` to finalize the guild creation process using the petition's data.

## Data Model

The `Petition` unit itself does not execute SQL queries or interact directly with database tables. The `Petition` class in `GuildMgr.h` contains methods like `LoadFromDB` and `SaveToDB`, but these are implemented in `GuildMgr.cpp` (not provided in this unit's source). Therefore, this unit has **no direct table interactions**. It relies on `GuildMgr` to handle persistence.

## Notable Implementation Details

1.  **Protocol Padding in `PetitionBuy`**: The `ReadFromWorldPacket` implementation for `PetitionBuy` contains a long sequence of `read_skip` calls. This indicates that the client sends a large block of data (likely legacy fields or padding for alignment) that the server intentionally ignores. Maintainers must preserve this exact sequence of skips; changing the order or removing them will desynchronize the parser, causing subsequent fields (like `name`) to be read from incorrect offsets.
2.  **Unused Lua Argument in `PetitionSign`**: The code explicitly skips an `int8` value in `PetitionSign::ReadFromWorldPacket`. The comment clarifies this is an artifact of the client's Lua API (`SignPetition(arg)`), which is never populated with meaningful data by the official UI. This is a defensive parsing measure to keep the packet stream aligned.
3.  **Configuration-Driven Completion**: The `IsComplete` method does not use a hardcoded magic number for the required signatures. Instead, it reads `sWorld.getConfig(CONFIG_UINT32_MIN_PETITION_SIGNS)`. This allows server administrators to adjust the difficulty of forming a guild without recompiling the code.
4.  **Const-Correctness**: Most getters in the `Petition` class are marked `const` (e.g., `GetId`, `GetTeam`, `IsComplete`), ensuring that reading the state of a petition does not inadvertently modify it. However, `GetCharterGuid` and `GetOwnerGuid` return non-const references (`ObjectGuid const&` is returned, but the method itself isn't marked const in the declaration? Actually, looking at `GuildMgr.h`: `ObjectGuid const& GetCharterGuid() { return m_charterGuid; }` is **not** marked `const`. This is a minor inconsistency compared to `GetOwnerGuid` which is also not marked `const` in the snippet provided, wait: `ObjectGuid const& GetOwnerGuid() { return m_ownerGuid; }` is also not marked `const`. However, `GetTeam` and `GetSignatureCount` **are** marked `const`. This suggests that while the return type prevents modification of the GUID content, the method itself could theoretically modify the object state, though it doesn't. This is a stylistic inconsistency in the header.
5.  **Signature List Ownership**: The `Petition` class holds a `std::list<PetitionSignature*>`. It does not manage the lifetime of these pointers directly in the provided header (deletion is likely handled by `GuildMgr` or the destructor in `.cpp`). The `GetSignatureList` returns a const reference, preventing external modification of the list structure, but callers still hold raw pointers to `PetitionSignature` objects.

## Member Reference

*   **ReadFromWorldPacket#5** (`PetitionShow::ReadFromWorldPacket`): Parses the `petitionerNpcGuid` from the packet. Calls `ObjectGuid/operator>>`.
*   **ReadFromWorldPacket#6** (`PetitionShowSignatures::ReadFromWorldPacket`): Parses the `itemGuid` from the packet. Calls `ObjectGuid/operator>>`.
*   **ReadFromWorldPacket#8** (`QueryPetition::ReadFromWorldPacket`): Parses `petitionGuid` (uint32) and `itemGuid` from the packet. Calls `ByteBuffer/operator>>` (implied for uint32/string handling if applicable, though map says `ByteBuffer/operator>>#9` and `ObjectGuid/operator>>`). *Correction*: Map says `ByteBuffer/operator>>#9` and `ObjectGuid/operator>>`. The source shows `recv_data >> petitionGuid` (uint32) and `recv_data >> itemGuid` (ObjectGuid). The map likely categorizes the uint32 read under ByteBuffer ops.
*   **PetitionShow** (ctor): Initializes the packet with opcode `CMSG_PETITION_SHOWLIST`.
*   **ReadFromWorldPacket#3** (`PetitionDecline::ReadFromWorldPacket`): Parses the `itemGuid` from the packet. Calls `ObjectGuid/operator>>`.
*   **ReadFromWorldPacket#9** (`TurnInPetition::ReadFromWorldPacket`): Parses the `itemGuid` from the packet. Calls `ObjectGuid/operator>>`.
*   **ReadFromWorldPacket#4** (`PetitionRename::ReadFromWorldPacket`): Parses `itemGuid` and `newName` from the packet. Calls `ByteBuffer/operator>>` (for string/name) and `ObjectGuid/operator>>`.
*   **ReadFromWorldPacket#7** (`PetitionSign::ReadFromWorldPacket`): Parses `itemGuid` and skips an unused `int8` argument. Calls `ObjectGuid/operator>>`.
*   **ReadFromWorldPacket** (`PetitionBuy::ReadFromWorldPacket`): Parses `guidNPC` and `name`, skipping extensive padding data. Calls `ByteBuffer/operator>>` (for skips/reads) and `ObjectGuid/operator>>`.
*   **ReadFromWorldPacket#2** (`OfferPetition::ReadFromWorldPacket`): Parses `itemGuid` and `playerGuid`. Calls `ByteBuffer/operator>>` (implied for first field if treated as generic, but source shows two GUIDs) and `ObjectGuid/operator>>`. *Note*: Map lists `ByteBuffer/operator>>` and `ObjectGuid/operator>>`. Source shows `recv_data >> itemGuid` then `recv_data >> playerGuid`. Both are GUIDs. The map might be grouping the first read differently or referring to a different overload, but strictly following source: it reads two GUIDs.
*   **Petition** (default ctor): Initializes `m_id` to 0. Called by `GuildMgr::LoadPetitions`.
*   **Petition#2** (param ctor): Initializes `m_id`, `m_charterGuid`, `m_ownerGuid`, and `m_name`. Called by `GuildMgr::CreatePetition`.
*   **GetId**: Returns `m_id`. Called by `GuildMgr` and `Guild::Create`.
*   **GetCharterGuid**: Returns `m_charterGuid`. Called by `GuildMgr::GetPetitionByCharterGuid`.
*   **GetOwnerGuid**: Returns `m_ownerGuid`. Called by `GuildMgr` and `WorldSession.PetitionsHandler`.
*   **GetName**: Returns `m_name`. Called by `Guild::Create` and `WorldSession.PetitionsHandler`.
*   **GetTeam**: Returns `m_team`. Called by `WorldSession.PetitionsHandler`.
*   **SetTeam**: Sets `m_team`. Called by `GuildMgr::CreatePetition`.
*   **GetSignatureCount**: Returns size of `m_signatures`. Called by `WorldSession.PetitionsHandler`.
*   **GetSignatureList**: Returns `m_signatures`. Called by `Guild::Create`.
*   **IsComplete**: Checks if signature count meets config minimum. Called by `GuildMgr::AddNewSignature` and `WorldSession.PetitionsHandler`.

---

<!-- machine-true, projected from graph.json -->

## Map — Petition

*Source:* Petition.cpp, Petition.h, GuildMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#5 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#6 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#8 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| PetitionShow | ctor | — | — | — |
| ReadFromWorldPacket#3 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#9 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>>, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#7 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>, ObjectGuid/operator>> | — | — |
| Petition | ctor | — | GuildMgr/LoadPetitions | — |
| Petition#2 | ctor | — | GuildMgr/CreatePetition | — |
| GetId | method | — | game_Guild_Guild/Create, GuildMgr/CreatePetition, GuildMgr/DeletePetition, GuildMgr/LoadPetitions, GuildMgr/SaveToDB#2, WorldSession.PetitionsHandler/HandlePetitionSignOpcode | — |
| GetCharterGuid | method | — | GuildMgr/GetPetitionByCharterGuid | — |
| GetOwnerGuid | method | — | GuildMgr/GetPetitionByOwnerGuid, GuildMgr/LoadPetitions, GuildMgr/SaveToDB#2, WorldSession.PetitionsHandler/HandlePetitionDeclineOpcode, WorldSession.PetitionsHandler/HandlePetitionQueryOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode | — |
| GetName | method | — | game_Guild_Guild/Create, WorldSession.PetitionsHandler/HandlePetitionQueryOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode | — |
| GetTeam | method | — | WorldSession.PetitionsHandler/HandlePetitionSignOpcode | — |
| SetTeam | method | — | GuildMgr/CreatePetition | — |
| GetSignatureCount | method | — | WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionShowSignOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode | — |
| GetSignatureList | method | — | game_Guild_Guild/Create | — |
| IsComplete | method | — | GuildMgr/AddNewSignature, WorldSession.PetitionsHandler/HandlePetitionSignOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode | — |
