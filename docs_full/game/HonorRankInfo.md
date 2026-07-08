# HonorRankInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# HonorRankInfo

**Purpose & Responsibilities**

`HonorRankInfo` is a lightweight data structure defined in `HonorMgr.h` that encapsulates the state of a player's honor rank within the World of Warcraft emulation context. It serves as the canonical representation of a character's standing in the PvP honor system, storing both the internal numeric rank used for game logic and the visual rank displayed to the user.

The structure tracks four key attributes:
1.  **Internal Rank (`rank`)**: A value ranging from 0 to 18, representing the absolute position in the honor hierarchy.
2.  **Visual Rank (`visualRank`)**: An integer ranging from -4 to 14, corresponding to the icon or title shown in the client interface. Negative values typically represent dishonorable states, while positive values represent honorable ranks.
3.  **Rank Point Bounds (`minRP`, `maxRP`)**: Floating-point values defining the lower and upper thresholds of Honor Points required to maintain or achieve the current rank.
4.  **Positive Status (`positive`)**: A boolean flag indicating whether the current rank is considered "honorable" (true) or "dishonorable" (false). This distinction is critical for determining eligibility for certain rewards, titles, or visual cues.

As a plain-old-data (POD) struct with a default constructor, `HonorRankInfo` is designed for efficient copying and storage. It does not contain logic itself; rather, it is populated by static helper functions in `HonorMgr` (such as `CalculateRank`) and stored within `HonorMgr` instances or player objects.

## Member-by-Member Behavior

### **HonorRankInfo** (Constructor)

The default constructor initializes all fields to safe, neutral defaults:
*   `rank` is set to `0`.
*   `visualRank` is set to `0`.
*   `maxRP` and `minRP` are set to `0.0f`.
*   `positive` is set to `true`.

This initialization ensures that an uninitialized or newly created `HonorRankInfo` object represents a baseline, non-penalized state. The constructor is called whenever a new instance is created, either explicitly or implicitly during variable declaration.

## Cross-Unit Boundaries

The `HonorRankInfo` constructor is invoked by three distinct units, reflecting its role as a foundational data container for the honor system:

1.  **Called by `HonorMgr/CalculateRank`**:
    *   **Direction**: `HonorMgr` creates a local `HonorRankInfo` instance to compute the rank based on a specific amount of rank points.
    *   **Collaboration**: `HonorMgr` uses this temporary instance to determine the correct `rank`, `visualRank`, and RP bounds for a given point total. Once calculated, this instance is returned to the caller. This is the primary mechanism for converting raw honor points into a structured rank object.

2.  **Called by `HonorMgr/FlushRankPoints`**:
    *   **Direction**: `HonorMgr` likely creates or resets `HonorRankInfo` instances during the weekly maintenance process where rank points are recalculated and distributed.
    *   **Collaboration**: During the flush operation, the system may need to re-evaluate ranks for many players. Creating fresh `HonorRankInfo` objects allows the system to recalculate standings from scratch based on the latest weekly scores, ensuring consistency across the server.

3.  **Called by `Player.Main/SatisfyItemRequirements`**:
    *   **Direction**: The `Player` class (specifically the `Main` partial) invokes the constructor when checking if a player meets the requirements for equipping or using certain items.
    *   **Collaboration**: Some items in World of Warcraft have honor rank restrictions (e.g., "Requires Rank 10"). To check this, the `Player` unit may instantiate a `HonorRankInfo` object to compare the player's current rank against the item's requirement. Alternatively, it may use the constructor to create a reference object representing the required rank for comparison. This highlights `HonorRankInfo`'s role in gatekeeping access to PvP-restricted content.

## Data Model

`HonorRankInfo` does not directly interact with any database tables. It is a transient in-memory structure. However, the data it holds corresponds to columns in the `characters` table (specifically `totalHonorPoints`, `todayHonorGains`, `weekHonorGains`, `totalKills`, etc.) and potentially the `pvpstats` table, depending on the specific database schema version. The `HonorMgr` class handles the persistence of this data, loading and saving the underlying values that `HonorRankInfo` represents.

## Notable Implementation Details

*   **Dual Rank System**: The separation of `rank` (internal) and `visualRank` (display) is crucial. The internal rank is a linear progression (0–18), while the visual rank maps to client-side assets. This decoupling allows the server to handle complex rank calculations without being tied to specific client UI constraints.
*   **Positive/Negative Distinction**: The `positive` boolean is essential for distinguishing between honorable and dishonorable states. Dishonorable ranks (negative visual ranks) often carry penalties, such as reduced honor gains or inability to equip certain gear. The default value of `true` ensures that new or reset objects are treated as honorable unless explicitly changed.
*   **Floating-Point Precision**: The use of `float` for `minRP` and `maxRP` suggests that rank thresholds are not always integers. This allows for fine-grained control over rank progression, accommodating fractional honor points earned through various activities.
*   **Static Initialization**: Since `HonorRankInfo` is a simple struct, it is often initialized via static methods in `HonorMgr` (like `InitRankInfo` or `CalculateRankInfo`). This pattern centralizes the logic for determining rank properties, ensuring consistency across all instances.

## Member Reference

**HonorRankInfo**
Default constructor for the `HonorRankInfo` struct. Initializes `rank` to 0, `visualRank` to 0, `maxRP` and `minRP` to 0.0f, and `positive` to true. This provides a clean, default state for new honor rank objects, ensuring they start as honorable with zero points. Called by `HonorMgr/CalculateRank`, `HonorMgr/FlushRankPoints`, and `Player.Main/SatisfyItemRequirements`.

---

<!-- machine-true, projected from graph.json -->

## Map — HonorRankInfo

*Source:* HonorMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HonorRankInfo | ctor | — | HonorMgr/CalculateRank, HonorMgr/FlushRankPoints, Player.Main/SatisfyItemRequirements | — |
