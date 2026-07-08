# TransactionPart

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TransactionPart

**Purpose & Responsibilities**

`TransactionPart` is a plain data structure (POD-like, though it contains a constructor) defined in `TransactionLog.h`. It serves as a container for recording the specific details of a single side of a transaction within the World of Warcraft emulation environment. Specifically, it captures the GUID of the entity involved, the amount of gold/money exchanged, any spell associated with the transaction, and a fixed-size array of item entries, counts, and GUIDs involved.

The structure is designed to hold up to `MAX_TRANSACTION_ITEMS` (defined as 6) distinct item records. It is typically used in pairs within the `PlayerTransactionData` struct to represent two sides of an interaction (e.g., buyer and seller, or caster and target).

**Member-by-Member Behavior**

*   **`TransactionPart()`**: The default constructor initializes the entire memory footprint of the `TransactionPart` instance to zero using `memset`. This ensures that all fields (`lowGuid`, `money`, `spell`, and the three item arrays) start in a clean, empty state. This is critical because the structure relies on fixed-size arrays rather than dynamic containers; zero-initialization prevents garbage values from being interpreted as valid item entries or GUIDs.

**Cross-Unit Boundaries**

According to the provided MAP, `TransactionPart` has no outgoing calls to other units and is not explicitly called by other units in the cross-reference list. However, its usage is implicit in the broader transaction logging system. It is embedded within `PlayerTransactionData`, which is likely populated by higher-level game logic (such as auction house interactions, trade windows, or loot distribution) and then passed to logging mechanisms. The lack of explicit "Calls out" or "Called by" entries in the MAP suggests that `TransactionPart` itself is purely a data carrier, manipulated by code outside this specific translation unit definition.

**Data Model**

This unit does not directly interact with database tables. It is a runtime data structure. The data it holds may eventually be persisted to database tables related to transaction logs (e.g., `character_achievement_progress`, `auctionhouse`, or custom audit logs), but `TransactionPart` itself performs no SQL operations.

**Notable Implementation Details**

1.  **Fixed-Size Arrays**: The structure uses C-style arrays (`itemsEntries`, `itemsCount`, `itemsGuid`) with a maximum size of 6. This imposes a hard limit on the number of items that can be recorded in a single transaction part. Any transaction involving more than 6 items would require truncation or splitting, depending on how the caller handles this constraint.
2.  **Zero-Initialization via `memset`**: The use of `memset(this, 0, sizeof(TransactionPart))` in the constructor is a performance-oriented choice common in C++ game servers. It avoids iterating over each field individually. However, it assumes that all members are trivially copyable and that zero is a valid "empty" state for all fields (which is true for integers and pointers/references in this context, assuming `uint32` GUIDs are non-zero for valid entities).
3.  **Parallel Arrays**: Item data is stored in three parallel arrays (`itemsEntries`, `itemsCount`, `itemsGuid`). This design requires the caller to maintain index consistency across all three arrays. There is no encapsulation (like a nested struct or vector of structs) to enforce this integrity, placing the burden of correctness on the code that populates `TransactionPart`.
4.  **`lowGuid` vs Full GUID**: The structure stores `lowGuid` as a `uint32`. In WoW architecture, object GUIDs are often 64-bit, composed of a high part (type) and a low part (unique ID). Storing only the low part implies that the type of the entity (player, creature, item, etc.) is either known from context or stored elsewhere, or that the logging system only cares about the unique identifier portion.

## Member Reference

**TransactionPart**
The default constructor for the `TransactionPart` struct. It initializes all member variables to zero by calling `memset` on the instance's memory block. This ensures that `lowGuid`, `money`, `spell`, and the three item arrays (`itemsEntries`, `itemsCount`, `itemsGuid`) are cleared before use.

---

<!-- machine-true, projected from graph.json -->

## Map — TransactionPart

*Source:* TransactionLog.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TransactionPart | ctor | — | — | — |
