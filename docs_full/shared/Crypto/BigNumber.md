<!-- provenance: verbose, failed-members -->
# BigNumber

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`BigNumber` is a C++ wrapper around OpenSSL’s `BIGNUM` structure, providing arbitrary-precision integer arithmetic for the server’s cryptographic protocols. It manages the lifecycle of the underlying `BIGNUM` pointer via RAII, ensuring allocation on construction and deallocation on destruction.

The class supports standard arithmetic (`+`, `-`, `*`, `/`, `%`) and specialized operations like modular exponentiation (`ModExp`) and random generation (`SetRand`), which are essential for the Secure Remote Password (SRP) protocol used in client authentication. It also provides conversion utilities to serialize these integers into binary byte arrays, hexadecimal strings, decimal strings, or fixed-width integers (`uint32`), facilitating network transmission and internal processing.

This unit is a pure utility component with no game logic, database interaction, or world simulation responsibilities. It is consumed exclusively by the authentication subsystem (`AuthSocket`, `WorldSocket`) and the SRP implementation (`SRP6`).

## Member-by-Member Behavior

### Construction and Destruction
*   **`BigNumber()`**: Allocates a new, zero-initialized `BIGNUM` via `BN_new`.
*   **`BigNumber(BigNumber const& bn)`**: Creates a deep copy using `BN_dup`, ensuring independent lifecycles.
*   **`BigNumber(uint32 val)`**: Allocates a new `BIGNUM` and initializes it with a 32-bit unsigned integer via `BN_set_word`.
*   **`~BigNumber()`**: Frees the underlying `BIGNUM` memory using `BN_free`.

### Initialization and Assignment
*   **`SetDword(uint32 val)`**: Overwrites the value with a 32-bit unsigned integer.
*   **`SetQword(uint64 val)`**: Overwrites the value with a 64-bit unsigned integer. The implementation splits the 64-bit value into high and low 32-bit halves, shifting and adding them to construct the big number.
*   **`SetBinary(uint8 const* bytes, int len)`**: Initializes the number from a raw byte array. The input is assumed to be little-endian; the method reverses the bytes into a temporary buffer before passing them to `BN_bin2bn`, which expects big-endian order.
*   **`SetHexStr(const char* str)`**: Parses a hexadecimal string into the `BIGNUM`. Returns the number of hex digits processed, or 0 if invalid.
*   **`SetRand(int numbits)`**: Generates a cryptographically secure random number of the specified bit length using `BN_rand`.
*   **`operator=`**: Performs a deep copy assignment from another `BigNumber` using `BN_copy`.

### Arithmetic Operations
Non-compound operators (`+`, `-`, `*`, `/`, `%`) create a temporary `BigNumber`, apply the corresponding compound operator, and return the result by value. Compound operators modify `this` in place.

*   **`operator+=` / `operator+`**: Addition.
*   **`operator-=` / `operator-`**: Subtraction.
*   **`operator*=` / `operator*`**: Multiplication. Uses `BN_CTX` for `BN_mul`.
*   **`operator/=` / `operator/`**: Division. Discards the remainder. Uses `BN_CTX` for `BN_div`.
*   **`operator%=` / `operator%`**: Modulo. Discards the quotient. Uses `BN_CTX` for `BN_mod`.

### Cryptographic Primitives
*   **`Exp(BigNumber const& bn)`**: Computes $this^{bn}$ using `BN_exp`. Returns a new `BigNumber`.
*   **`ModExp(BigNumber const& bn1, BigNumber const& bn2)`**: Computes $(this^{bn1}) \pmod{bn2}$ using `BN_mod_exp`. This is the core operation for SRP key exchanges. Returns a new `BigNumber`.

### Inspection and Conversion
*   **`GetNumBytes()`**: Returns the minimum number of bytes required to represent the current value.
*   **`AsDword()`**: Converts the `BIGNUM` to a `uint32`. Truncates if the value exceeds 32 bits.
*   **`isZero()`**: Returns `true` if the value is mathematically zero.
*   **`BN()`**: Exposes the raw `bignum_st*` pointer for direct OpenSSL interoperability.
*   **`AsByteArray(int minSize, bool reverse)`**: Converts the number to a `std::vector<uint8>`. Pads with leading zeros if `minSize` is greater than the natural byte length. By default (`reverse = true`), the output is reversed to little-endian order to match internal game structures.
*   **`AsHexStr()`**: Converts the number to a lowercase hexadecimal string. Manages memory for the OpenSSL-generated buffer using `OPENSSL_free`.
*   **`AsDecStr()`**: Converts the number to a decimal string. Manages memory for the OpenSSL-generated buffer using `OPENSSL_free`.

