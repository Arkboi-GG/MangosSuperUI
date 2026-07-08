<!-- provenance: verbose, failed-members -->
# blackwing_lair

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# blackwing_lair

**Purpose & Responsibilities**

`blackwing_lair.h` is a header-only unit that defines the constant identifiers, enumerations, and data indices required for the Blackwing Lair raid instance scripts. It contains no executable logic, classes, or functions. Its sole responsibility is to provide a centralized registry of numeric constants—such as encounter types, instance data keys, NPC IDs, spell IDs, and quest IDs—that are shared across the multiple `.cpp` files implementing the raid's boss AIs and instance handlers. This prevents the scattering of magic numbers throughout the codebase and ensures consistency in entity identification and state tracking.

**Member-by-Member Behavior**

As a header-only file containing only `enum` definitions, there are no executable members. The "members" are the enumeration values themselves, which serve as static constants. They are categorized by their functional role:

*   **Encounter Types (`TYPE_*`)**: Identify specific bosses or events for the instance script's encounter history.
*   **Instance Data Indices (`DATA_*`)**: Serve as integer keys for the instance script's internal data container, allowing retrieval of dynamic state like boss GUIDs, door states, and event timers.
*   **Entity Identifiers (`NPC_*`, `GO_*`, `SPELL_*`, etc.)**: Define the numeric IDs for game objects, non-player characters, spells, quests, and factions referenced by scripts.

**Cross-Unit Boundaries**

This unit has no direct cross-unit calls because it contains no executable code. It is included by other units (such as the Blackwing Lair instance script and individual boss AI implementations) to access these constants. It does not call out to any other units.

**Data Model**

This unit does not interact directly with any database tables. It defines constants used by scripts that may indirectly query or update database records, but `blackwing_lair.h` itself contains no SQL queries or table references.

**Notable Implementation Details**

*   **Header-Only Design**: The unit is wrapped in `#ifndef DEF_BLACKWING_LAIR` guards and contains only `enum` definitions, making it a lightweight, dependency-free header.
*   **Centralized Constants**: All IDs and indices are defined here to avoid hardcoding values in multiple places.
*   **Parallel GUID Tracking**: The presence of both `DATA_NEFARIUS_GUID` and `DATA_NEFARIAN_GUID` suggests distinct entity representations for Nefarian (e.g., pre-boss vs. boss form).
*   **Hardcoded Health Cap**: `RAZORGORE_MAX_HEALTH_DURING_POSESSION` is a fixed value (450,000), capping Razorgore's health during a specific phase regardless of scaling.

## Member Reference

