# SRP6

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `SRP6` class implements the **Secure Remote Password (SRP-6)** protocol, serving as the cryptographic engine for account authentication in `wowvmangos`. It manages the generation of ephemeral keys, derivation of session keys, and verification of proofs exchanged between the client and server. The class encapsulates large-number arithmetic (via `BigNumber`) and SHA1 hashing, maintaining internal state for primes, generators, salts, verifiers, and ephemeral keys. It supports both initial login flows (driven by `AuthSocket`) and account management operations (driven by `AccountMgr`).

## Member-by-Member Behavior

### Initialization and Constants

*   **`SRP6`**: Initializes the instance with fixed SRP parameters: a 1024-bit safe prime $N$ and generator $g=7$.

### Login and Reconnection Flow

These methods support the interactive authentication handshake managed by `AuthSocket`.

*   **`CalculateHostPublicEphemeral`**: Generates a random server private ephemeral $b$ and computes the public ephemeral $B = (3v + g^b) \pmod N$. It uses a hardcoded multiplier $k=3$. Called by `AuthSocket::_HandleLogonChallenge`.
*   **`CalculateSessionKey`**: Validates the client public ephemeral $A$ (must not be zero or divisible by $N$). Computes the scrambling parameter $u = H(A, B)$ and the shared secret $S = (A \cdot v^u)^b \pmod N$. Returns `false` if safeguards fail. Called by `AuthSocket::_HandleLogonProof__PostRecv`.
*   **`HashSessionKey`**: Derives the strong session key $K$ from $S$ using a custom method: splitting $S$ into even/odd byte streams, hashing each with SHA1, and interleaving the results. Called by `AuthSocket::_HandleLogonProof__PostRecv`.
*   **`CalculateProof`**: Computes the server proof $M$ by hashing $H(N)\oplus H(g)$, $H(\text{username})$, salt $s$, $A$, $B$, and $K$. Called by `AuthSocket::_HandleLogonProof__PostRecv`.
*   **`Proof`**: Compares the server-computed proof $M$ with the client-provided proof. **Note:** The logic is inverted; it returns `false` if the proofs match (`memcmp` returns 0) and `true` if they differ. Callers must interpret this correctly. Called by `AuthSocket::_HandleLogonProof__PostRecv`.
*   **`Finalize`**: Computes the server's second proof $M_2 = H(A, M, K)$ to send to the client. Called by `AuthSocket::_HandleLogonProof__PostRecv`.
*   **`SetStrongSessionKey`**: Sets $K$ from a hex string, used during reconnection. Called by `AuthSocket::_HandleReconnectChallenge`.
*   **`GetHostPublicEphemeral`**, **`GetGeneratorModulo`**, **`GetPrime`**, **`GetProof`**, **`GetStrongSessionKey`**: Accessors for $B$, $g$, $N$, $M$, and $K$. Used by `AuthSocket` to construct packets.

### Account Management and Verification

These methods support credential creation and validation managed by `AccountMgr`.

*   **`CalculateVerifier` (Overload 1)**: Generates a random 32-byte salt and delegates to the second overload. Called by `AccountMgr::CreateAccount`, `AccountMgr::ChangePassword`, and `AccountMgr::ChangeUsername`.
*   **`CalculateVerifier#2` (Overload 2)**: Computes the password verifier $v = g^x \pmod N$, where $x = H(A, s, \text{reversed}(rI))$. The input `rI` (hash of username:password) is byte-reversed before hashing. Updates internal $s$ and $v$. Called by `AccountMgr::CheckPassword`.
*   **`ProofVerifier`**: Compares the internally computed verifier $v$ (as hex) with a stored verifier string `vC`. Returns `true` if they match. Called by `AccountMgr::CheckPassword`.
*   **`SetSalt`**, **`SetVerifier`**: Load $s$ and $v$ from hex strings, validating they are non-zero. Called by `AuthSocket::_HandleLogonChallenge`.
*   **`GetSalt`**, **`GetVerifier`**: Accessors for $s$ and $v$. Used by `AccountMgr` to persist credentials.

## Cross-Unit Boundaries

*   **`AuthSocket`**: Drives the authentication state machine. Passes client data ($A$, proofs) and retrieves server data ($B$, $N$, $g$, $M$, $K$). Handles reconnection by restoring $K$.
*   **`AccountMgr`**: Manages persistence. Requests new salt/verifier pairs for account changes and verifies passwords by comparing computed verifiers against database values.
*   **`BigNumber`**: Provides modular exponentiation, random generation, and binary/hex conversion.
*   **`Crypto::Hash::SHA1`**: Provides hashing for proofs, session keys, and verifier derivation.

## Data Model

This unit contains no SQL queries. It operates on in-memory `BigNumber` objects representing `salt` and `verifier` fields from the `account` table, passed as hex strings by `AccountMgr`.

## Notable Implementation Details

1.  **Inverted Proof Return Value**: `Proof` returns `false` on successful match and `true` on mismatch. This is counter-intuitive and relies on `AuthSocket` handling the inversion.
2.  **Hardcoded Multiplier**: `CalculateHostPublicEphemeral` uses $k=3$ instead of the standard $k=H(N,g)$.
3.  **Custom Key Derivation**: `HashSessionKey` uses a split-and-interleave SHA1 strategy rather than simple $K=H(S)$.
4.  **Byte Reversal**: `CalculateVerifier#2` reverses the input hash `rI` before computing $x$, indicating endianness differences between storage/transmission and internal representation.

