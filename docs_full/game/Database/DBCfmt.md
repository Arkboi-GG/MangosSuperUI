<!-- provenance: verbose, failed-members -->
# DBCfmt

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DBCfmt

## Purpose & Responsibilities

`DBCfmt.h` defines a set of global `const char` arrays that specify the binary record layouts for various World of Warcraft client-side DBC (Data Block Chunk) tables. These format strings are consumed by the server's DBC loading infrastructure to correctly parse raw binary data into structured C++ objects. The unit contains no executable logic, classes, or functions; it serves purely as a data registry.

## Data Model

This unit does not interact with any SQL database tables. It defines schemas for client-side binary files (e.g., `ChrClasses.dbc`, `ItemSet.dbc`). There are no SQL queries or schema interactions in this code.

## Notable Implementation Details

### Format String Syntax
The format strings use a character-based notation to describe field types within a DBC row:
- `n`: Primary key/ID field (typically 32-bit integer).
- `i`: 32-bit integer.
- `f`: 32-bit float.
- `d`: 64-bit double.
- `s`: Null-terminated string.
- `x`: Padding or unused bytes (ignored during parsing).

### Client Build Variants
Several format strings are conditional on `SUPPORTED_CLIENT_BUILD`, reflecting changes in DBC binary structures across World of Warcraft patches:

1.  **`ChrClassesEntryfmt`**:
    -   Builds > `CLIENT_BUILD_1_9_4`: Ends with `xxix`.
    -   Builds ≤ `CLIENT_BUILD_1_9_4`: Ends with `xxi`.

2.  **`ItemSetEntryfmt`**:
    -   Builds > `CLIENT_BUILD_1_10_2`: Shorter format.
    -   Builds > `CLIENT_BUILD_1_6_1` and ≤ `1.10.2`: Longer format with extensive padding.
    -   Builds ≤ `CLIENT_BUILD_1_6_1`: Similar to the middle range but ends with fewer integers.

### Unused/Commented Out Entries
-   `ItemDisplayTemplateEntryfmt` and `WorldMapOverlayEntryfmt` are commented out, indicating these tables are either unsupported, handled differently, or deprecated in this server version.

## Cross-Unit Boundaries

This unit does not actively call into other units. It is **referenced by** the DBC loading subsystem elsewhere in the codebase. Other units include this header and use these string constants to configure parsers for specific DBC tables.

## Member Reference

The MAP for this unit contains no functional members (functions/methods). The following entries list the declared data symbols, which are the only "members" of this unit.

**AreaTableEntryfmt**
Format string for the `AreaTable` DBC table. Structure: `niiiixxxxxissssssssxixxxi`.

**AreaTriggerEntryfmt**
Format string for the `AreaTrigger` DBC table. Structure: `niffffffff`.

**AuctionHouseEntryfmt**
Format string for the `AuctionHouse` DBC table. Structure: `niiixxxxxxxxx`.

**BankBagSlotPricesEntryfmt**
Format string for the `BankBagSlotPrices` DBC table. Structure: `ni`.

**CharSectionsEntryfmt**
Format string for the `CharSections` DBC table. Structure: `diiiiixxxi`.

**CharacterFacialHairStylesfmt**
Format string for the `CharacterFacialHairStyles` DBC table. Structure: `iiixxxxxx`.

**ChrClassesEntryfmt**
Format string for the `ChrClasses` DBC table. Structure varies by client build:
-   Post-1.9.4: `nxxixssssssssxxix`
-   Pre-1.9.4: `nxxixssssssssxxi`

**ChrRacesEntryfmt**
Format string for the `ChrRaces` DBC table. Structure: `niixiixxiiiiixixissssssssxxxx`.

**ChatChannelsEntryfmt**
Format string for the `ChatChannels` DBC table. Structure: `nixssssssssxxxxxxxxxx`. Note: Index is not used for more compact storage.

**CinematicSequencesEntryfmt**
Format string for the `CinematicSequences` DBC table. Structure: `nxxxxxxxxx`.

