<!-- provenance: failed-members -->
# RealmZone

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# RealmZone

**RealmZone** is a header-only definition unit that provides a strongly typed enumeration (`RealmZone`) representing the geographic, linguistic, and operational zones associated with World of Warcraft realms within the Mangos server architecture. It serves as a shared contract between the realm list daemon (`realmd`) and the game world server (`mangosd`), ensuring both components interpret zone identifiers consistently.

The unit defines no functions, classes, or methods. Its entire responsibility is to declare the `RealmZone` enum and the constant `MAX_REALM_ZONES`. Consequently, it has no runtime behavior, no database interactions, and no cross-unit call dependencies beyond being included by other headers.

## Purpose & Responsibilities

The primary purpose of `RealmZone.h` is to standardize the representation of realm zones. In the context of the World of Warcraft client-server protocol, realms are categorized by zone to enforce specific rules regarding character naming conventions, language support, and server availability.

Key responsibilities include:
1.  **Enum Definition**: Providing the `RealmZone` enum, which maps integer values (`uint8`) to specific zone identifiers.
2.  **Constraint Documentation**: Via comments, it documents the character set restrictions (e.g., "extended-Latin", "East-Asian", "basic-Latin at create") associated with each zone. This information is critical for validation logic elsewhere in the codebase (likely in `realmd` or account management modules) that checks whether a proposed character name is valid for a given realm's zone.
3.  **Boundary Constant**: Defining `MAX_REALM_ZONES` as 38, which likely serves as an array size bound or loop limit for iterating over all possible zones in other units.

## Data Model

This unit does not interact with any database tables. It contains no SQL queries, no table references, and no schema definitions. The data it represents (zone IDs and their properties) is static and embedded directly in the C++ enum.

## Notable Implementation Details

### Zone-Specific Naming Rules
The comments attached to each enum value reveal important business logic constraints that consumers of this enum must respect:
*   **Standard Zones**: Zones like `REALM_ZONE_UNITED_STATES`, `REALM_ZONE_GERMAN`, etc., use "extended-Latin" character sets. This implies that characters in these zones can contain accented characters typical of Western European languages.
*   **East-Asian Zones**: Zones such as `REALM_ZONE_KOREA`, `REALM_ZONE_TAIWAN`, and `REALM_ZONE_CHINA` use "East-Asian" character sets.
*   **Tournament and CN Zones**: Many zones labeled as `TOURNAMENT` or `CN` (China) have a distinct rule: "basic-Latin at create, any at login". This suggests a two-phase validation process:
    1.  **Character Creation**: Strictly limited to basic Latin characters (A-Z, 0-9, perhaps hyphens/underscores).
    2.  **Login/Existing Characters**: May allow any language, possibly reflecting legacy data or specific tournament rules where existing names are preserved regardless of current zone restrictions.
*   **Unknown/Development**: `REALM_ZONE_UNKNOWN` and `REALM_ZONE_DEVELOPMENT` allow "any language", indicating relaxed validation for internal or undefined realms.

### Enum Type Safety
The enum is declared as `enum RealmZone : uint8`. This explicitly constrains the underlying type to an unsigned 8-bit integer. This is significant for network serialization and database storage, ensuring that zone IDs are compact and consistent with the expected protocol format.

### Maximum Zone Count
The macro `MAX_REALM_ZONES` is defined as 38. The highest enum value is `REALM_ZONE_CN5_8` (37). This suggests the enum is zero-indexed and covers values 0 through 37, totaling 38 distinct zones. Code relying on this constant should allocate arrays or loops of size 38 to safely cover all defined zones.

## Cross-Unit Boundaries

As a header-only definition unit with no executable code, `RealmZone.h` does not call into other units. However, it is **called by** (included by) other units that need to interpret realm zone data. While the MAP does not list specific callers, typical consumers would include:
*   **Realm List Handlers**: Units in `realmd` that populate the realm list sent to clients, needing to map database zone IDs to this enum for display or filtering.
*   **Character Name Validators**: Units in `mangosd` or `realmd` that validate character names during creation, using the zone ID to determine allowed character sets.
*   **Protocol Serializers**: Units that pack/unpack network packets containing realm zone information.

## Member Reference

This unit contains no functions or methods. The following entries correspond to the symbols defined in the MAP.

**RealmZone**
An enumeration (`enum RealmZone : uint8`) defining 38 distinct realm zones. Each value corresponds to a specific geographic or operational region (e.g., United States, Korea, China, Tournament servers) and includes comments specifying the allowed character sets for character names in that zone. Values range from 0 (`REALM_ZONE_UNKNOWN`) to 37 (`REALM_ZONE_CN5_8`).

**MAX_REALM_ZONES**
A preprocessor macro defined as `38`. Represents the total number of defined realm zones in the `RealmZone` enum. Used as a boundary constant for arrays or iterations involving all possible zones.

---

<!-- machine-true, projected from graph.json -->

## Map — RealmZone

*Source:* RealmZone.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|

---

<!-- verify: failed-members | invented: MAX_REALM_ZONES, RealmZone -->
