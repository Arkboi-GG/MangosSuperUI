# DBCStores

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DBCStores

## Purpose & Responsibilities

`DBCStores` is the central subsystem responsible for loading, validating, and providing access to **Data Block Chunk (DBC)** files. In the context of this server emulator, DBC files are binary data files provided by the client game that define static game content such as item statistics, spell definitions, creature models, talent trees, taxi paths, and localization strings.

This unit performs three primary functions:
1.  **Initialization:** At server startup, `LoadDBCStores` reads dozens of `.dbc` files from the filesystem, validates their structure against expected C++ memory layouts, and populates global `DBCStorage` containers. It also builds secondary lookup indices (e.g., for talents, taxi paths, and character appearance) to optimize runtime queries.
2.  **Runtime Access:** It provides a suite of accessor functions (`Get...`) that allow other subsystems (Player, ObjectMgr, SpellMgr, etc.) to retrieve specific data points from these stores efficiently.
3.  **Validation & Utilities:** It implements logic for validating player/pet names against profanity/reserved lists, converting between world coordinates and map UI coordinates, and checking client build compatibility.

The unit does not interact with SQL database tables; all data originates from the local filesystem's DBC directory.

## Member-by-Member Behavior

### Initialization and Loading

**`LoadDBCStores`** is the entry point for initializing all DBC data. It takes a `dataPath` string pointing to the root data directory.
1.  It iterates through a fixed list of ~45 DBC files.
2.  For each file, it calls the internal template helper `LoadDBC` (not exposed in the MAP but critical to behavior). This helper:
    *   Verifies that the record size defined in the format string matches the size of the corresponding C++ struct. If they mismatch, it logs an error via `Log.Main/Out` and fails the load.
    *   Loads the main English/neutral data.
    *   Attempts to load localized string variants (e.g., French, German) if available in subdirectories.
3.  After loading specific DBCs, it performs post-processing to build optimized lookup structures:
    *   **Character Appearance:** Builds `sCharFacialHairMap` and `sCharSectionMap` by iterating `sCharacterFacialHairStylesStore` and `sCharSectionsStore`. These maps key on composite integers derived from Race, Gender, and Variation IDs to allow O(1) lookups during character creation/validation.
    *   **Name Validation:** Reads `NamesProfanity.dbc` and `NamesReserved.dbc`. It converts the names to wide strings, replaces Emacs-style regex anchors (`\<`, `\>`) with standard C++ regex anchors (`^`, `$`), and compiles them into `std::wregex` objects stored in `NamesProfaneValidators` and `NamesReservedValidators`.
    *   **Spell Categories:** Iterates all spells via `SpellMgr/GetSpellEntry` to populate `sSpellCategoriesStore`, grouping spells by their category ID.
    *   **Pet Family Spells:** Iterates `SkillLineAbility` entries via `ObjectMgr/GetSkillLineAbility`. It identifies passive skills learned on getting a race/class skill and associates them with `CreatureFamily` entries, populating `sPetFamilySpellsStore`.
    *   **Talent Spells:** Iterates `sTalentStore` to build `sTalentSpellPosMap`, mapping Spell IDs to their rank position within a talent tree.
    *   **Taxi Paths:**
        *   Builds `sTaxiPathSetBySource` from `sTaxiPathStore`, mapping `(from_node, to_node)` pairs to path IDs and prices.
        *   Builds `sTaxiPathNodesByPath` from `sTaxiPathNodeStore`. Since nodes are not contiguous in the DBC, it first calculates the maximum index for each path to resize vectors, then fills them with pointers to the original DBC entries.
        *   Calculates `sTaxiNodesMask`, a bitmask identifying which taxi nodes are part of the "scripted" network (i.e., have at least one path that is *not* purely spell-based). It determines spell-based paths by scanning all spells for `SPELL_EFFECT_SEND_TAXI` effects via `SpellMgr/GetSpellEntry`.
    *   **WMO Area Table:** Builds `sWMOAreaInfoByTripple` from `sWMOAreaTableStore`, allowing lookup by `(rootId, adtId, groupId)`.
    *   **Skill Race Class Info:** Builds `SkillRaceClassInfoBySkill` from `sSkillRaceClassInfoStore`, filtering out entries where the associated `SkillLine` doesn't exist.
