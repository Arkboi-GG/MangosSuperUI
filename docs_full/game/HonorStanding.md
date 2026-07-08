# HonorStanding

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# HonorStanding

**Purpose & Responsibilities**  
`HonorStanding` is a lightweight data structure within the `wowvmangos` codebase that represents a single player’s position in the weekly honor standings. It holds two pieces of information: the player’s unique identifier (`guid`) and their current competitive points (`cp`). Its primary responsibility is to enable sorting of players by competitive points in descending order, which is essential for determining honor rankings and distributing rank points during weekly maintenance cycles.

This struct is part of the broader honor system managed by `HonorMgr` and `HonorMaintenancer`. It does not perform any I/O, validation, or complex logic itself—it is purely a value holder with a custom comparison operator to support ordered containers like `std::vector` when sorted.

---

## Member-by-Member Behavior

### **HonorStanding** (constructor)  
The default constructor initializes both `guid` and `cp` to zero. This ensures that any instance created without explicit initialization starts in a known, safe state. The constructor is called exclusively by `HonorMgr/LoadStandingLists`, which populates standing lists from database results. Since `LoadStandingLists` is responsible for reading raw data and constructing these objects, the constructor’s role is minimal but critical for memory safety and predictable behavior.

### **operator<** (method)  
This operator defines the ordering semantics for `HonorStanding` instances. It returns `true` if the current object’s `cp` is greater than the other’s `cp`, effectively implementing a descending sort by competitive points. This inversion is intentional: higher competitive points should appear earlier in sorted lists, which aligns with how honor standings are displayed and processed (e.g., top players get priority in rank point distribution).

The operator is not called directly by any other unit in the provided map, but it is implicitly used whenever `std::sort` or similar algorithms operate on `HonorStandingList` (which is defined as `std::vector<HonorStanding>`). The sorting behavior is relied upon by `HonorMaintenancer.GenerateScores` and related methods that process standing lists to calculate rank points.

---

## Cross-Unit Boundaries

### Called By: `HonorMgr/LoadStandingLists`  
The constructor `HonorStanding()` is invoked by `HonorMgr/LoadStandingLists` (located in `HonorMgr.cpp`, though not included in the source packet) to instantiate new `HonorStanding` objects while parsing database rows. Each row corresponds to a player’s standing record, and the constructor provides a clean slate before fields are populated. No data flows back from this unit to the caller—this is a pure construction step.

### Implicit Usage by Sorting Algorithms  
While not explicitly listed in the “Called by” column, `operator<` is consumed by standard library sorting routines operating on `HonorStandingList`. These sorts are performed within `HonorMaintenancer` methods such as `GenerateScores` and `GetStandingCPByPosition`, which require the list to be ordered by `cp` descending. The direction of dependency is unidirectional: `HonorMaintenancer` depends on `HonorStanding`’s ordering semantics, but `HonorStanding` has no awareness of its consumers.

---

## Data Model

This unit does not directly interact with any database tables. The `HonorStanding` struct is populated indirectly via `HonorMgr/LoadStandingLists`, which reads from a table (likely `character_honor_standing` or similar, based on naming conventions in WoW emulators), but the schema for that table is not provided in the SCHEMA section, nor are any SQL queries visible in the source code for this unit. Therefore, no column-level details can be cited. The only data elements relevant to this unit are `guid` (uint32) and `cp` (float), which mirror expected database columns but are not validated or typed beyond their C++ declarations.

---

## Notable Implementation Details

- **Descending Sort via Ascending Operator**: The `operator<` implements descending order by returning `cp > hs.cp`. This is a common idiom in C++ to avoid writing custom comparators for `std::sort`, but it can be counterintuitive to readers unfamiliar with STL conventions. A maintainer must recognize that “less than” here means “should come after in ascending order,” hence the reversal.

- **No Validation or Bounds Checking**: Neither the constructor nor the operator performs any validation on `guid` or `cp`. Invalid values (e.g., negative `cp`, zero `guid`) are accepted silently. This places the burden of correctness on the calling code (`LoadStandingLists`), which must ensure data integrity before constructing instances.

- **Trivial Size and Copy Semantics**: As a POD-like struct with only primitive members, `HonorStanding` is cheap to copy and move. This makes it suitable for use in vectors and temporary calculations without performance concerns.

- **No Ownership or Lifecycle Management**: The struct does not manage resources, hold pointers, or participate in RAII patterns. It is entirely passive, relying on external systems for creation, population, and destruction.

---

## Member Reference

**HonorStanding** — Default constructor initializing `guid` and `cp` to zero. Called by `HonorMgr/LoadStandingLists` to create new standing entries from database results.

**operator<** — Comparison operator that orders `HonorStanding` instances by `cp` in descending order. Used implicitly by sorting algorithms on `HonorStandingList` within `HonorMaintenancer` methods to prioritize higher competitive points.

---

<!-- machine-true, projected from graph.json -->

## Map — HonorStanding

*Source:* HonorMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HonorStanding | ctor | — | HonorMgr/LoadStandingLists | — |
| operator< | method | — | — | — |
