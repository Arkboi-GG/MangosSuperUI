# WardenKeyGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`WardenKeyGenerator` is a lightweight, stateful pseudo-random byte generator built on SHA-1. It consumes an initial seed buffer to derive two static state vectors (`o1`, `o2`) and one dynamic working vector (`o0`). It produces a deterministic stream of bytes by iteratively hashing the combination of these vectors. This stream is used by the Warden anti-cheat subsystem (`Log.Warden/Warden`) to generate session keys or nonces for client-server handshake verification.

## Member-by-Member Behavior

### Initialization
**`WardenKeyGenerator`**
Initializes the generator from a seed buffer. It splits the input `buff` (of length `size`) into two halves:
1.  `o1` becomes the SHA-1 hash of the first half (`size / 2` bytes).
2.  `o2` becomes the SHA-1 hash of the remaining bytes.
3.  `o0` is initialized to zero.
4.  It immediately calls `FillUp()` to compute the first valid `o0` digest and reset the consumption counter `taken` to 0.

### Byte Generation
**`Generate`**
Fills a destination buffer `buf` of length `sz` with bytes from the current `o0` digest. It iterates through the requested size, copying bytes from `o0` at index `taken`. If `taken` reaches 20 (the SHA-1 digest size), it calls `FillUp()` to regenerate `o0` and reset `taken` before continuing. This allows seamless generation of arbitrary-length streams.

### State Refresh
**`FillUp`**
Regenerates the working digest `o0`. It creates a new SHA-1 context, updates it sequentially with `o1`, the current `o0`, and `o2`, then stores the resulting digest back into `o0`. It resets `taken` to 0. Note that `o1` and `o2` remain constant throughout the object's lifetime; only `o0` evolves, forming a hash chain: $o0_{new} = SHA1(o1 || o0_{old} || o2)$.

## Cross-Unit Boundaries

*   **Called by `Log.Warden/Warden`**: The Warden module instantiates this class and calls `Generate` to obtain cryptographically derived bytes for anti-cheat protocol data.
*   **Calls into `Crypto::Hash::SHA1`**: All hashing logic is delegated to the `Crypto::Hash::SHA1` utility (`CreateZero`, `ComputeFrom`, `Generator`, `UpdateData`, `GetDigest`). No low-level crypto is implemented here.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory.

## Notable Implementation Details

1.  **Hardcoded Digest Size**: The check `if (taken == 20)` in `Generate` assumes SHA-1's 20-byte output. Changing the underlying hash algorithm would break this class without code changes.
2.  **Deterministic Stream**: Given the same seed, the output is identical. Security relies on SHA-1's pre-image resistance; while SHA-1 is collision-broken, it remains suitable for this legacy PRNG pattern.
3.  **Not Thread-Safe**: Mutable state (`o0`, `taken`) is unprotected. Concurrent access from multiple threads will cause corruption.
4.  **Odd-Length Seeds**: If the input seed size is odd, the second half passed to `o2` is one byte larger than the first half. This is handled correctly by pointer arithmetic.

## Member Reference

**WardenKeyGenerator**
Constructor that initializes the PRNG state. It splits the input seed buffer into two halves, hashing each to create static state vectors `o1` and `o2`. It initializes `o0` to zero and then immediately calls `FillUp()` to generate the first usable digest.

**Generate**
Method that fills a provided buffer with pseudo-random bytes. It reads sequentially from the internal `o0` digest. When `o0` is exhausted (20 bytes read), it triggers `FillUp()` to refresh the state before continuing. Supports generating arbitrary lengths by chaining digests.

**FillUp**
Private method that refreshes the internal state. It computes a new SHA-1 digest by hashing the concatenation of `o1`, `o0`, and `o2`, storing the result in `o0`. It also resets the byte consumption counter `taken` to 0.

---

<!-- machine-true, projected from graph.json -->

## Map — WardenKeyGenerator

*Source:* WardenKeyGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WardenKeyGenerator | ctor | — | Log.Warden/Warden | — |
| Generate | method | — | Log.Warden/Warden | — |
| FillUp | method | — | — | — |