4.  **Error Handling:** If any DBC files failed to load (either missing or incompatible format), it logs the errors via `Log.Main/Out` and `Log.Main/WaitBeforeContinueIfNeed`, then terminates the process with `exit(1)`.

**`IsAcceptableClientBuild`** checks if a given client build number matches any of the builds defined in the `EXPECTED_MANGOSD_CLIENT_BUILD` macro array. It returns `true` if a match is found.

**`AcceptableClientBuildsListStr`** generates a space-separated string of all acceptable client build numbers for logging purposes.

### Character Appearance and Validation

**`GetCharFacialHairEntry`** retrieves a `CharacterFacialHairStylesEntry` for a specific race, gender, and facial hair ID. It constructs a composite key (`race | (gender << 8) | (facialHairId << 16)`) and looks it up in the pre-built `sCharFacialHairMap`.

**`GetCharSectionEntry`** retrieves a `CharSectionsEntry` for a specific race, section type (hair/beard/etc.), gender, variation, and color. It uses `sCharSectionMap.equal_range` to find all entries matching the race/gender/type prefix, then iterates to find one matching the specific variation/color that is not flagged as unavailable.

**`GetAllValidCharSectionVariationAndColorPairs`** populates a vector with all valid `(variation, color)` pairs for a given race, section type, and gender. It iterates the relevant range in `sCharSectionMap` and filters out unavailable sections. This is used for random appearance generation.

**`ValidateName`** checks a wide-string name against the compiled profanity and reserved word regexes. It returns `CHAR_NAME_PROFANE` if a profane match is found, `CHAR_NAME_RESERVED` if a reserved word matches, or `CHAR_NAME_SUCCESS` otherwise.

### Game Data Accessors

**`GetPetName`** returns the localized name of a pet family given its ID and language index. It looks up the `CreatureFamilyEntry` in `sCreatureFamilyStore`.

**`GetTalentSpellPos`** returns a pointer to a `TalentSpellPos` struct indicating which talent and rank a specific spell ID corresponds to. It looks up the spell ID in `sTalentSpellPosMap`.

**`GetTalentSpellCost`** (two overloads):
1.  Takes a `TalentSpellPos` pointer and returns `rank + 1` (the talent point cost).
2.  Takes a `spellId`, calls `GetTalentSpellPos` internally, and returns the cost. Returns 0 if the spell is not a talent.

**`GetWMOAreaTableEntryByTripple`** looks up a `WMOAreaTableEntry` using a triple of `(rootId, adtId, groupId)`. It uses the pre-built `sWMOAreaInfoByTripple` map.

**`GetChannelEntryFor`** (two overloads):
1.  By `channel_id`: Linearly scans `sChatChannelsStore` to find an entry with a matching `ChannelID`.
2.  By `name`: Linearly scans `sChatChannelsStore`. For each entry, it iterates through its pattern strings, removes `%s` placeholders, and checks if the input name contains the resulting pattern. This supports wildcard-like channel matching.

**`Zone2MapCoordinates`** and **`Map2ZoneCoordinates`**:
These functions convert between 3D world coordinates (or normalized zone coordinates) and 2D map UI coordinates.
*   `Zone2MapCoordinates`: Takes normalized x/y (0-100) and a zone ID. Looks up the `WorldMapAreaEntry` for the zone. Swaps x/y (due to client coordinate system differences), scales them by the zone's bounding box dimensions, and offsets by the zone's origin. Returns `false` if the zone is not found or has zero area.
*   `Map2ZoneCoordinates`: The inverse operation. Takes world/map x/y and a zone ID. Reverses the scaling and offsetting, swaps x/y back, and returns `false` if invalid.

