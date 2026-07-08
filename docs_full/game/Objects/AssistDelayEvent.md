# AssistDelayEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AssistDelayEvent

**Purpose & Responsibilities**
`AssistDelayEvent` is a transient event class defined in `Creature.h` that implements a delayed response mechanism for creature assistance in combat. It inherits from `BasicEvent`, integrating into the server’s asynchronous event scheduling system. Its specific role is to enforce a time gap between a creature calling for help and the actual engagement of allied creatures (assistants). This delay prevents instantaneous, overwhelming group responses to aggression, allowing for more realistic combat pacing and providing players a brief window to react or disengage.

**Member-by-Member Behavior**
The unit consists of a single declared member: the constructor.

*   **AssistDelayEvent**: This constructor initializes the event with the necessary context for the delayed action. It takes the `ObjectGuid` of the target (`victim`), a reference to the `Unit` that initiated the call (`owner`), and a `std::list<Creature*>` of potential assistants. Internally, it stores the victim's GUID, the owner reference, and converts the list of assistant pointers into a `std::vector<ObjectGuid>` (`m_assistantGuids`). This conversion ensures that the event holds stable identifiers rather than raw pointers, mitigating risks of dangling pointers if the assistant creatures are despawned or moved during the delay period. The default constructor is private, enforcing that all instances must be created with explicit context.

**Cross-Unit Boundaries**
*   **Calls Out**: The header does not declare any outgoing calls. However, the `Execute` method (defined in the corresponding `.cpp` file, not shown here but implied by the `BasicEvent` interface) will interact with `Creature` and `Unit` implementations to resolve the stored GUIDs and command the assistants to engage the victim.
*   **Called By**: This event is instantiated and scheduled by other parts of the codebase, typically within `Creature` methods responsible for calling for help (e.g., `CallForHelp` or `CallAssistance` in `Creature.cpp`). The `BasicEvent` scheduler invokes the `Execute` method when the scheduled delay expires.

**Data Model**
This unit does not interact directly with any database tables. It operates entirely on in-memory objects (`ObjectGuid`, `Unit`, `Creature`) and temporary state managed by the event scheduler.

**Notable Implementation Details**
*   **GUID-Based Storage**: The constructor accepts a `std::list<Creature*>` but stores `std::vector<ObjectGuid>`. This design choice prioritizes safety during the delay interval. Since creatures can be despawned, killed, or moved while the event is pending, storing raw pointers would lead to undefined behavior. The implementation must look up the creatures by GUID when `Execute` runs, checking for validity before acting.
*   **Owner Reference Risk**: The `owner` is stored as a direct reference (`Unit&`). If the owner unit is destroyed before the event executes, accessing this reference would cause a crash. The implementation in the `.cpp` file must ensure the owner remains valid or handle the case where the owner no longer exists.
*   **Private Default Constructor**: The default constructor is private, ensuring that `AssistDelayEvent` cannot be instantiated without the required victim, owner, and assistant data.

## Member Reference

**AssistDelayEvent**
Constructor that initializes the event with the victim's GUID, a reference to the owner unit, and a list of assistant creatures. It converts the assistant pointers into a vector of GUIDs for safe storage during the delay period.

---

<!-- machine-true, projected from graph.json -->

## Map — AssistDelayEvent

*Source:* Creature.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AssistDelayEvent | decl | — | — | — |