## Cross-Unit Boundaries

`BigNumber` calls no other application units, relying solely on OpenSSL. It is depended upon by:

1.  **`AuthSocket`**:
    *   **`_HandleLogonChallenge`**: Generates random salts and verifies client proofs using `BigNumber` constructors, `SetHexStr`, `SetRand`, and `AsByteArray`.
    *   **`_HandleReconnectProof` / `_HandleReconnectChallenge`**: Processes session resumption data using `SetBinary`, `GetNumBytes`, `AsByteArray`, and `SetRand`.
    *   **`VerifyPinData`**: Validates PIN codes by constructing `BigNumber` instances from binary data and converting them to decimal strings via `AsDecStr`.
    *   **`_HandleLogonProof__PostRecv`**: Finalizes login proof verification, converting results to hex strings via `AsHexStr`.

2.  **`WorldSocket`**:
    *   **`_HandleAuthSession`**: Processes the initial authentication handshake, parsing hex strings (`SetHexStr`) and binary data (`SetBinary`, `AsByteArray`) for SRP calculations.

3.  **`SRP6`**:
    *   **`CalculateHostPublicEphemeral`**: Generates the server’s public ephemeral key using `ModExp`, `operator*`, `operator+`, `operator%`, `operator=`, `GetNumBytes`, and `SetRand`.
    *   **`CalculateSessionKey`**: Derives the shared session key using `ModExp`, `operator*`, `operator%`, `operator=`, and `isZero`.
    *   **`CalculateVerifier` / `CalculateVerifier#2`**: Computes the password verifier using `ModExp`, `SetHexStr`, `SetBinary`, `AsByteArray`, `isZero`, and `operator=`.
    *   **`CalculateProof`**: Generates the server’s proof using `SetBinary`.
    *   **`HashSessionKey`**: Hashes the session key by converting it to a byte array via `AsByteArray` and `SetBinary`.
    *   **`ProofVerifier`**: Verifies the client’s proof using `AsHexStr`.
    *   **`SetSalt` / `SetVerifier`**: Initializes SRP parameters using `SetHexStr` and `isZero`.
    *   **`SRP6`**: Initializes parameters using `SetDword` and `SetHexStr`.

4.  **`AccountMgr`**:
    *   **`ChangePassword` / `ChangeUsername` / `CreateAccount`**: Generate new verifiers and salts, converting results to hex strings via `AsHexStr` for database storage.

5.  **`Generator` (HMACSHA1, MD5, SHA1)**:
    *   **`UpdateData#3`**: Accepts `BigNumber` objects, converting them to byte arrays via `AsByteArray` to feed into hash updates.

6.  **`Log.Warden`**:
    *   **`Warden`**: Processes Warden anti-cheat data, converting binary signatures to byte arrays via `AsByteArray`.

## Data Model

`BigNumber` interacts with **no database tables**. It operates purely on in-memory data structures.

## Notable Implementation Details

1.  **Endianness Handling**: `SetBinary` assumes little-endian input and reverses it for OpenSSL. `AsByteArray` defaults to reversing output back to little-endian (`reverse = true`). This symmetry allows seamless round-tripping between internal memory layouts and OpenSSL’s big-endian requirements. Callers must pass `reverse = false` only when explicit network-order (big-endian) data is needed.
2.  **Memory Management**: `AsHexStr` and `AsDecStr` correctly call `OPENSSL_free` on buffers allocated by OpenSSL, preventing leaks.
3.  **Context Allocation**: Compound arithmetic operators (`*=`, `/=`, `%=`) allocate and free a `BN_CTX` on every invocation. This prioritizes simplicity and exception safety over micro-optimization, which is acceptable given the infrequent nature of these operations in authentication flows.
4.  **No Exception Safety**: The class does not handle allocation failures from `BN_new`. If allocation fails, subsequent OpenSSL calls will likely crash. This is acceptable in a server environment where memory exhaustion is a fatal condition.
5.  **`SetQword` Workaround**: The manual bit-shifting in `SetQword` handles 64-bit values by splitting them into two 32-bit words, compensating for the lack of a direct `BN_set_u64` in some OpenSSL configurations.

## Member Reference

**BigNumber** (ctor): Allocates a new `BIGNUM` via `BN_new`. Called by `AuthSocket/VerifyPinData`, `AuthSocket/_HandleLogonChallenge`, `AuthSocket/_HandleReconnectProof`, `SRP6/CalculateProof`, `SRP6/CalculateVerifier`, `SRP6/CalculateVerifier#2`, `WorldSocket/_HandleAuthSession`.

