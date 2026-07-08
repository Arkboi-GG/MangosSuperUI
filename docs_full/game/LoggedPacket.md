<!-- provenance: failed-members -->
# LoggedPacket

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LoggedPacket

**Purpose & Responsibilities**  
`LoggedPacket` is a lightweight aggregate struct defined in `SniffFile.h` that captures a single network packet event for logging or replay purposes. It stores whether the packet originated from the client (`isClientPacket`), a wall-clock timestamp of when the capture occurred (`timestamp`), and the serialized packet data itself (`data`, a `WorldPacket`). Its primary responsibility is to normalize the timing metadata on server-bound packets: if a packet is marked as *not* from the client (i.e., server-to-client) and lacks an embedded packet time, `LoggedPacket` injects the current high-resolution millisecond time via `WorldTimer::getMSTime()`. This ensures consistent temporal context for all logged traffic, regardless of whether the original sender included timing information.

The struct is instantiated exclusively by **MovementAnticheat/LogMovementPacket**, indicating its role in anti-cheat telemetry—specifically, recording movement-related network activity for later analysis or validation.

---

## Member-by-Member Behavior

### **LoggedPacket** (constructor)
Initializes the struct with three fields:
- `isClientPacket`: copied directly from the first argument.
- `data`: copy-constructed from the provided `WorldPacket const&`.
- `timestamp`: set to the current Unix epoch time via `time(nullptr)`.

Additionally, if the packet is *not* from the client (`!isClientPacket`) and the `WorldPacket` does not already contain a valid packet time (`!data.GetPacketTime()`), the constructor calls `data.FillPacketTime(WorldTimer::getMSTime())` to embed the current millisecond timestamp into the packet’s internal structure. This step is critical for ensuring that server-originated packets have a reliable, high-resolution time marker for downstream processing.

No other members exist in this unit. The struct is purely data-holding with initialization logic confined to the constructor.

---

## Cross-Unit Boundaries

### Called By: **MovementAnticheat/LogMovementPacket**
This is the sole caller of `LoggedPacket`’s constructor. The collaboration is straightforward: **MovementAnticheat/LogMovementPacket** constructs a `LoggedPacket` instance to encapsulate a captured network packet along with its directional metadata (client vs. server origin). The resulting object is then passed to logging infrastructure (e.g., `SniffFile::WritePacket`) for persistence.

No outbound calls are made from `LoggedPacket` to other units beyond the constructor’s use of `WorldTimer::getMSTime()`, which is a static utility function for retrieving the current time in milliseconds. This call is internal to the constructor and does not constitute a cross-unit dependency in the architectural sense—it is a standard library-like utility invocation.

---

## Data Model

This unit does not interact with any database tables. All data is held in memory within the `LoggedPacket` struct and transiently processed during construction. No SQL queries, table references, or schema dependencies are present in the source code.

---

## Notable Implementation Details

1. **Conditional Time Injection**:  
   The constructor’s conditional logic (`if (!isClientPacket && !data.GetPacketTime())`) is a key behavioral nuance. It assumes that client-originated packets either always carry valid packet times or that missing times on client packets are acceptable to leave uncorrected. Server-originated packets, however, are expected to have their times filled if absent. This asymmetry suggests that client-side packet timing may be handled elsewhere or deemed less critical for anti-cheat analysis.

2. **Copy Semantics**:  
   The `data` field is copy-constructed from the input `WorldPacket const&`. This implies that `WorldPacket` supports efficient copying (likely via reference counting or shallow copy mechanisms internally). Engineers must be aware that modifying the original `WorldPacket` after constructing a `LoggedPacket` will not affect the logged copy, but large packets may incur performance costs due to deep copying if `WorldPacket` does not optimize this.

3. **Timestamp Granularity Mismatch**:  
   The `timestamp` field uses `time_t` (second-level precision via `time(nullptr)`), while the injected packet time uses `WorldTimer::getMSTime()` (millisecond precision). This dual-timestamp approach may lead to inconsistencies if downstream consumers expect uniform granularity. However, since `timestamp` appears to serve as a coarse capture time and `data.GetPacketTime()` provides fine-grained event time, this design may be intentional for separating “when we logged it” from “when the event occurred.”

4. **No Validation of Input Packet**:  
   The constructor does not validate whether the input `WorldPacket` is empty, malformed, or otherwise invalid. It blindly copies the data and conditionally fills the time. If garbage packets are passed in, they will be logged as-is, potentially corrupting logs or misleading anti-cheat analysis.

---

## Member Reference

**LoggedPacket**  
Constructor that initializes `isClientPacket`, `data`, and `timestamp`. Injects millisecond-level packet time into server-originated packets lacking one. Called exclusively by **MovementAnticheat/LogMovementPacket**.

---

<!-- machine-true, projected from graph.json -->

## Map — LoggedPacket

*Source:* SniffFile.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LoggedPacket | ctor | — | MovementAnticheat/LogMovementPacket | — |

---

<!-- verify: failed-members | invented: MovementAnticheat/LogMovementPacket -->
