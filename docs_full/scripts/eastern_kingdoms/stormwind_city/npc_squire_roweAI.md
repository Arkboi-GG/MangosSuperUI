# npc_squire_roweAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# npc_squire_roweAI

**Purpose & Responsibilities**

`npc_squire_roweAI` implements the artificial intelligence for the NPC **Rowe** (Entry ID `17804`, defined as `NPC_ROWE` in `quest_stormwind_rendezvous.h`). Rowe is a squire involved in the "Stormwind Rendezvous" and "The Great Masquerade" quest chain. This AI manages Rowe's movement along predefined waypoints, tracks the progress of the associated event via internal steps, and coordinates with the main quest logic to handle player interactions and event triggers. It acts as a subordinate actor in the larger scripted event managed by `npc_reginald_windsorAI`.

**Member-by-Member Behavior**

### **Reset**
*   **Kind:** Method
*   **Behavior:** This method overrides the base `ScriptedAI::Reset` interface. In the provided source (`quest_stormwind_rendevous.h`), the body is explicitly empty (`void Reset() override {}`). It performs no initialization or state clearing. Its primary role is to satisfy the virtual function signature required by the `ScriptedAI` framework, ensuring that if the creature resets (e.g., despawns and respawns, or the script engine forces a reset), the AI does not crash due to a missing override. Any actual state initialization for Rowe occurs elsewhere, likely in the constructor or during specific event triggers handled in `UpdateAI` or `MovementInform`.

### **Cross-Unit Boundaries**

*   **Called by `quest_stormwind_rendezvous/npc_squire_roweAI`:**
    The MAP indicates that `Reset` is called by `quest_stormwind_rendezvous/npc_squire_roweAI`. This reflects the internal lifecycle management of the AI object itself. When the game engine or the script system determines that Rowe's AI needs to be reset (typically upon respawn or manual reload), it invokes this method. Since the method is empty, the collaboration is purely structural: the caller expects a reset hook to exist, and this unit provides a no-op implementation. No data crosses this boundary, and no side effects occur.

*   **Calls Out:**
    The MAP lists no outgoing calls from `Reset` to other units. This is consistent with the empty implementation. Other members of this class (not listed in the MAP but present in the header, such as `UpdateAI` or `MovementInform`) would likely interact with the core game engine (movement system, player interaction system) or potentially with `npc_reginald_windsorAI` (Reginald Windsor's AI) to coordinate the quest event, but these interactions are not part of the `Reset` member's behavior.

**Data Model**

This unit does not directly access any database tables. The MAP confirms that the `Tables` column for `Reset` is empty, and the source code contains no SQL queries or direct database connections. All state is held in memory within the AI object's member variables (e.g., `m_uiStep`, `m_bEventProcessed`).

**Notable Implementation Details**

*   **Empty Override:** The most notable detail is that `Reset` is an empty override. This suggests that Rowe's state is either persistent across resets (unlikely for a quest NPC) or, more probably, that the initialization logic is handled in the constructor `npc_squire_roweAI(Creature* pCreature)` or in response to specific events like `MoveInLineOfSight` or `GossipHello` (which are declared in the header but not part of this specific MAP entry). Maintainers should be aware that calling `Reset` will not clear timers or step counters; if a clean slate is needed, it must be done manually in other methods.
*   **State Variables:** Although not part of the `Reset` member, the class declares several state variables (`m_uiTimer`, `m_uiStep`, `m_bEventProcessed`, `m_bWindsorUp`, `m_playerGuid`). These indicate that Rowe's behavior is stateful and driven by a timer and a step counter. The `Reset` method's emptiness implies that these states are not automatically cleared on reset, which could lead to bugs if the NPC is reset mid-event without proper cleanup in other parts of the code.
*   **Dependency on Windsor:** The presence of `m_bWindsorUp` and the close coupling with `npc_reginald_windsorAI` (as seen in the header's declaration of both AIs) suggests that Rowe's actions are tightly synchronized with Reginald Windsor's. Rowe likely waits for Windsor to reach certain points or complete certain actions before proceeding.

## Member Reference

**Reset**: Overrides the base `ScriptedAI::Reset` method with an empty body. It serves as a no-op placeholder to satisfy the virtual function interface, performing no initialization, state clearing, or data manipulation. It is called by the AI system when the creature resets, but since it does nothing, any necessary state management must be handled in other methods (e.g., constructor or event handlers).

---

<!-- machine-true, projected from graph.json -->

## Map — npc_squire_roweAI

*Source:* quest_stormwind_rendezvous.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Reset | method | — | quest_stormwind_rendezvous/npc_squire_roweAI | — |