**BigNumber#2** (ctor): Copy constructor. Creates a deep copy using `BN_dup`.

**BigNumber#3** (ctor): Constructor from `uint32`. Allocates a new `BIGNUM` and sets its value using `BN_set_word`. Called by `SRP6/CalculateHostPublicEphemeral`.

**~BigNumber** (dtor): Frees the underlying `BIGNUM` memory using `BN_free`.

**SetDword** (method): Sets the value to a 32-bit unsigned integer using `BN_set_word`. Called by `SRP6/SRP6`.

**operator+** (method): Adds another `BigNumber` to this one, returning a new `BigNumber`. Implemented via copy and `+=`. Called by `SRP6/CalculateHostPublicEphemeral`.

**SetQword** (method): Sets the value to a 64-bit unsigned integer by splitting into high/low 32-bit parts and combining them.

**operator-** (method): Subtracts another `BigNumber` from this one, returning a new `BigNumber`. Implemented via copy and `-=`.

**SetBinary** (method): Initializes the number from a little-endian byte array by reversing it and passing to `BN_bin2bn`. Called by `AuthSocket/VerifyPinData`, `AuthSocket/_HandleReconnectProof`, `SRP6/CalculateProof`, `SRP6/CalculateSessionKey`, `SRP6/CalculateVerifier#2`, `SRP6/HashSessionKey`.

**operator*** (method): Multiplies this `BigNumber` by another, returning a new `BigNumber`. Implemented via copy and `*=`. Called by `SRP6/CalculateHostPublicEphemeral`, `SRP6/CalculateSessionKey`.