*   **TYPE_RAZORGORE**: Enum value `0`, identifying the Razorgore the Untamed encounter.
*   **TYPE_VAELASTRASZ**: Enum value `1`, identifying the Vaelastrasz the Corrupt encounter.
*   **TYPE_LASHLAYER**: Enum value `2`, identifying the Lashlayer encounter.
*   **TYPE_FIREMAW**: Enum value `3`, identifying the Firemaw encounter.
*   **TYPE_EBONROC**: Enum value `4`, identifying the Ebonroc encounter.
*   **TYPE_FLAMEGOR**: Enum value `5`, identifying the Flamewaker (Flamegor) encounter.
*   **TYPE_CHROMAGGUS**: Enum value `6`, identifying the Chromaggus encounter.
*   **TYPE_NEFARIAN**: Enum value `7`, identifying the Nefarian encounter.
*   **TYPE_VAEL_EVENT**: Enum value `8`, identifying a specific Vaelastrasz-related event.
*   **TYPE_SCEPTER_RUN**: Enum value `9`, identifying the Scepter Run event.
*   **MAX_ENCOUNTER**: Enum value `10`, defining the upper limit for encounter types.
*   **DATA_RAZORGORE_GUID**: Enum value `0`, index for storing Razorgore's GUID.
*   **DATA_VAELASTRASZ_GUID**: Enum value `1`, index for storing Vaelastrasz's GUID.
*   **DATA_LASHLAYER_GUID**: Enum value `2`, index for storing Lashlayer's GUID.
*   **DATA_FIREMAW_GUID**: Enum value `3`, index for storing Firemaw's GUID.
*   **DATA_EBONROC_GUID**: Enum value `4`, index for storing Ebonroc's GUID.
*   **DATA_FLAMEGOR_GUID**: Enum value `5`, index for storing Flamegor's GUID.
*   **DATA_CHROMAGGUS_GUID**: Enum value `6`, index for storing Chromaggus's GUID.
*   **DATA_NEFARIUS_GUID**: Enum value `7`, index for storing Nefarius/Nefarian's GUID (likely pre-boss form).
*   **DATA_NEFARIAN_GUID**: Enum value `8`, index for storing Nefarian's GUID (likely main boss form).
*   **DATA_GRETOK_GUID**: Enum value `9`, index for storing Grethok the Controller's GUID.
*   **DATA_TRIGGER_GUID**: Enum value `10`, index for storing a generic trigger object's GUID.
*   **DATA_ORB_DOMINATION_GUID**: Enum value `11`, index for storing the Orb of Domination's GUID.
*   **DATA_EGG**: Enum value `12`, index for tracking egg-related state.
*   **DATA_HOW_EGG**: Enum value `13`, index for tracking how the egg was obtained/handled.
*   **DATA_CHROM_BREATH**: Enum value `14`, index for tracking Chromaggus's breath weapon state.
*   **DATA_NEF_COLOR**: Enum value `15`, index for tracking Nefarian's elemental color state.
*   **DATA_DOOR_RAZORGORE_ENTER**: Enum value `16`, index for Razorgore's entrance door state.
*   **DATA_DOOR_RAZORGORE_EXIT**: Enum value `17`, index for Razorgore's exit door state.
*   **DATA_DOOR_VAELASTRASZ**: Enum value `18`, index for Vaelastrasz's door state.
*   **DATA_DOOR_LASHLAYER**: Enum value `19`, index for Lashlayer's door state.
*   **DATA_DOOR_CHROMAGGUS_ENTER**: Enum value `20`, index for Chromaggus's entrance door state.
*   **DATA_DOOR_CHROMAGGUS_EXIT**: Enum value `21`, index for Chromaggus's exit door state.
*   **DATA_DOOR_CHROMAGGUS_SIDE**: Enum value `22`, index for Chromaggus's side door state.
*   **DATA_DOOR_NEFARIAN**: Enum value `23`, index for Nefarian's door state.
*   **DATA_SCEPTER_CHAMPION**: Enum value `24`, index for the Scepter Run champion's identifier.
*   **DATA_SCEPTER_RUN_TIME**: Enum value `25`, index for the Scepter Run timer.
*   **MAX_DATAS**: Enum value `26`, defining the size of the instance data array.
*   **GO_DRAKONID_BONES**: Constant `179804`, Game Object ID for Drakonid Bones.
*   **SPELL_POSSESS**: Constant `19832`, Spell ID for the Possess effect.
*   **SPELL_POSSESS_VISUAL**: Constant `23014`, Spell ID for the Possess visual effect.
*   **NPC_RAZORGORE**: Constant `12435`, NPC ID for Razorgore the Untamed.
*   **NPC_VAELASTRASZ**: Constant `13020`, NPC ID for Vaelastrasz the Corrupt.
*   **NPC_LASHLAYER**: Constant `12017`, NPC ID for Lashlayer.
*   **NPC_FIREMAW**: Constant `11983`, NPC ID for Firemaw.
*   **NPC_EBONROC**: Constant `14601`, NPC ID for Ebonroc.
*   **NPC_FLAMEGOR**: Constant `11981`, NPC ID for Flamewaker.
*   **NPC_CHROMAGGUS**: Constant `14020`, NPC ID for Chromaggus.
*   **NPC_NEFARIAN**: Constant `11583`, NPC ID for Nefarian.
*   **NPC_LORD_NEFARIAN**: Constant `10162`, NPC ID for Lord Nefarian (likely alternate form).
*   **NPC_ORB_OF_DOMINATION**: Constant `14453`, NPC ID for the Orb of Domination.
*   **NPC_GRETHOK_THE_CONTROLLER**: Constant `12557`, NPC ID for Grethok the Controller.
*   **NPC_BLACKWING_GUARDSMAN**: Constant `14456`, NPC ID for Blackwing Guardsman.
*   **NPC_BLACKWING_LEGGIONAIRE**: Constant `12416`, NPC ID for Blackwing Legionnaire.
*   **NPC_BLACKWING_MAGE**: Constant `12420`, NPC ID for Blackwing Mage.
*   **NPC_DEATH_TALON_DRAGONSPAWN**: Constant `12422`, NPC ID for Death Talon Dragonspawn.
*   **NPC_DEATH_TALON_CAPTAIN**: Constant `12467`, NPC ID for Death Talon Captain.
*   **NPC_DEATH_TALON_SEETHER**: Constant `12464`, NPC ID for Death Talon Seether.
*   **NPC_DEATH_TALON_WYRMKIN**: Constant `12465`, NPC ID for Death Talon Wyrmkin.
*   **NPC_DEATH_TALON_FLAMESCALE**: Constant `12463`, NPC ID for Death Talon Flamescale.
*   **NPC_DEATH_TALON_HATCHER**: Constant `12468`, NPC ID for Death Talon Hatcher.
*   **NPC_BLACKWING_TASKMASTER**: Constant `12458`, NPC ID for Blackwing Taskmaster.
*   **NPC_BLACKWING_TECHNICIAN**: Constant `13996`, NPC ID for Blackwing Technician.
*   **NPC_CORRUPTED_GREEN_WHELP**: Constant `14023`, NPC ID for Corrupted Green Whelp.
*   **NPC_CORRUPTED_RED_WHELP**: Constant `14022`, NPC ID for Corrupted Red Whelp.
*   **NPC_CORRUPTED_BLUE_WHELP**: Constant `14024`, NPC ID for Corrupted Blue Whelp.
*   **NPC_CORRUPTED_BRONZE_WHELP**: Constant `14025`, NPC ID for Corrupted Bronze Whelp.
*   **NPC_BLACKWING_WARLOCK**: Constant `12459`, NPC ID for Blackwing Warlock.
*   **NPC_DEATH_TALON_OVERSEER**: Constant `12461`, NPC ID for Death Talon Overseer.
*   **NPC_BLACKWING_SPELLBINDER**: Constant `12457`, NPC ID for Blackwing Spellbinder.
*   **NPC_DEATH_TALON_WYRMGUARD**: Constant `12460`, NPC ID for Death Talon Wyrmguard.
*   **NPC_BRONZE_DRAKANOID**: Constant `14263`, NPC ID for Bronze Drakanoid.
*   **NPC_BLUE_DRAKANOID**: Constant `14261`, NPC ID for Blue Drakanoid.
*   **NPC_RED_DRAKANOID**: Constant `14264`, NPC ID for Red Drakanoid.
*   **NPC_GREEN_DRAKANOID**: Constant `14262`, NPC ID for Green Drakanoid.
*   **NPC_BLACK_DRAKANOID**: Constant `14265`, NPC ID for Black Drakanoid.
*   **NPC_CHROMATIC_DRAKANOID**: Constant `14302`, NPC ID for Chromatic Drakanoid.
*   **NPC_BONE_CONSTRUCT**: Constant `14605`, NPC ID for Bone Construct.
*   **QUEST_NEFARIUS_CORRUPTION**: Constant `8730`, Quest ID for "Nefarius' Corruption."
*   **FACTION_MONSTER**: Constant `14`, Faction ID for Monster (hostile).
*   **FACTION_FRIENDLY**: Constant `35`, Faction ID for Friendly.
*   **RAZORGORE_MAX_HEALTH_DURING_POSESSION**: Constant `450000`, Max health cap for Razorgore during possession.