## Member Reference

*   **SRP6**: Constructor initializing prime $N$ and generator $g$.
*   **CalculateHostPublicEphemeral**: Generates server private ephemeral $b$ and public ephemeral $B$.
*   **CalculateProof**: Computes server proof $M$ from username, salt, keys, and session key.
*   **CalculateSessionKey**: Validates client key $A$ and computes shared secret $S$.
*   **CalculateVerifier**: Generates random salt and computes verifier $v$.
*   **CalculateVerifier#2**: Computes verifier $v$ from provided salt and password hash.
*   **GetHostPublicEphemeral**: Returns $B$.
*   **GetGeneratorModulo**: Returns $g$.
*   **GetPrime**: Returns $N$.
*   **GetProof**: Returns $M$.
*   **GetSalt**: Returns $s$.
*   **GetStrongSessionKey**: Returns $K$.
*   **GetVerifier**: Returns $v$.
*   **SetStrongSessionKey**: Sets $K$ from hex string.
*   **HashSessionKey**: Derives $K$ from $S$ using custom split-hash logic.
*   **Proof**: Compares server proof $M$ with client proof; returns `false` on match.
*   **ProofVerifier**: Compares computed $v$ with stored verifier string; returns `true` on match.
*   **Finalize**: Computes server second proof $M_2$.
*   **SetSalt**: Sets $s$ from hex string.
*   **SetVerifier**: Sets $v$ from hex string.

---

<!-- machine-true, projected from graph.json -->

## Map — SRP6

*Source:* SRP6.cpp, SRP6.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SRP6 | ctor | BigNumber/SetDword, BigNumber/SetHexStr | AccountMgr/ChangePassword, AccountMgr/ChangeUsername, AccountMgr/CheckPassword, AccountMgr/CreateAccount | — |
| CalculateHostPublicEphemeral | method | BigNumber/BigNumber#3, BigNumber/GetNumBytes, BigNumber/ModExp, BigNumber/operator%, BigNumber/operator*, BigNumber/operator+, BigNumber/operator=, BigNumber/SetRand, Errors/PrintStacktraceAndThrow | AuthSocket/_HandleLogonChallenge | — |
| CalculateProof | method | BigNumber/BigNumber, BigNumber/SetBinary, Digest/size#2, Generator.SHA1/ComputeFrom, Generator.SHA1/ComputeFrom#3, Generator.SHA1/Generator, Generator.SHA1/GetDigest, Generator.SHA1/UpdateData#3 | AuthSocket/_HandleLogonProof__PostRecv | — |
| CalculateSessionKey | method | BigNumber/isZero, BigNumber/ModExp, BigNumber/operator%, BigNumber/operator*, BigNumber/operator=, BigNumber/SetBinary, Generator.SHA1/Generator, Generator.SHA1/GetDigest, Generator.SHA1/UpdateData#3 | AuthSocket/_HandleLogonProof__PostRecv | — |
| CalculateVerifier | method | BigNumber/AsHexStr, BigNumber/BigNumber, BigNumber/SetRand | AccountMgr/ChangePassword, AccountMgr/ChangeUsername, AccountMgr/CreateAccount | — |
| CalculateVerifier#2 | method | BigNumber/AsByteArray, BigNumber/BigNumber, BigNumber/GetNumBytes, BigNumber/isZero, BigNumber/ModExp, BigNumber/operator=, BigNumber/SetBinary, BigNumber/SetHexStr, Digest/size#2, Generator.SHA1/Generator, Generator.SHA1/GetDigest, Generator.SHA1/UpdateData#3, Generator.SHA1/UpdateData#4 | AccountMgr/CheckPassword | — |
| GetHostPublicEphemeral | method | — | AuthSocket/_HandleLogonChallenge | — |
| GetGeneratorModulo | method | — | AuthSocket/_HandleLogonChallenge | — |
| GetPrime | method | — | AuthSocket/_HandleLogonChallenge | — |
| GetProof | method | — | — | — |
| GetSalt | method | — | AccountMgr/ChangePassword, AccountMgr/ChangeUsername, AccountMgr/CreateAccount | — |
| GetStrongSessionKey | method | — | AuthSocket/_HandleLogonProof__PostRecv, AuthSocket/_HandleReconnectProof | — |
| GetVerifier | method | — | AccountMgr/ChangePassword, AccountMgr/ChangeUsername, AccountMgr/CreateAccount | — |
| SetStrongSessionKey | method | — | AuthSocket/_HandleReconnectChallenge | — |
| HashSessionKey | method | BigNumber/AsByteArray, BigNumber/SetBinary, Generator.SHA1/ComputeFrom#4 | AuthSocket/_HandleLogonProof__PostRecv | — |
| Proof | method | BigNumber/AsByteArray | AuthSocket/_HandleLogonProof__PostRecv | — |
| ProofVerifier | method | BigNumber/AsHexStr | AccountMgr/CheckPassword | — |
| Finalize | method | Generator.SHA1/Generator, Generator.SHA1/GetDigest, Generator.SHA1/UpdateData#3 | AuthSocket/_HandleLogonProof__PostRecv | — |
| SetSalt | method | BigNumber/isZero, BigNumber/SetHexStr | AuthSocket/_HandleLogonChallenge | — |
| SetVerifier | method | BigNumber/isZero, BigNumber/SetHexStr | AuthSocket/_HandleLogonChallenge | — |