**`GetSkillRaceClassInfo`** retrieves `SkillRaceClassInfoEntry` for a specific skill, race, and class. It uses `SkillRaceClassInfoBySkill.equal_range` to find candidates for the skill, then filters by checking if the race and class bits are set in the entry's masks.

**`GetUnitRaceName`** and **`GetUnitClassName`**: Simple lookups in `sChrRacesStore` and `sChrClassesStore` respectively, returning the localized name string for the given locale index.

### Internal Helpers

**`WMOAreaTableTripple`** (ctor) and **`operator<`**:
A helper struct used as a key in `sWMOAreaInfoByTripple`.
*   The constructor initializes `groupId`, `rootId`, and `adtId`. Note the parameter order in the constructor (`r, a, g`) maps to `rootId`, `adtId`, `groupId` in the member variables, but the member declaration order is `groupId`, `rootId`, `adtId`.
*   `operator<` uses `memcmp` to compare the raw bytes of the struct. The comment notes this is ordered by "entropy" to minimize comparison time, implying the most variable fields are placed first in memory or compared first. However, since `groupId` is declared first, it is compared first.

**`LoadDBC_assert_print`**: A static helper that logs an error message regarding DBC format size mismatches and returns `false`. It is called by the `LoadDBC` template when an assertion failure condition is detected.

## Cross-Unit Boundaries

*   **`LoadDBCStores`**:
    *   **Calls `Errors/PrintStacktraceAndThrow`**: Implicitly via `MANGOS_ASSERT` if a format size mismatch occurs (though the code shows it calls `LoadDBC_assert_print` which logs and returns false, the assert might trigger depending on build config).
    *   **Calls `Log.Main/Out`**: To log initialization progress, errors, and final status.
    *   **Calls `Log.Main/WaitBeforeContinueIfNeed`**: Before exiting on fatal DBC errors, allowing the user to see the error message.
    *   **Calls `ObjectMgr/GetMaxSkillLineAbilityId`, `ObjectMgr/GetSkillLineAbility`, `ObjectMgr/GetMaxTaxiNodeId`, `ObjectMgr/GetTaxiNodeEntry`**: To iterate over skill line abilities and taxi nodes to build secondary indices (`sPetFamilySpellsStore`, `sTaxiNodesMask`).
    *   **Calls `SpellMgr/GetMaxSpellId`, `SpellMgr/GetSpellEntry`, `SpellMgr/Instance`**: To iterate over spells for building `sSpellCategoriesStore` and identifying spell-based taxi paths.
    *   **Calls `shared_Util/Utf8toWStr`**: To convert DBC name strings to wide strings for regex compilation.
    *   **Called by `World/SetInitialWorldSettings`**: The server world object triggers DBC loading during its startup sequence.

*   **`IsAcceptableClientBuild`**:
    *   **Called by `WorldSocket/_HandleAuthSession`**: During authentication, the server checks if the connecting client's build version is supported.

*   **`AcceptableClientBuildsListStr`**:
    *   **Called by `Master/Run`**: Likely used in startup banners or configuration validation to display supported versions.

*   **`GetPetName`**:
    *   **Called by `ObjectMgr/GeneratePetName`**: Used when generating default names for pets.

*   **`GetTalentSpellPos`**:
    *   **Called by `ObjectMgr/ApplyPremadeSpecTemplateToPlayer`**: To identify talents in premade specs.
    *   **Called by `PartyBotAI/CloneFromPlayer`**: To clone talent states.
    *   **Called by `Player.Main/AddSpell`, `Player.Main/RemoveSpell`**: To manage talent-related spells.
    *   **Called by `SpellMgr/LoadSpellChains`**: To resolve spell chain dependencies.