**CreatureDisplayInfofmt**
Format string for the `CreatureDisplayInfo` DBC table. Structure: `nixifxxxxxxx`.

**CreatureDisplayInfoExtrafmt**
Format string for the `CreatureDisplayInfoExtra` DBC table. Structure: `nixxxxxxxxxxxxxxxxx`.

**CreatureModelDatafmt**
Format string for the `CreatureModelData` DBC table. Structure: `nisxfxxxxxxxxxxf`.

**CreatureFamilyfmt**
Format string for the `CreatureFamily` DBC table. Structure: `nfifiiiissssssssxx`.

**CreatureSpellDatafmt**
Format string for the `CreatureSpellData` DBC table. Structure: `niiiixxxx`.

**CreatureTypefmt**
Format string for the `CreatureType` DBC table. Structure: `nxxxxxxxxxx`.

**DurabilityCostsfmt**
Format string for the `DurabilityCosts` DBC table. Structure: `niiiiiiiiiiiiiiiiiiiiiiiiiiiii`.

**DurabilityQualityfmt**
Format string for the `DurabilityQuality` DBC table. Structure: `nf`.

**EmotesEntryfmt**
Format string for the `Emotes` DBC table. Structure: `nsxiiix`.

**EmotesTextEntryfmt**
Format string for the `EmotesText` DBC table. Structure: `nxixxxxxxxxxxxxxxxx`.

**GameObjectDisplayInfofmt**
Format string for the `GameObjectDisplayInfo` DBC table. Structure: `nsxxxxxxxxxx`.

**ItemBagFamilyfmt**
Format string for the `ItemBagFamily` DBC table. Structure: `nxxxxxxxxx`.

**ItemRandomPropertiesfmt**
Format string for the `ItemRandomProperties` DBC table. Structure: `nsiiixxssssssssx`.

**ItemSetEntryfmt**
Format string for the `ItemSet` DBC table. Structure varies by client build:
-   Post-1.10.2: `dssssssssxxxxxxxxxxxxxxxxxxiiiiiiiiiiiiiiiiii`
-   Between 1.6.1 and 1.10.2: `dssssssssxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxiiiiiiiiiiiiiiiiii`
-   Pre-1.6.1: `dssssssssxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxiiiiiiiiiiiiiiii`

**LiquidTypefmt**
Format string for the `LiquidType` DBC table. Structure: `niii`.

**LockEntryfmt**
Format string for the `Lock` DBC table. Structure: `niiiiiiiiiiiiiiiiiiiiiiiixxxxxxxx`.

**MailTemplateEntryfmt**
Format string for the `MailTemplate` DBC table. Structure: `nxxxxxxxxx`.

**MapEntryfmt**
Format string for the `Map` DBC table. Structure: `nxixssssssssxxxxxxxixxxxxxxxxxxxxxxxxxixxx`.

**NamesProfanityEntryfmt**
Format string for the `NamesProfanity` DBC table. Structure: `ds`.

**NamesReservedEntryfmt**
Format string for the `NamesReserved` DBC table. Structure: `ds`.

**QuestSortEntryfmt**
Format string for the `QuestSort` DBC table. Structure: `nxxxxxxxxx`.

**SkillLinefmt**
Format string for the `SkillLine` DBC table. Structure: `nixssssssssxxxxxxxxxxi`.

**SkillLineAbilityfmt**
Format string for the `SkillLineAbility` DBC table. Structure: `niiiixxiiiiixxi`.

**SkillRaceClassInfofmt**
Format string for the `SkillRaceClassInfo` DBC table. Structure: `diiiiiix`.

**SkillTiersfmt**
Format string for the `SkillTiers` DBC table. Structure: `niiiiiiiiiiiiiiiiiiiiiiiiiiiiiiii`.

**SpellCategoryfmt**
Format string for the `SpellCategory` DBC table. Structure: `ni`.

**SpellCastTimefmt**
Format string for the `SpellCastTime` DBC table. Structure: `niii`.

**SpellDurationfmt**
Format string for the `SpellDuration` DBC table. Structure: `niii`.

