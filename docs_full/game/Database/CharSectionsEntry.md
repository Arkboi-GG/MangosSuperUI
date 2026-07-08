# CharSectionsEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CharSectionsEntry

**Purpose & Responsibilities**

`CharSectionsEntry` is a lightweight data structure within `DBCStructure.h` that represents a single row from the `CharSections.dbc` file. In the context of the WoW server emulation, this DBC file defines the valid combinations of character appearance attributes—specifically skin tone, face geometry, facial hair, hairstyle, and underwear—for every playable race and gender.

The primary responsibility of this unit is to provide a mechanism to query the validity of these appearance options. It exposes a single method, `HasFlag`, which allows other parts of the system to determine if a specific character section configuration is marked as unavailable or restricted by checking the `Flags` field against known bitmasks (such as `SECTION_FLAG_UNAVAILABLE`). This ensures that the server only accepts valid appearance data during character creation or modification, preventing clients from sending invalid or hacked appearance indices.

**Member-by-Member Behavior**

### Appearance Validation Logic

*   **`HasFlag`**: This inline method checks whether a specific `CharSectionFlags` bitmask is set in the entry's `Flags` field. It performs a bitwise AND operation between the stored `Flags` and the provided `flag` argument. If the result is non-zero, it returns `true`, indicating the flag is active. Currently, the only defined flag is `SECTION_FLAG_UNAVAILABLE` (0x01), which marks a particular variation/color combination as invalid for selection. This method is critical for filtering out unusable appearance options before they are applied to a character model.

**Cross-Unit Boundaries**

*   **Called by `DBCStores/GetAllValidCharSectionVariationAndColorPairs`**: The `DBCStores` unit (likely responsible for loading and querying DBC data) calls `HasFlag` to iterate through all `CharSectionsEntry` records. It uses this method to filter out entries where `SECTION_FLAG_UNAVAILABLE` is set, thereby constructing a list of only valid appearance pairs (variation index and color index) for a given race, gender, and section type. This ensures that the server-side validation logic aligns with Blizzard's intended restrictions.
*   **Called by `DBCStores/GetCharSectionEntry`**: Similarly, when retrieving a specific character section entry by ID or criteria, `DBCStores` invokes `HasFlag` to verify the integrity of the returned data. If the requested entry has the unavailable flag set, the calling logic can reject the request or handle it appropriately, preventing the application of invalid appearance data.

**Data Model**

This unit interacts exclusively with the `CharSections.dbc` file, which is a binary data file provided by the game client. It does not interact with any SQL database tables. The relevant fields from the DBC structure mapped to this class are:

*   `Race`: The race ID associated with this appearance option.
*   `Gender`: The gender (male/female) associated with this option.
*   `BaseSection`: The type of appearance section (e.g., skin, face, hair).
*   `VariationIndex`: The specific geometric variation (e.g., face shape, hair style).
*   `ColorIndex`: The specific color index (e.g., skin tone, hair color).
*   `Flags`: Bitmask containing status information, primarily `SECTION_FLAG_UNAVAILABLE`.

**Notable Implementation Details**

*   **Inline Efficiency**: The `HasFlag` method is declared `inline` within the struct definition. Given that this check is likely performed frequently during character creation and appearance updates, inlining avoids function call overhead, contributing to performance optimization in hot paths.
*   **Bitwise Flag Handling**: The use of a bitmask (`Flags`) allows for future extensibility. While only `SECTION_FLAG_UNAVAILABLE` is currently defined, additional flags could be added to the `CharSectionFlags` enum without changing the struct layout or the `HasFlag` logic, maintaining backward compatibility.
*   **Const Correctness**: The `HasFlag` method is marked `const`, ensuring it does not modify the state of the `CharSectionsEntry` object. This is important because these entries are typically loaded into read-only memory or cached stores, and const-correctness prevents accidental modification.

## Member Reference

**HasFlag**: An inline method that checks if a specific `CharSectionFlags` value is set in the entry's `Flags` field using a bitwise AND operation. It returns `true` if the flag is present, allowing callers to determine if a character appearance option is unavailable or otherwise restricted.

---

<!-- machine-true, projected from graph.json -->

## Map — CharSectionsEntry

*Source:* DBCStructure.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HasFlag | method | — | DBCStores/GetAllValidCharSectionVariationAndColorPairs, DBCStores/GetCharSectionEntry | — |
