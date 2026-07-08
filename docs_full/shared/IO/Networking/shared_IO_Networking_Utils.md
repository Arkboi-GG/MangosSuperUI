# shared_IO_Networking_Utils

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# shared_IO_Networking_Utils

## Purpose & Responsibilities

`shared_IO_Networking_Utils` provides a single utility function, `IsInSameSubnet`, for determining whether two IPv4 addresses belong to the same logical network segment. This unit operates strictly within the `IO::Networking` namespace and serves as a low-level helper for network address validation logic elsewhere in the codebase. It does not manage connections, handle I/O, or maintain state; it performs pure bitwise arithmetic on IP address representations.

## Member-by-Member Behavior

### `IsInSameSubnet`

This function determines if `ipAddressInQuestion` resides within the subnet defined by `subnetIpAddress` and `subnetMaskInCidrNotation`.

1.  **Protocol Validation**: The function first checks if both input `IpAddress` objects are of type `IPv4`. If either is not IPv4 (e.g., IPv6 or invalid), it immediately returns `false`. This ensures the bitwise logic below is only applied to 32-bit addresses.
2.  **Mask Validation**: It asserts that `subnetMaskInCidrNotation` is between 0 and 32 inclusive. This is a debug-time check (`MANGOS_ASSERT`) ensuring the CIDR notation is valid.
3.  **Bitwise Calculation**:
    *   It constructs a 32-bit binary subnet mask by shifting `0xFFFFFFFF` left by `(32 - subnetMaskInCidrNotation)` bits. For example, a /24 mask results in `0xFFFFFF00`.
    *   It retrieves the internal 32-bit unsigned integer representation of both the question address and the subnet base address using `_getInternalIPv4ReprAsUint32`.
    *   It applies the binary mask to both addresses using the bitwise AND operator.
    *   It compares the resulting network portions. If they are identical, the addresses are in the same subnet.

## Cross-Unit Boundaries

*   **Called by `RealmList/GetAddressForClient`**: The `RealmList` unit uses this function to validate client connection requests. Specifically, `RealmList.GetAddressForClient` likely uses `IsInSameSubnet` to determine if an incoming client IP matches a configured realm subnet, allowing the server to route or accept connections based on network topology rules.
*   **Calls `Errors/PrintStacktraceAndThrow`**: Indirectly via `MANGOS_ASSERT`. If the CIDR mask is out of bounds (outside 0–32), the assertion triggers error handling in the `Errors` unit. In release builds, this check is typically disabled, but in debug builds, it enforces strict parameter validity.
*   **Calls `IpAddress/GetType`**: Used to verify that both input addresses are IPv4. This delegates type-checking to the `IpAddress` unit.
*   **Calls `IpAddress/_getInternalIPv4ReprAsUint32`**: Used to extract the raw 32-bit integer value of the IP addresses for bitwise comparison. This accesses internal state of the `IpAddress` unit.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory objects passed as arguments.

## Notable Implementation Details

*   **IPv4 Only**: The function explicitly rejects non-IPv4 addresses. It does not support IPv6 subnet calculations.
*   **CIDR Notation**: The mask is expected in CIDR format (0–32), not as a dotted-decimal string or 32-bit integer. The conversion to a binary mask is handled internally via bit shifting.
*   **Assertion vs. Exception**: The use of `MANGOS_ASSERT` for the mask range check means that invalid masks will cause a crash or stack trace in debug builds but may lead to undefined behavior or incorrect results in release builds if the assertion is compiled out. Callers must ensure the mask is valid before calling this function in production environments.
*   **Bitwise Logic**: The calculation `0xFFFFFFFF << (32 - cidr)` relies on undefined behavior if `cidr` is 32, because shifting a 32-bit integer by 32 bits is undefined in C++. However, the assertion `subnetMaskInCidrNotation <= 32` allows 32. If `cidr` is 32, the shift amount is 0, resulting in `0xFFFFFFFF`, which is correct. Wait: `32 - 32 = 0`. Shifting by 0 is well-defined. The undefined behavior would occur if the shift amount was >= 32. Since the max shift is 32 (when cidr=0, shift=32), and min shift is 0 (when cidr=32, shift=0), the shift amount is always in [0, 32]. Shifting a 32-bit int by 32 is undefined in standard C++, but many compilers treat it as 0 or all bits set depending on context. Actually, `0xFFFFFFFF << 32` is undefined behavior. If `subnetMaskInCidrNotation` is 0, the shift is 32. This is a potential bug in strict C++ standards, though often works as intended on common platforms (resulting in 0). A safer implementation might use `uint64_t` for the shift or handle the 0 case separately. However, based on the source provided, this is the implemented logic.

## Member Reference

**IsInSameSubnet**: Determines if two IPv4 addresses share the same subnet by comparing their network portions after applying a CIDR-based bitmask. Returns `false` if either address is not IPv4. Uses `IpAddress.GetType` for validation, `IpAddress._getInternalIPv4ReprAsUint32` for data extraction, and `MANGOS_ASSERT` (linked to `Errors.PrintStacktraceAndThrow`) to enforce valid CIDR ranges. Called by `RealmList.GetAddressForClient`.

---

<!-- machine-true, projected from graph.json -->

## Map — shared_IO_Networking_Utils

*Source:* Utils.cpp, Utils.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IsInSameSubnet | function | Errors/PrintStacktraceAndThrow, IpAddress/GetType, IpAddress/_getInternalIPv4ReprAsUint32 | RealmList/GetAddressForClient | — |