*   **`GetTalentSpellCost`**:
    *   **Called by `Player.Main/AddSpell`, `Player.Main/RemoveSpell`**: To calculate talent point costs.
    *   **Called by various `ChatHandler` commands**: (`HandleLearnAllCommand`, `HandleListTalentsCommand`, etc.) for displaying talent information.
    *   **Called by `ObjectMgr/LoadQuests`, `ObjectMgr/LoadTrainers#2`**: To validate trainer/quest spell costs.
    *   **Called by `SpellEntry/GetErrorAtShapeshiftedCast`, `SpellMgr/LoadSpellLearnSpells`**: For spell validation and learning logic.

*   **`GetWMOAreaTableEntryByTripple`**:
    *   **Called by `GridMap/GetAreaFlag`**: To determine area flags based on WMO geometry.

*   **`GetCharFacialHairEntry`, `GetCharSectionEntry`, `GetAllValidCharSectionVariationAndColorPairs`**:
    *   **Called by `Player.Main/ValidateAppearance`**: To ensure selected appearance options are valid for the chosen race/gender.
    *   **Called by `Player.Main/SelectRandomAppearance`**: To pick random valid appearances.

*   **`GetChannelEntryFor`**:
    *   **Called by `ChannelMgr/GetJoinChannel`, `game_Chat_Channel/Channel`**: To resolve channel metadata by ID or name pattern.

*   **`Zone2MapCoordinates`, `Map2ZoneCoordinates`**:
    *   **Called by `ChatHandler.TeleportCommands/HandleGoZoneXYCommand`**: To convert command arguments to world coordinates.
    *   **Called by `ChatHandler.UnitCommands/HandleGPSCommand`**: To convert world coordinates to map coordinates for display.

*   **`GetSkillRaceClassInfo`**:
    *   **Called by `Player.Main/UpdateSkillsForLevel`, `Player.Main/UpdateSpellTrainedSkills`, `Player.Main/_LoadSkills`**: To determine starting skill levels and trained skills based on race/class.
    *   **Called by `WorldSession.SkillHandler/HandleUnlearnSkillOpcode`**: To validate unlearning requests.

*   **`GetUnitRaceName`, `GetUnitClassName`**:
    *   **Called by `AsyncCommandHandlers/HandleResponse`**: To provide human-readable race/class names in async command responses.

*   **`ValidateName`**:
    *   **Called by `ObjectMgr/CheckPetName`, `ObjectMgr/CheckPlayerName`**: To enforce naming rules.

## Data Model

This unit does not interact with SQL database tables. All data is sourced from binary `.dbc` files located in the `dbc/` subdirectory of the specified `dataPath`. The `DBCStorage` templates manage the in-memory representation of this data.

## Notable Implementation Details

1.  **Linear Scans for Channels:** `GetChannelEntryFor` performs a linear scan of `sChatChannelsStore` for both ID and name lookups. Given that chat channels are relatively few, this is likely acceptable, but it is less efficient than hash-map lookups used elsewhere.
2.  **Regex Compilation at Startup:** `LoadDBCStores` compiles `std::wregex` objects for every entry in `NamesProfanity.dbc` and `NamesReserved.dbc`. This is computationally expensive but done once at startup. The conversion from Emacs-style anchors (`\<`, `\>`) to POSIX/C++ anchors (`^`, `$`) is a specific adaptation for the C++ regex engine.
3.  **Taxi Path Mask Calculation:** The calculation of `sTaxiNodesMask` is complex. It iterates all spells to find those with `SPELL_EFFECT_SEND_TAXI`, collecting their target path IDs. Then, for each taxi node, it checks if *any* outgoing path is *not* in this spell-based set. If so, the node is marked as part of the scripted network. This distinction likely affects how the server handles taxi flight logic (e.g., scripted flights vs. instant spell transports).
4.  **Coordinate Swapping:** `Zone2MapCoordinates` and `Map2ZoneCoordinates` explicitly swap X and Y coordinates. The comments indicate this is due to the client's map coordinate system being swapped relative to the world coordinate system. Failure to swap would result in incorrect positioning on the minimap/world map.
5.  **Memory Layout Optimization:** The `WMOAreaTableTripple` struct uses `memcmp` for comparison. The member order (`groupId`, `rootId`, `adtId`) is chosen to optimize this comparison, presumably because `groupId` varies more frequently or is more significant for sorting.
6.  **Hardcoded File Count:** `LoadDBCStores` uses a hardcoded constant `DBCFilesCount = 45`. If the number of loaded DBCs changes, this constant must be updated manually, otherwise the progress bar and error checking logic will be inaccurate.
7.  **Conditional Compilation:** `ItemBagFamily.dbc` is only loaded if `SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_8_4`. This indicates backward compatibility handling for older client versions.

