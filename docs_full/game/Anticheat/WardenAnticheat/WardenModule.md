# WardenModule

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`WardenModule` represents a single, platform-specific binary payload used by the Warden anti-cheat system in World of Warcraft. It encapsulates the encrypted/compressed module binary, the decryption key, and the associated challenge-response metadata required to verify client integrity.

The class is responsible for:
1.  **Loading and Validating Assets**: Reading three distinct binary files (the module binary, the RC4 key, and the challenge/response data) from disk.
2.  **Integrity Verification**: Computing an MD5 hash of the module binary to ensure it matches expected signatures during transmission.
3.  **Platform Detection**: Determining whether the loaded module is intended for Windows or macOS (specifically Mac x86) by inspecting internal opcode structures.
4.  **Metadata Extraction**: Parsing specific offsets and opcodes from the challenge/response file that define how the client should perform memory scans and handle challenges.

This unit is strictly a data container and loader; it does not perform network communication or active cheating detection itself. It prepares the data structures that `WardenModuleMgr` uses to distribute checks to clients.

## Member-by-Member Behavior

### Construction and Loading

**`WardenModule(std::string const& bin, std::string const& kf, std::string const& cr)`**
This constructor loads the complete Warden module state from three file paths:
1.  **Binary Module (`bin`)**: Reads the entire file into `binary`. It validates that the file is at least 264 bytes (defined as `SignatureSize + 4`). After loading, it computes the MD5 hash of the raw binary data using `Generator.MD5/ComputeFrom#2` and stores it in `hash`.
2.  **Key File (`kf`)**: Reads the RC4 decryption key into `key`. It strictly validates that the file size is exactly 16 bytes (`KeySize`).
3.  **Challenge/Response File (`cr`)**: Reads structured data defining the anti-cheat logic.
    *   It first reads three fixed-size headers: `memoryRead`, `pageScanCheck`, and `opcodes`.
    *   It then calculates the remaining file size to determine the number of `ChallengeResponseEntry` records. It validates that the remaining data size is perfectly divisible by the size of `ChallengeResponseEntry`.
    *   It reads the array of entries into `crk`.
    *   **Platform-Specific Validation**: If the module is detected as Windows-compatible (via the `Windows()` method), it performs two critical checks:
        *   It verifies that `memoryRead` and `pageScanCheck` are non-zero. If either is zero, it throws a runtime error, indicating corrupted or incompatible module data.
        *   It calculates `scanTerminator` by finding the first byte value (0–255) that does *not* appear in the `opcodes` array. This value acts as a sentinel to indicate the end of a scan sequence.

**`WardenModule()`**
A default constructor declared in the header but not implemented in the source. Given the strict validation in the parameterized constructor and the lack of a body in `.cpp`, this likely exists to satisfy compiler requirements for certain container operations or move semantics, though instances created via this path would contain uninitialized data. In practice, `WardenModuleMgr` always uses the parameterized constructor.

### Platform Identification

**`Windows()`**
Determines the target operating system of the module.
*   It iterates through the `opcodes` array (9 bytes).
*   If *any* byte in `opcodes` is non-zero, it returns `true` (Windows).
*   If all bytes are zero, it returns `false` (macOS x86).
*   This heuristic relies on the fact that macOS modules historically ship with zeroed-out opcode arrays in this specific structure, while Windows modules populate them with valid scan opcodes.

## Cross-Unit Boundaries

*   **Called by `WardenModuleMgr/WardenModuleMgr`**: The manager creates instances of `WardenModule` by invoking the parameterized constructor. It passes file paths for the binary, key, and CR data. The manager relies on this unit to throw exceptions if the files are missing, malformed, or invalid, allowing the manager to handle startup failures gracefully.
*   **Calls `Generator.MD5/ComputeFrom#2`**: During construction, the unit delegates the calculation of the binary's integrity hash to the cryptographic utility unit. This ensures the hash stored in `hash` is consistent with the rest of the codebase's hashing standards.

## Data Model

This unit does not interact with any database tables. All data is loaded from local binary files on the filesystem.

## Notable Implementation Details

1.  **Sentinel Calculation Logic**: The calculation of `scanTerminator` in the constructor is notable. It assumes that the 9-byte `opcodes` array will never use all 256 possible byte values. It finds the smallest unused byte value to serve as a terminator. This logic only runs for Windows modules; macOS modules do not set `scanTerminator` because they do not use this opcode-based scanning mechanism in the same way.
2.  **Strict Size Validation**: The constructor enforces rigid size constraints. The key file *must* be exactly 16 bytes. The binary file *must* be at least 264 bytes. The CR file's variable-length section *must* be a multiple of `sizeof(ChallengeResponseEntry)`. Any deviation results in immediate termination via `std::runtime_error`.
3.  **Memory Layout Assumptions**: The code assumes `ChallengeResponseEntry` is packed tightly (`#pragma pack(push, 1)` in the header). The constructor reads raw bytes directly into structs and vectors, relying on this packing to correctly interpret the binary file format.
4.  **Move Semantics**: The class defines `WardenModule(WardenModule&& other) = default;`. This allows efficient transfer of ownership of the large `binary` and `crk` vectors when moving modules between containers, avoiding deep copies.
5.  **Client Build Guard**: The entire implementation is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_5_1`. This indicates that Warden modules in this format are only relevant for client versions newer than 1.5.1. Older clients likely used a different anti-cheat mechanism or module format.

## Member Reference

**`WardenModule#2`** (Constructor): Loads the Warden module from three binary files (module binary, RC4 key, challenge/response data). Validates file sizes and contents. Computes the MD5 hash of the binary using `Generator.MD5/ComputeFrom#2`. For Windows modules, validates that memory read/page scan offsets are present and calculates a `scanTerminator` byte not present in the opcodes. Throws `std::runtime_error` on any I/O failure or data inconsistency.

**`WardenModule`** (Default Constructor): Declared in header, not implemented in source. Likely a placeholder for move-semantics compatibility or container requirements; produces an object with uninitialized members.

**`Windows`**: Returns `true` if any byte in the `opcodes` array is non-zero, indicating a Windows module. Returns `false` if all opcodes are zero, indicating a macOS x86 module. Used by the constructor to apply platform-specific validation rules.

---

<!-- machine-true, projected from graph.json -->

## Map — WardenModule

*Source:* WardenModule.cpp, WardenModule.hpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WardenModule#2 | ctor | Generator.MD5/ComputeFrom#2 | WardenModuleMgr/WardenModuleMgr | — |
| WardenModule | ctor | — | — | — |
| Windows | method | — | WardenModuleMgr/WardenModuleMgr | — |
