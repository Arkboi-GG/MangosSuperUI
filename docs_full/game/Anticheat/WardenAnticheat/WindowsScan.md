# WindowsScan

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WindowsScan

**Purpose & Responsibilities**

`WindowsScan` is a specialized subclass of `Scan` within the Warden anti-cheat system, designed exclusively for Windows-based World of Warcraft clients. Its primary responsibility is to enforce platform-specific constraints on scan definitions by automatically applying the `ScanFlags::Windows` flag to all instances. It serves as the base class for various concrete scan types (e.g., memory, module, driver, and timing scans) that target Windows-specific APIs, memory layouts, and file structures.

The class itself contains no unique logic beyond its constructor; it delegates all building and checking behavior to the `Scan` base class via function pointers (`BuildT` and `CheckT`). It acts as a type-safe marker and configuration wrapper, ensuring that scans derived from it are correctly identified as Windows-only during the Warden execution pipeline.

**Member-by-Member Behavior**

### Construction and Initialization

*   **`WindowsScan`**: The sole functional member of this unit. This constructor initializes the `Scan` base class with the provided builder and checker function pointers, request/reply sizes, comment, and build range limits. Crucially, it modifies the provided `flags` argument by bitwise OR-ing it with `ScanFlags::Windows`. This ensures that any scan instance created through this constructor is permanently marked as applicable only to Windows clients. The default constructor is explicitly deleted to prevent instantiation without proper initialization.

### Declaration

*   **`WindowsScan#2`**: This represents the declaration of the class itself in the header file. It defines the inheritance hierarchy (`public Scan`) and the interface (constructor signature). It does not contain implementation logic.

**Cross-Unit Boundaries**

*   **Calls Out**:
    *   **`Scan` (Base Class)**: The `WindowsScan` constructor calls the protected constructor of `Scan` (`WardenScan/Scan`). It passes the modified flags (`flags | ScanFlags::Windows`) along with all other parameters. This establishes the core behavior of the scan (how it builds packets and checks responses) while `WindowsScan` restricts its applicability.
    *   **`ScanFlags` Operator**: The constructor uses the `operator|` defined for `ScanFlags` (likely in `WardenScan.hpp` or included headers) to combine the input flags with the `Windows` flag.

*   **Called By**:
    *   **Concrete Scan Implementations**: Numerous specific scan classes inherit from `WindowsScan` and invoke its constructor. These include:
        *   `WindowsCodeScan`: Scans executable memory segments for patterns.
        *   `WindowsDriverScan`: Detects specific drivers (e.g., WoWGlider).
        *   `WindowsFileHashScan`: Hashes client files to detect tampering.
        *   `WindowsHookScan`: Detects API hooking by analyzing jump instructions.
        *   `WindowsLuaScan`: Reads Lua variables from the client.
        *   `WindowsMemoryScan`: Reads arbitrary memory addresses.
        *   `WindowsModuleScan`: Checks for the presence of specific DLLs/modules.
        *   `WindowsStringHashScan`: Hashes strings in memory.
        *   `WindowsTimeScan`: Detects timing discrepancies between OS and game clocks.
    *   These callers provide the specific `BuildT` and `CheckT` lambdas or functions that define the actual anti-cheat logic, while relying on `WindowsScan` to handle the platform tagging.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely in memory, configuring scan objects that are later serialized into network packets or executed against client responses. The `ScanFlags::FromDatabase` flag exists in the enum, indicating that some scans *originating* from this system might be loaded from a database elsewhere in the codebase, but `WindowsScan` itself performs no SQL operations.

**Notable Implementation Details**

1.  **Flag Enforcement**: The most critical detail is the automatic injection of `ScanFlags::Windows`. This prevents accidental application of Windows-specific scans to Mac clients. If a developer forgets to pass the correct flags, the `WindowsScan` constructor ensures the `Windows` bit is always set.
2.  **Deleted Default Constructor**: `WindowsScan() = delete;` enforces that every instance must be fully initialized with a builder, checker, and metadata. This prevents the creation of empty or invalid scan objects.
3.  **Inheritance Strategy**: `WindowsScan` inherits publicly from `Scan`. This allows polymorphic usage where a `std::vector<Scan*>` can hold both `WindowsScan` and `MacScan` derivatives. The `ScanFlags` member in the base class is used at runtime to filter which scans are sent to which clients.
4.  **No Virtual Functions**: `WindowsScan` does not override any virtual functions from `Scan`. All behavior is determined by the `m_builder` and `m_checker` function pointers stored in the base class. This design keeps the class lightweight and focused solely on configuration.

## Member Reference

**WindowsScan**
Constructor that initializes the `Scan` base class. It takes a builder function, a checker function, request/reply sizes, a comment, flags, and build range limits. It automatically adds the `ScanFlags::Windows` flag to the provided flags before passing them to the base class. This ensures the scan is only executed on Windows clients. The default constructor is deleted.

**WindowsScan#2**
Declaration of the `WindowsScan` class in `WardenScan.hpp`. Defines the class as a public subclass of `Scan` and declares the constructor and deleted default constructor. Contains no implementation logic.

---

<!-- machine-true, projected from graph.json -->

## Map — WindowsScan

*Source:* WardenScan.hpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WindowsScan | ctor | — | WardenScan/WindowsCodeScan, WardenScan/WindowsDriverScan, WardenScan/WindowsFileHashScan, WardenScan/WindowsHookScan, WardenScan/WindowsLuaScan, WardenScan/WindowsLuaScan#2, WardenScan/WindowsLuaScan#3, WardenScan/WindowsMemoryScan, WardenScan/WindowsMemoryScan#2, WardenScan/WindowsMemoryScan#3, WardenScan/WindowsMemoryScan#4, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenScan/WindowsStringHashScan, WardenScan/WindowsTimeScan | — |
| WindowsScan#2 | decl | — | — | — |