**SpellFocusObjectfmt**
Format string for the `SpellFocusObject` DBC table. Structure: `nxxxxxxxxx`.

**SpellItemEnchantmentfmt**
Format string for the `SpellItemEnchantment` DBC table. Structure: `niiiiiixxxiiissssssssxii`.

**SpellRadiusfmt**
Format string for the `SpellRadius` DBC table. Structure: `nfxx`.

**SpellRangefmt**
Format string for the `SpellRange` DBC table. Structure: `nffxxxxxxxxxxxxxxxxxxx`.

**SpellShapeshiftfmt**
Format string for the `SpellShapeshift` DBC table. Structure: `nxssssssssxiix`.

**SpellVisualfmt**
Format string for the `SpellVisual` DBC table. Structure: `niiiiiiiiiiiiiii`.

**StableSlotPricesfmt**
Format string for the `StableSlotPrices` DBC table. Structure: `ni`.

**TalentEntryfmt**
Format string for the `Talent` DBC table. Structure: `niiiiiiiixxxxixxixxxi`.

**TalentTabEntryfmt**
Format string for the `TalentTab` DBC table. Structure: `nxxxxxxxxxxxiix`.

**TaxiNodesEntryfmt**
Format string for the `TaxiNodes` DBC table. Structure: `nifffssssssssxii`.

**TaxiPathEntryfmt**
Format string for the `TaxiPath` DBC table. Structure: `niii`.

**TaxiPathNodeEntryfmt**
Format string for the `TaxiPathNode` DBC table. Structure: `diiifffii`.

**WMOAreaTableEntryfmt**
Format string for the `WMOAreaTable` DBC table. Structure: `niiixxxxxiixxxxxxxxx`.

**WorldMapAreaEntryfmt**
Format string for the `WorldMapArea` DBC table. Structure: `xinxffff`.

**TransportAnimationfmt**
Format string for the `TransportAnimation` DBC table. Structure: `diifffx`.

**WorldSafeLocsEntryfmt**
Format string for the `WorldSafeLocs` DBC table. Structure: `nifffxxxxxxxxx`.

---

<!-- machine-true, projected from graph.json -->

## Map — DBCfmt

*Source:* DBCfmt.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|

---

<!-- verify: failed-members | invented: AreaTableEntryfmt, AreaTriggerEntryfmt, AuctionHouseEntryfmt, BankBagSlotPricesEntryfmt, CharacterFacialHairStylesfmt, CharSectionsEntryfmt, ChatChannelsEntryfmt, ChrClassesEntryfmt, ChrRacesEntryfmt, CinematicSequencesEntryfmt, CreatureDisplayInfoExtrafmt, CreatureDisplayInfofmt, CreatureFamilyfmt, CreatureModelDatafmt, CreatureSpellDatafmt, CreatureTypefmt, DurabilityCostsfmt, DurabilityQualityfmt, EmotesEntryfmt, EmotesTextEntryfmt, GameObjectDisplayInfofmt, ItemBagFamilyfmt, ItemRandomPropertiesfmt, ItemSetEntryfmt, LiquidTypefmt, LockEntryfmt, MailTemplateEntryfmt, MapEntryfmt, NamesProfanityEntryfmt, NamesReservedEntryfmt, QuestSortEntryfmt, SkillLineAbilityfmt, SkillLinefmt, SkillRaceClassInfofmt, SkillTiersfmt, SpellCastTimefmt, SpellCategoryfmt, SpellDurationfmt, SpellFocusObjectfmt, SpellItemEnchantmentfmt, SpellRadiusfmt, SpellRangefmt, SpellShapeshiftfmt, SpellVisualfmt, StableSlotPricesfmt, TalentEntryfmt, TalentTabEntryfmt, TaxiNodesEntryfmt, TaxiPathEntryfmt, TaxiPathNodeEntryfmt, TransportAnimationfmt, WMOAreaTableEntryfmt, WorldMapAreaEntryfmt, WorldSafeLocsEntryfmt -->