## Member Reference

**WMOAreaTableTripple** (ctor): Initializes the `groupId`, `rootId`, and `adtId` members from the constructor arguments.

**operator<**: Compares two `WMOAreaTableTripple` instances using `memcmp` on their raw memory layout to determine ordering.

**IsAcceptableClientBuild**: Checks if the provided `build` number exists in the `EXPECTED_MANGOSD_CLIENT_BUILD` array.

**AcceptableClientBuildsListStr**: Generates a space-separated string of all acceptable client build numbers from `EXPECTED_MANGOSD_CLIENT_BUILD`.

**LoadDBC_assert_print**: Logs an error message indicating a mismatch between the DBC format record size and the C++ struct size, then returns `false`.

**LoadDBCStores**: Loads all DBC files from the specified `dataPath`, builds secondary lookup indices (appearance, talents, taxi, skills, name validation), and terminates the server if any required DBCs are missing or incompatible.

**GetPetName**: Retrieves the localized name for a pet family ID from `sCreatureFamilyStore`.

**GetTalentSpellPos**: Looks up a spell ID in `sTalentSpellPosMap` to return its talent rank position.

**GetTalentSpellCost**: Returns the talent point cost (`rank + 1`) for a given `TalentSpellPos` or spell ID.

**GetTalentSpellCost#2**: Overload that takes a `spellId`, resolves its `TalentSpellPos`, and returns the cost.

**GetWMOAreaTableEntryByTripple**: Looks up a `WMOAreaTableEntry` in `sWMOAreaInfoByTripple` using `(rootId, adtId, groupId)`.

**GetCharFacialHairEntry**: Looks up a `CharacterFacialHairStylesEntry` in `sCharFacialHairMap` using a composite key of race, gender, and facial hair ID.

**GetCharSectionEntry**: Finds a `CharSectionsEntry` in `sCharSectionMap` matching race, gender, type, variation, and color, excluding unavailable sections.

**GetAllValidCharSectionVariationAndColorPairs**: Populates a vector with all valid variation/color pairs for a given race, section type, and gender from `sCharSectionMap`.

**GetChannelEntryFor#2**: Linearly scans `sChatChannelsStore` to find an entry with a matching `ChannelID`.

**GetChannelEntryFor**: Linearly scans `sChatChannelsStore` to find an entry whose pattern (with `%s` removed) is contained within the provided name.

**Zone2MapCoordinates**: Converts normalized zone coordinates to map UI coordinates using `sWorldMapAreaStore`, swapping axes and applying scaling/offset.

**Map2ZoneCoordinates**: Converts map UI coordinates to normalized zone coordinates using `sWorldMapAreaStore`, reversing the transformation.

**GetSkillRaceClassInfo**: Finds a `SkillRaceClassInfoEntry` in `SkillRaceClassInfoBySkill` matching the skill, race, and class masks.

**GetUnitRaceName**: Retrieves the localized race name from `sChrRacesStore`.

**GetUnitClassName**: Retrieves the localized class name from `sChrClassesStore`.

