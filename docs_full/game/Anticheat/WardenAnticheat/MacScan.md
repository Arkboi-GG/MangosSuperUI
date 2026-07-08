# MacScan

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MacScan

**Purpose & Responsibilities**

`MacScan` is a specialized subclass of `Scan` within the Warden anti-cheat system, designed exclusively for macOS clients. Its primary responsibility is to enforce platform-specific integrity checks by ensuring that any scan instance created via this class is automatically tagged with the `ScanFlags::Mac` flag. This tagging mechanism allows the broader Warden infrastructure to filter and dispatch scans appropriately, ensuring that macOS-specific checks are never sent to Windows clients and vice versa.

The class itself contains no unique logic beyond its constructor; it delegates all behavioral implementation (building the network packet and checking the response) to the base `Scan` class via function objects (`BuildT` and `CheckT`) passed during construction. It serves as a type-safe factory wrapper that guarantees the correct platform flag is set.

## Member-by-Member Behavior

### **MacScan** (Constructor)

The constructor initializes a `MacScan` object by forwarding parameters to the protected `Scan` constructor. Its critical side effect is modifying the `flags` argument before passing it up the inheritance chain.

1.  **Flag Modification**: It takes the incoming `ScanFlags flags` parameter and performs a bitwise OR operation with `ScanFlags::Mac`. This ensures that regardless of the flags passed by the caller, the resulting scan object is permanently marked as applicable to macOS clients.
2.  **Delegation**: It passes the modified flags, along with the `builder`, `checker`, `requestSize`, `replySize`, `comment`, `minBuild`, and `maxBuild` arguments, to the `Scan` base class constructor.
3.  **Initialization**: The base `Scan` constructor stores these values in its member variables (`m_builder`, `m_checker`, `flags`, etc.), setting up the object for later use by the Warden manager.

## Cross-Unit Boundaries

*   **Called by `WardenScan/MacStringHashScan`**:
    *   **Direction**: Inbound.
    *   **Context**: The `MacStringHashScan` class (defined in the same header, `WardenScan.hpp`) inherits from both `MacScan` and `StringHashScan`. When a `MacStringHashScan` object is instantiated, it invokes the `MacScan` constructor as part of its initialization list.
    *   **Collaboration**: `MacStringHashScan` provides specific builder and checker logic for hashing strings on macOS. By calling `MacScan`'s constructor, it ensures that these string-hash checks are correctly flagged as macOS-only operations. This prevents the server from attempting to execute macOS-specific string hash scans against Windows clients, which would fail or cause protocol errors.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory, constructing and validating scan packets based on configuration data passed at runtime. Any persistence of scan definitions occurs in higher-level units (such as the Warden manager loading scans from the database), but `MacScan` itself is agnostic to data storage mechanisms.

## Notable Implementation Details

*   **Flag Enforcement**: The most significant detail of `MacScan` is the hardcoded inclusion of `ScanFlags::Mac`. This design choice centralizes platform identification logic. If a developer creates a new scan type for macOS, inheriting from `MacScan` guarantees the flag is set, reducing the risk of human error in manually setting flags.
*   **No Virtual Functions**: `MacScan` does not override any virtual methods from `Scan`. All behavior is determined by the `BuildT` and `CheckT` lambdas/function pointers passed to the constructor. This makes `MacScan` a lightweight wrapper rather than a complex polymorphic entity.
*   **Protected Base Constructor**: The base `Scan` constructor is `protected`, meaning `MacScan` cannot be instantiated directly by external code unless it is a friend or derived class. However, since `MacScan` is public, external code *can* instantiate `MacScan` directly if they provide the necessary builder/checker functors. The protection is primarily to prevent direct instantiation of the abstract-like `Scan` base class.
*   **Build Range Validation**: Like all `Scan` objects, `MacScan` carries `buildMin` and `buildMax` fields. These are used by the Warden manager to ensure the scan is only sent to clients running compatible game builds. The `MacScan` constructor does not validate these ranges; it simply stores them.

## Member Reference

**MacScan**
Constructor for the `MacScan` class. It accepts a builder functor, a checker functor, request/reply sizes, a comment, scan flags, and build range limits. It modifies the provided `flags` by adding `ScanFlags::Mac` via bitwise OR, then forwards all arguments to the `Scan` base class constructor. This ensures the scan is identified as macOS-specific. It is called by `MacStringHashScan` (in `WardenScan.hpp`) during its initialization.

---

<!-- machine-true, projected from graph.json -->

## Map — MacScan

*Source:* WardenScan.hpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MacScan | ctor | — | WardenScan/MacStringHashScan | — |