---

<!-- machine-true, projected from graph.json -->

## Map — blackwing_lair

*Source:* blackwing_lair.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|

---

<!-- verify: failed-members | invented: DATA_CHROMAGGUS_GUID, DATA_CHROM_BREATH, DATA_DOOR_CHROMAGGUS_ENTER, DATA_DOOR_CHROMAGGUS_EXIT, DATA_DOOR_CHROMAGGUS_SIDE, DATA_DOOR_LASHLAYER, DATA_DOOR_NEFARIAN, DATA_DOOR_RAZORGORE_ENTER, DATA_DOOR_RAZORGORE_EXIT, DATA_DOOR_VAELASTRASZ, DATA_EBONROC_GUID, DATA_EGG, DATA_FIREMAW_GUID, DATA_FLAMEGOR_GUID, DATA_GRETOK_GUID, DATA_HOW_EGG, DATA_LASHLAYER_GUID, DATA_NEFARIAN_GUID, DATA_NEFARIUS_GUID, DATA_NEF_COLOR, DATA_ORB_DOMINATION_GUID, DATA_RAZORGORE_GUID, DATA_SCEPTER_CHAMPION, DATA_SCEPTER_RUN_TIME, DATA_TRIGGER_GUID, DATA_VAELASTRASZ_GUID, FACTION_FRIENDLY, FACTION_MONSTER, GO_DRAKONID_BONES, MAX_DATAS, MAX_ENCOUNTER, NPC_BLACKWING_GUARDSMAN, NPC_BLACKWING_LEGGIONAIRE, NPC_BLACKWING_MAGE, NPC_BLACKWING_SPELLBINDER, NPC_BLACKWING_TASKMASTER, NPC_BLACKWING_TECHNICIAN, NPC_BLACKWING_WARLOCK, NPC_BLACK_DRAKANOID, NPC_BLUE_DRAKANOID, NPC_BONE_CONSTRUCT, NPC_BRONZE_DRAKANOID, NPC_CHROMAGGUS, NPC_CHROMATIC_DRAKANOID, NPC_CORRUPTED_BLUE_WHELP, NPC_CORRUPTED_BRONZE_WHELP, NPC_CORRUPTED_GREEN_WHELP, NPC_CORRUPTED_RED_WHELP, NPC_DEATH_TALON_CAPTAIN, NPC_DEATH_TALON_DRAGONSPAWN, NPC_DEATH_TALON_FLAMESCALE, NPC_DEATH_TALON_HATCHER, NPC_DEATH_TALON_OVERSEER, NPC_DEATH_TALON_SEETHER, NPC_DEATH_TALON_WYRMGUARD, NPC_DEATH_TALON_WYRMKIN, NPC_EBONROC, NPC_FIREMAW, NPC_FLAMEGOR, NPC_GREEN_DRAKANOID, NPC_GRETHOK_THE_CONTROLLER, NPC_LASHLAYER, NPC_LORD_NEFARIAN, NPC_NEFARIAN, NPC_ORB_OF_DOMINATION, NPC_RAZORGORE, NPC_RED_DRAKANOID, NPC_VAELASTRASZ, QUEST_NEFARIUS_CORRUPTION, RAZORGORE_MAX_HEALTH_DURING_POSESSION, SPELL_POSSESS, SPELL_POSSESS_VISUAL, TYPE_CHROMAGGUS, TYPE_EBONROC, TYPE_FIREMAW, TYPE_FLAMEGOR, TYPE_LASHLAYER, TYPE_NEFARIAN, TYPE_RAZORGORE, TYPE_SCEPTER_RUN, TYPE_VAELASTRASZ, TYPE_VAEL_EVENT -->