**ValidateName**: Checks a name against compiled profanity and reserved word regexes, returning a status code.

---

<!-- machine-true, projected from graph.json -->

## Map — DBCStores

*Source:* DBCStores.cpp, DBCStores.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WMOAreaTableTripple | ctor | — | — | — |
| operator< | method | — | — | — |
| IsAcceptableClientBuild | function | — | WorldSocket/_HandleAuthSession | — |
| AcceptableClientBuildsListStr | function | — | Master/Run | — |
| LoadDBC_assert_print | function | Log.Main/Out | — | — |
| LoadDBCStores | function | Errors/PrintStacktraceAndThrow, Log.Main/Out, Log.Main/WaitBeforeContinueIfNeed, ObjectMgr/GetMaxSkillLineAbilityId, ObjectMgr/GetMaxTaxiNodeId, ObjectMgr/GetSkillLineAbility, ObjectMgr/GetTaxiNodeEntry, ProgressBar/BarGoLink#2, shared_Util/Utf8toWStr, SpellMgr/GetMaxSpellId, SpellMgr/GetSpellEntry, SpellMgr/Instance, TalentSpellPos/TalentSpellPos#2, TaxiPathBySourceAndDestination/TaxiPathBySourceAndDestination#2, TaxiPathNodePtr/TaxiPathNodePtr#2 | World/SetInitialWorldSettings | — |
| GetPetName | function | — | ObjectMgr/GeneratePetName | — |
| GetTalentSpellPos | function | — | ObjectMgr/ApplyPremadeSpecTemplateToPlayer, PartyBotAI/CloneFromPlayer, Player.Main/AddSpell, Player.Main/RemoveSpell, SpellMgr/LoadSpellChains | — |
| GetTalentSpellCost | function | — | Player.Main/AddSpell, Player.Main/RemoveSpell | — |
| GetTalentSpellCost#2 | function | — | ChatHandler.CharacterCommands/HandleLearnAllCommand, ChatHandler.CharacterCommands/HandleLearnAllMySpellsCommand, ChatHandler.CharacterCommands/HandleListTalentsCommand, ChatHandler.LookupCommands/ShowSpellListHelper, ChatHandler.UnitCommands/HandleListAurasCommand, ObjectMgr/LoadQuests, ObjectMgr/LoadTrainers#2, SpellEntry/GetErrorAtShapeshiftedCast, SpellMgr/LoadSpellLearnSpells | — |
| GetWMOAreaTableEntryByTripple | function | — | GridMap/GetAreaFlag | — |
| GetCharFacialHairEntry | function | — | Player.Main/ValidateAppearance | — |
| GetCharSectionEntry | function | CharSectionsEntry/HasFlag | Player.Main/ValidateAppearance | — |
| GetAllValidCharSectionVariationAndColorPairs | function | CharSectionsEntry/HasFlag | Player.Main/SelectRandomAppearance | — |
| GetChannelEntryFor#2 | function | — | — | — |
| GetChannelEntryFor | function | — | ChannelMgr/GetJoinChannel, game_Chat_Channel/Channel | — |
| Zone2MapCoordinates | function | — | ChatHandler.TeleportCommands/HandleGoZoneXYCommand | — |
| Map2ZoneCoordinates | function | — | ChatHandler.UnitCommands/HandleGPSCommand | — |
| GetSkillRaceClassInfo | function | — | Player.Main/UpdateSkillsForLevel, Player.Main/UpdateSpellTrainedSkills, Player.Main/_LoadSkills, WorldSession.SkillHandler/HandleUnlearnSkillOpcode | — |
| GetUnitRaceName | function | — | AsyncCommandHandlers/HandleResponse | — |
| GetUnitClassName | function | — | AsyncCommandHandlers/HandleResponse | — |
| ValidateName | function | — | ObjectMgr/CheckPetName, ObjectMgr/CheckPlayerName | — |
