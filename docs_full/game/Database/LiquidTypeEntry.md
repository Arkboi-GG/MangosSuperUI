# LiquidTypeEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LiquidTypeEntry

**Purpose & Responsibilities**

`LiquidTypeEntry` is a Plain Old Data Structure (POD) that represents a single row from the `LiquidType.dbc` file in the World of Warcraft client data. Its primary responsibility is to map a specific liquid identifier (`LiquidId`) to its physical properties and associated gameplay effects. Specifically, it defines:
1.  **Visual/Physical Classification:** The `Type` field categorizes the liquid (e.g., Water, Magma, Slime), which dictates how the client renders the surface and how physics interactions (such as swimming or sinking) behave.
2.  **Gameplay Effects:** The `SpellId` field links the liquid to a specific spell effect. This is critical for environmental hazards; for instance, standing in Magma typically triggers a damage-over-time spell, while standing in Slime might apply a movement-slowing debuff.

This structure is part of the larger `DBCStructure.h` header, which defines the memory layout for all DBC (Data Block Client) files. These structures are packed tightly (`#pragma pack(1)`) to match the binary format of the game client's data files, allowing the server to parse them directly into memory.

**Member-by-Member Behavior**

The unit consists of two constructors and four public data members. It contains no methods other than construction, serving purely as a data container.

*   **Constructors:**
    *   **Parameterized Constructor:** Initializes all fields explicitly. This is likely used during the DBC loading process where the server parses the binary file and instantiates these objects with the extracted values.
    *   **Default Constructor:** Initializes the object to a default state (zero-initialized due to `= default` and standard POD rules). This is used when declaring arrays or vectors of `LiquidTypeEntry` before populating them.

*   **Data Members:**
    *   `Id`: The unique identifier for this entry within the `LiquidType.dbc` file. Used as the primary key for lookups.
    *   `LiquidId`: An internal identifier used by the client and server to identify the specific liquid instance in the world geometry. Comments indicate specific IDs correspond to specific liquids (e.g., 23 for Water, 35 for Magma).
    *   `Type`: An enumeration-like integer defining the category of the liquid. Comments map `0` to Magma, `2` to Slime, and `3` to Water. This value drives logic related to player interaction (swimming vs. walking) and visual rendering.
    *   `SpellId`: The ID of the spell applied to entities interacting with this liquid. This allows the server to cast the appropriate environmental effect (damage, slow, etc.) based on the liquid type.

**Cross-Unit Boundaries**

According to the provided MAP, `LiquidTypeEntry` has **no outgoing calls** to other units and is **not called by** other units in the context of this specific documentation scope. However, in the broader system:
*   **Called By:** This structure is instantiated and populated by the DBC loading subsystem (likely in a file such as `DBCStoreLoader.cpp` or similar, though not listed in the MAP). Once loaded, instances of `LiquidTypeEntry` are typically accessed by:
    *   **Movement/Physics Systems:** To determine if a player is swimming or walking based on the `Type`.
    *   **Spell/Aura Systems:** To retrieve the `SpellId` and apply the corresponding effect when a player enters the liquid.
    *   **Map/Grid Systems:** To resolve liquid types at specific coordinates for collision and interaction checks.

**Data Model**

`LiquidTypeEntry` maps directly to the `LiquidType.dbc` file. It does not interact with SQL database tables. The data is static client-side data loaded at server startup.

**Notable Implementation Details**

1.  **Memory Packing:** The struct is defined within a `#pragma pack(1)` block. This ensures that the compiler does not insert padding bytes between members, guaranteeing that the in-memory layout matches the binary layout of the DBC file exactly. This is crucial for correct parsing.
2.  **Hardcoded Magic Numbers:** The comments in the source code reveal hardcoded mappings for `LiquidId` and `Type` (e.g., `// 23: Water; 29: Ocean...`). While helpful for debugging, this indicates that the logic relying on these values assumes specific DBC content. If the DBC file changes (e.g., in a different game patch), these comments become stale, and potentially the logic depending on them if it uses magic numbers instead of proper enums.
3.  **No Validation:** The struct itself performs no validation on the input values. It is assumed that the DBC loader provides valid data. Invalid `SpellId` or `Type` values would only cause issues when the data is consumed by other systems.
4.  **Default Initialization:** The use of `= default` for the constructor ensures that aggregate initialization rules apply, making it safe to use in standard containers like `std::vector` or `std::array` without explicit initialization lists.

## Member Reference

**LiquidTypeEntry**
The parameterized constructor initializes the `Id`, `LiquidId`, `Type`, and `SpellId` members with the provided arguments. This is the primary means of creating a fully populated entry during DBC loading.

**LiquidTypeEntry#2**
The default constructor, declared with `= default`. It creates an instance with all members zero-initialized (or default-initialized for primitive types). This supports usage in fixed-size arrays or dynamic containers where elements are added later.

---

<!-- machine-true, projected from graph.json -->

## Map — LiquidTypeEntry

*Source:* DBCStructure.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LiquidTypeEntry | ctor | — | — | — |
| LiquidTypeEntry#2 | decl | — | — | — |