**operator/** (method): Divides this `BigNumber` by another, returning a new `BigNumber`. Implemented via copy and `/=`.

**SetHexStr** (method): Parses a hexadecimal string into the `BIGNUM`. Returns the number of digits processed. Called by `AuthSocket/_HandleLogonChallenge`, `SRP6/CalculateVerifier#2`, `SRP6/SetSalt`, `SRP6/SetVerifier`, `SRP6/SRP6`, `WorldSocket/_HandleAuthSession`.

**SetRand** (method): Generates a random number of the specified bit length using `BN_rand`. Called by `AuthSocket/_HandleLogonChallenge`, `AuthSocket/_HandleReconnectChallenge`, `SRP6/CalculateHostPublicEphemeral`, `SRP6/CalculateVerifier`.

**operator%** (method): Computes the modulo of this `BigNumber` by another, returning a new `BigNumber`. Implemented via copy and `%=`. Called by `SRP6/CalculateHostPublicEphemeral`, `SRP6/CalculateSessionKey`.

**operator=** (method): Deep copy assignment from another `BigNumber` using `BN_copy`. Called by `SRP6/CalculateHostPublicEphemeral`, `SRP6/CalculateSessionKey`, `SRP6/CalculateVerifier#2`.

**operator+=** (method): Adds another `BigNumber` to this one in place using `BN_add`.

**BN** (method): Returns the raw `bignum_st*` pointer.

**operator-=** (method): Subtracts another `BigNumber` from this one in place using `BN_sub`.

**operator*=** (method): Multiplies this `BigNumber` by another in place using `BN_mul` with a temporary `BN_CTX`.

**operator/=** (method): Divides this `BigNumber` by another in place using `BN_div` with a temporary `BN_CTX`.

**operator%=** (method): Computes the modulo of this `BigNumber` by another in place using `BN_mod` with a temporary `BN_CTX`.

**Exp** (method): Computes exponentiation ($this^{bn}$) using `BN_exp` with a temporary `BN_CTX`. Returns a new `BigNumber`.

**ModExp** (method): Computes modular exponentiation ($(this^{bn1}) \pmod{bn2}$) using `BN_mod_exp` with a temporary `BN_CTX`. Returns a new `BigNumber`. Called by `SRP6/CalculateHostPublicEphemeral`, `SRP6/CalculateSessionKey`, `SRP6/CalculateVerifier#2`.

**GetNumBytes** (method): Returns the number of bytes required to represent the value using `BN_num_bytes`. Called by `AuthSocket/_HandleReconnectProof`, `SRP6/CalculateHostPublicEphemeral`, `SRP6/CalculateVerifier#2`.

**AsDword** (method): Converts the value to a `uint32` using `BN_get_word`.

**isZero** (method): Checks if the value is zero using `BN_is_zero`. Called by `SRP6/CalculateSessionKey`, `SRP6/CalculateVerifier#2`, `SRP6/SetSalt`, `SRP6/SetVerifier`.

**AsByteArray** (method): Converts the value to a `std::vector<uint8>`. Pads with leading zeros if `minSize` is exceeded. Reverses the byte order to little-endian by default (`reverse=true`). Called by `AuthSocket/VerifyPinData`, `AuthSocket/_HandleLogonChallenge`, `AuthSocket/_HandleReconnectChallenge`, `Generator.HMACSHA1/UpdateData#3`, `Generator.MD5/UpdateData#3`, `Generator.SHA1/UpdateData#3`, `Log.Warden/Warden`, `SRP6/CalculateVerifier#2`, `SRP6/HashSessionKey`, `SRP6/Proof`, `WorldSocket/_HandleAuthSession`.

**AsHexStr** (method): Converts the value to a hexadecimal string using `BN_bn2hex`. Manages memory with `OPENSSL_free`. Called by `AccountMgr/ChangePassword`, `AccountMgr/ChangeUsername`, `AccountMgr/CreateAccount`, `AuthSocket/_HandleLogonProof__PostRecv`, `SRP6/CalculateVerifier`, `SRP6/ProofVerifier`.

**AsDecStr** (method): Converts the value to a decimal string using `BN_bn2dec`. Manages memory with `OPENSSL_free`. Called by `AuthSocket/VerifyPinData`.

---

<!-- machine-true, projected from graph.json -->

## Map — BigNumber

*Source:* BigNumber.cpp, BigNumber.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BigNumber | ctor | — | AuthSocket/VerifyPinData, AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleReconnectProof, SRP6/CalculateProof, SRP6/CalculateVerifier, SRP6/CalculateVerifier#2, WorldSocket/_HandleAuthSession | — |
| BigNumber#2 | ctor | — | — | — |
| BigNumber#3 | ctor | — | SRP6/CalculateHostPublicEphemeral | — |
| ~BigNumber | dtor | — | — | — |
| SetDword | method | — | SRP6/SRP6 | — |
| operator+ | method | — | SRP6/CalculateHostPublicEphemeral | — |
| SetQword | method | — | — | — |
| operator- | method | — | — | — |
| SetBinary | method | — | AuthSocket/VerifyPinData, AuthSocket/_HandleReconnectProof, SRP6/CalculateProof, SRP6/CalculateSessionKey, SRP6/CalculateVerifier#2, SRP6/HashSessionKey | — |
| operator* | method | — | SRP6/CalculateHostPublicEphemeral, SRP6/CalculateSessionKey | — |
| operator/ | method | — | — | — |
| SetHexStr | method | — | AuthSocket/_HandleLogonChallenge, SRP6/CalculateVerifier#2, SRP6/SetSalt, SRP6/SetVerifier, SRP6/SRP6, WorldSocket/_HandleAuthSession | — |
| SetRand | method | — | AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleReconnectChallenge, SRP6/CalculateHostPublicEphemeral, SRP6/CalculateVerifier | — |
| operator% | method | — | SRP6/CalculateHostPublicEphemeral, SRP6/CalculateSessionKey | — |
| operator= | method | — | SRP6/CalculateHostPublicEphemeral, SRP6/CalculateSessionKey, SRP6/CalculateVerifier#2 | — |
| operator+= | method | — | — | — |
| BN | method | — | — | — |
| operator-= | method | — | — | — |
| operator*= | method | — | — | — |
| operator/= | method | — | — | — |
| operator%= | method | — | — | — |
| Exp | method | — | — | — |
| ModExp | method | — | SRP6/CalculateHostPublicEphemeral, SRP6/CalculateSessionKey, SRP6/CalculateVerifier#2 | — |
| GetNumBytes | method | — | AuthSocket/_HandleReconnectProof, SRP6/CalculateHostPublicEphemeral, SRP6/CalculateVerifier#2 | — |
| AsDword | method | — | — | — |
| isZero | method | — | SRP6/CalculateSessionKey, SRP6/CalculateVerifier#2, SRP6/SetSalt, SRP6/SetVerifier | — |
| AsByteArray | method | — | AuthSocket/VerifyPinData, AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleReconnectChallenge, Generator.HMACSHA1/UpdateData#3, Generator.MD5/UpdateData#3, Generator.SHA1/UpdateData#3, Log.Warden/Warden, SRP6/CalculateVerifier#2, SRP6/HashSessionKey, SRP6/Proof, WorldSocket/_HandleAuthSession | — |
| AsHexStr | method | — | AccountMgr/ChangePassword, AccountMgr/ChangeUsername, AccountMgr/CreateAccount, AuthSocket/_HandleLogonProof__PostRecv, SRP6/CalculateVerifier, SRP6/ProofVerifier | — |
| AsDecStr | method | — | AuthSocket/VerifyPinData | — |

---

<!-- verify: failed-members | invented: operator -->
