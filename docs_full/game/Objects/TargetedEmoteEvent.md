# TargetedEmoteEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TargetedEmoteEvent

**Purpose & Responsibilities**

`TargetedEmoteEvent` is a transient event class within the MaNGOS server architecture, designed to defer the execution of a specific visual animation (an "emote") on a `Creature` instance directed at a specific target. It inherits from `BasicEvent`, integrating into the server's global event scheduler. Its primary responsibility is to ensure that an emote action—such as a nod, wave, or attack animation—is performed by a creature toward a designated unit (identified by `ObjectGuid`) at a scheduled future time. This mechanism allows the server to decouple the decision to emote from the actual execution, ensuring synchronization with other game logic or providing a delay before the visual cue is broadcast to clients.

**Member-by-Member Behavior**

The unit consists of a single member: the constructor. The `Execute` method, while part of the class definition in `Creature.h`, is not listed in the provided MAP for this unit and is therefore not described here as a distinct behavioral entry, though its logic is intrinsic to the event's lifecycle.

*   **`TargetedEmoteEvent` (Constructor)**: Initializes the event object with references to the acting `Creature` (`owner`), the `ObjectGuid` of the intended target (`targetGuid`), and the numeric identifier of the emote to perform (`emoteId`). It stores these values in private member variables (`m_owner`, `m_targetGuid`, `m_emoteId`) for use during execution. The constructor explicitly initializes the base `BasicEvent` class, preparing the object for scheduling.

**Cross-Unit Boundaries**

*   **Called by `Map.ScriptCommands/ScriptCommand_Emote`**: The `Map` unit (specifically its script command handling subsystem) creates instances of `TargetedEmoteEvent` when processing emote commands from scripts (likely SmartScripts or similar scripted behaviors). The `Map` unit passes the relevant `Creature`, target GUID, and emote ID to the constructor, effectively outsourcing the deferred execution of the emote to this event class. This separation allows the `Map` unit to schedule the emote without blocking or managing the timing logic itself.
*   **Implicit Dependencies**: While not explicitly listed as "Calls out" in the MAP, the `Execute` method (defined in the header) relies on resolving the `ObjectGuid` to a `Unit` pointer (typically via `ObjectAccessor`) and invoking emote methods on the `Creature` instance. These interactions are internal to the server's core object model.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely on in-memory objects (`Creature`, `Unit`, `ObjectGuid`) and configuration data (emote IDs). No SQL queries or table accesses are performed by `TargetedEmoteEvent`.

**Notable Implementation Details**

*   **One-Time Execution**: The `Execute` method (defined in the header) returns `false`, which is the standard convention in MaNGOS for events that should fire only once. This ensures the event is automatically removed from the scheduler after execution, preventing memory leaks or repeated emotes.
*   **Target Validation**: The event checks if the target unit still exists before attempting to perform the emote. This is a critical safety measure, as targets may disappear (die, despawn, move out of range) between the time the event is scheduled and when it executes. Skipping the emote in such cases avoids potential crashes or undefined behavior.
*   **Reference Semantics**: The `Creature` is stored as a reference (`Creature& m_owner`). This implies that the `Creature` object must remain alive for the duration of the event. If the creature despawns or dies before the event executes, accessing the reference would lead to undefined behavior. However, in typical usage, the event is usually scheduled for a very short duration (often immediate or near-immediate), minimizing this risk. The `ObjectGuid` for the target is stored by value, allowing safe resolution even if the target object is destroyed.
*   **Emote Directionality**: By passing the target `Unit` to the emote function, the event ensures the emote is directed *at* the target. This is crucial for social emotes (like waving or nodding) where the direction matters for visual coherence.

## Member Reference

**TargetedEmoteEvent** (ctor): Constructs the event with a reference to the owning `Creature`, the `ObjectGuid` of the target, and the emote ID. Initializes the base `BasicEvent` class.

---

<!-- machine-true, projected from graph.json -->

## Map — TargetedEmoteEvent

*Source:* Creature.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TargetedEmoteEvent | ctor | — | Map.ScriptCommands/ScriptCommand_Emote | — |
