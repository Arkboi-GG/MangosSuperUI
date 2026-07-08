# Base32

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `Base32` unit provides a self-contained implementation of the Base32 encoding and decoding algorithm, compliant with RFC 4648. It serves as a utility within the `Crypto::Encoding` namespace to convert binary data (`std::vector<uint8>`) into a text-safe string representation (`std::string`) and vice versa.

This unit is composed of two distinct layers:
1.  **Legacy C Implementation:** A port of a third-party library (originally by Adrien Kunysz, MIT licensed) that handles the low-level bit manipulation, character mapping, and sequence processing. This layer operates on raw `unsigned char` buffers.
2.  **vMangos C++ Wrapper:** A modern C++ interface (`Crypto::Encoding::Base32`) that manages memory allocation, type safety, and error handling using `std::string`, `std::vector`, and `nonstd::optional`.

The primary consumer of this unit is the authentication subsystem, specifically for generating and validating Time-based One-Time Passwords (TOTP).

## Member-by-Member Behavior

The members are divided into the internal C-style helper functions (which implement the core algorithm) and the public C++ API.

### Core Algorithm Helpers (Internal)

These functions are `static` and operate on raw buffers. They follow the RFC 4648 specification, treating data as sequences of 5 octets (40 bits) encoded into 8 Base32 characters (40 bits).

*   **min**: A simple helper returning the smaller of two `size_t` values. Used to determine the remaining length of input data during encoding chunks.
*   **pad**: Fills a buffer segment with the padding character `=`. This is invoked when an input sequence contains fewer than 5 octets, requiring the output to be padded to maintain the 8-character block structure.
*   **encode_char**: Maps a 5-bit integer value (0–31) to its corresponding Base32 ASCII character (`A`–`Z`, `2`–`7`). It uses a static lookup table and masks the input to ensure only the lower 5 bits are considered.
*   **decode_char**: Reverses `encode_char`. It maps an ASCII character back to its 5-bit integer value. It returns `-1` if the character is invalid (not in `A-Z` or `2-7`) or if it is a padding character. This strict validation is critical for security contexts.
*   **get_octet**: Calculates which input octet (byte) contains the start of a specific 5-bit output block. Since Base32 blocks span byte boundaries, this function determines the source byte index for a given block index (0–7).
*   **get_offset**: Calculates the bit shift required to align the relevant 5 bits within the source octet. The offset can be positive (shift right to discard trailing bits) or negative (indicating that bits spill over into the next octet).
*   **shift_right** / **shift_left**: Safe bitwise shift wrappers. Standard C++ bitwise shifts by negative amounts are undefined behavior. These functions handle negative offsets by reversing the shift direction (e.g., a negative right shift becomes a left shift), ensuring portable and correct bit extraction across octet boundaries.
*   **encode_sequence**: Encodes a single chunk of up to 5 octets into 8 Base32 characters. It iterates through the 8 output blocks, calculating the source octet and bit offset for each, extracting the 5-bit value, and converting it to a character. If the input chunk is shorter than 5 octets, it pads the remaining output positions with `=`.
*   **base32_encode**: The main driver for encoding. It iterates over the input buffer in 5-octet chunks, calling `encode_sequence` for each chunk. It uses the `BASE32_LEN` macro to manage buffer sizes.
*   **decode_sequence**: Decodes a single 8-character Base32 block into up to 5 octets. It processes each character, converting it to a 5-bit value and placing it into the correct position in the output buffer.
    *   *Notable Logic:* If an invalid character is encountered, it checks if it is a terminator (`\0`) or padding (`=`). If it is a true invalid character, it returns `-1` to signal failure. This behavior was customized from the original library to strictly reject malformed input rather than stopping silently.
*   **base32_decode**: The main driver for decoding. It iterates over the input string in 8-character chunks, calling `decode_sequence`. It accumulates the total number of bytes written. If `decode_sequence` returns `-1` (invalid character), it immediately returns `0` to indicate total failure.

### Public C++ API

These functions provide the safe, high-level interface used by the rest of the codebase.

*   **Encode**: Converts a `std::vector<uint8>` to a `std::string`.
    1.  Calculates the required output size using `BASE32_LEN`.
    2.  Resizes the output string to that size.
    3.  Calls the internal `base32_encode` function, casting the string's internal buffer to `unsigned char*`.
    4.  Returns the populated string.
*   **Decode**: Converts a Base32 `std::string` to a `std::vector<uint8>`, returning `nonstd::optional<std::vector<uint8>>`.
    1.  Returns an empty vector if the input string is empty.
    2.  Calculates the maximum possible output size. It uses `ALIGN8` to round the input length up to the nearest multiple of 8, then applies `UNBASE32_LEN`. This ensures the allocated buffer is large enough even if the input string length is not a perfect multiple of 8 (though valid Base32 should be).
    3.  Checks if the calculated output size exceeds `max_binary_output_size`. If so, it returns `nullopt` to prevent buffer overflows or excessive memory allocation.
    4.  Allocates a temporary vector with extra padding (`output_size + 8`) to satisfy the internal decoder's access patterns.
    5.  Calls `base32_decode`.
    6.  If `base32_decode` returns `0` (indicating invalid characters were found), it returns `nullopt`.
    7.  Otherwise, it resizes the output vector to the actual number of bytes written and returns it wrapped in an optional.

## Cross-Unit Boundaries

*   **Called by `AuthSocket/GenerateTotpPin`**: The `Decode` function is called by the authentication socket logic to parse TOTP seeds or codes received from clients. This highlights the security-critical nature of the `Decode` function's strict validation; failing to reject invalid Base32 strings could lead to authentication bypasses or parsing errors in security tokens.
*   **Calls `Errors/PrintStacktraceAndThrow`**: While not explicitly shown in the call stack for normal operation, the `Decode` function relies on `MANGOS_ASSERT` (which likely ties into the error handling system) to verify that the number of bytes written does not exceed the expected output size. If this assertion fails, it indicates a bug in the underlying `base32_decode` logic or a memory corruption issue, triggering a crash/debug break via the error handling unit.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory binary data and strings.

## Notable Implementation Details

1.  **Strict Decoding Failure**: The original library stopped decoding at invalid characters but continued processing or returned partial results. The vMangos version modifies `decode_sequence` and `base32_decode` to return `-1` and `0` respectively upon encountering any invalid character. This ensures that malformed Base32 strings (e.g., from user input or network corruption) are completely rejected rather than partially parsed, which is crucial for cryptographic operations like TOTP verification.
2.  **Bitwise Shift Safety**: The `shift_right` and `shift_left` functions explicitly handle negative offsets. In standard C++, shifting by a negative amount is undefined behavior. By converting negative right shifts to left shifts (and vice versa), the code ensures correct bit extraction when 5-bit blocks straddle byte boundaries.
3.  **Buffer Padding in Decode**: The `Decode` wrapper allocates `output_size + 8` bytes. The internal `base32_decode` function accesses memory beyond the immediate output block during its loop (specifically when checking for termination or padding). The extra 8 bytes provide a safe guard against out-of-bounds reads within the temporary buffer, although the final result is trimmed to the actual `written` count.
4.  **Alignment Handling**: The `Decode` function uses `ALIGN8` to round up the input string length before calculating the output size. This accommodates inputs that might not be perfectly aligned to 8-character blocks, preventing under-allocation of the output buffer. However, it relies on the internal decoder to correctly identify the end of valid data via padding or null terminators.
5.  **OpenSSL Replacement Note**: Comments in the source indicate that this custom implementation is intended to be replaced by OpenSSL's `EVP` functions once the project requires OpenSSL 1.1.0 or higher. Until then, this self-contained implementation avoids external dependencies for Base32 encoding.

## Member Reference

**min**: Helper function returning the minimum of two `size_t` values, used to limit chunk sizes during encoding.

**pad**: Fills a buffer segment with `=` characters for Base32 padding.

**encode_char**: Converts a 5-bit integer to its Base32 ASCII character equivalent using a lookup table.

**decode_char**: Converts a Base32 ASCII character to its 5-bit integer value, returning -1 for invalid or padding characters.

**get_octet**: Determines the source byte index for a given 5-bit block index in the encoding/decoding process.

**get_offset**: Calculates the bit shift amount needed to align a 5-bit block within a source byte, handling both positive and negative offsets.

**shift_right**: Performs a bitwise right shift, converting negative offsets to left shifts to avoid undefined behavior.

**shift_left**: Performs a bitwise left shift, delegating to `shift_right` with a negated offset.

**encode_sequence**: Encodes a chunk of up to 5 bytes into 8 Base32 characters, handling padding for incomplete chunks.

**base32_encode**: Iterates over input data in 5-byte chunks, calling `encode_sequence` to produce the full Base32 string.

**decode_sequence**: Decodes a chunk of 8 Base32 characters into up to 5 bytes, returning -1 if invalid characters are encountered.

**base32_decode**: Iterates over input Base32 data in 8-character chunks, calling `decode_sequence` and accumulating the output length; returns 0 on any invalid character.

**Encode**: Public C++ wrapper that converts a `std::vector<uint8>` to a `std::string` using the internal `base32_encode` function.

**Decode**: Public C++ wrapper that converts a Base32 `std::string` to a `std::vector<uint8>`, returning `nonstd::optional` to handle errors. It validates input length, prevents buffer overflows via `max_binary_output_size`, and rejects invalid Base32 strings by checking the return value of `base32_decode`.

---

<!-- machine-true, projected from graph.json -->

## Map — Base32

*Source:* Base32.cpp, Base32.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| min | function | — | — | — |
| pad | function | — | — | — |
| encode_char | function | — | — | — |
| decode_char | function | — | — | — |
| get_octet | function | — | — | — |
| get_offset | function | — | — | — |
| shift_right | function | — | — | — |
| shift_left | function | — | — | — |
| encode_sequence | function | — | — | — |
| base32_encode | function | — | — | — |
| decode_sequence | function | — | — | — |
| base32_decode | function | — | — | — |
| Encode | function | — | — | — |
| Decode | function | Errors/PrintStacktraceAndThrow | AuthSocket/GenerateTotpPin | — |
