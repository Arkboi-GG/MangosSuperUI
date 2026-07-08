# npc_spirit_shadeAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`npc_spirit_shadeAI` is a minimal, passive creature AI used within the **Dragon of Nightmare** encounter (specifically associated with the boss **Lethon**, as indicated by the member `m_lethonGuid`). Its primary responsibility is to act as a stationary or non-aggressive entity that persists until a specific spell condition is met. Unlike standard hostile mobs, this AI explicitly suppresses all combat initiation and reaction behaviors (`EnterCombat`, `AttackedBy`, `AttackStart`, `EnterEvadeMode` are empty stubs). It exists solely to wait for a spell hit event (`SpellHitTarget`) or to manage its own lifecycle via `UpdateAI`, likely serving as a visual effect, a target dummy, or a conditional trigger component in the Lethon phase of the raid.

## Member-by-Member Behavior

The unit implements four core lifecycle methods that are intentionally inert, and two active methods that drive its limited logic.

### Combat Suppression
The following methods are overridden to do nothing. This ensures the Spirit Shade never engages in combat, never attacks players, and does not flee or reset its state during normal combat flow. It effectively "ghosts" through the combat system.

*   **EnterCombat**: Empty body. The creature enters combat state but performs no actions (no aggro announcements, no initial spells).
*   **AttackedBy**: Empty body. The creature ignores being attacked. It does not retaliate or mark attackers.
*   **AttackStart**: Empty body. The creature will not initiate an attack on any unit, even if in range.
*   **EnterEvadeMode**: Empty body. The creature does not perform any cleanup or movement when evading (though given its passive nature, evasion is likely triggered externally or never occurs).

### Active Logic
*   **SpellHitTarget**: This is the primary interaction point for external events. It is called when a spell hits the Spirit Shade. While the specific implementation logic is not visible in the provided header (and the MAP indicates no internal calls), its presence suggests the Shade reacts to specific spells—likely dispelling, dying, or triggering a phase change in the Lethon encounter.
*   **UpdateAI**: The main tick loop. It manages timers and periodic checks. Given the member variables `m_uiDelay` and `m_lethonGuid`, this method likely checks if the delay has expired or verifies the status of the linked Lethon boss.

## Cross-Unit Boundaries

*   **Called by (Other Units)**: The MAP indicates no external units call these specific members. However, `SpellHitTarget` is implicitly called by the game engine's spell system when a spell resolves on this creature. `UpdateAI` is called by the core AI scheduler.
*   **Calls out (Other Units)**: The MAP lists no outgoing calls. This implies the logic within `SpellHitTarget` and `UpdateAI` is self-contained or relies on base class functionality (`ScriptedAI`) for standard operations like timer management or unit validity checks. The member `m_lethonGuid` suggests a logical link to the `boss_lethonAI` unit (defined in the same header), but no direct method calls are recorded in the MAP for this partial.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory using runtime state (`m_uiDelay`, `m_lethonGuid`).

## Notable Implementation Details

1.  **Passive Entity Design**: The most notable aspect of `npc_spirit_shadeAI` is the explicit emptying of all combat-related hooks (`EnterCombat`, `AttackedBy`, `AttackStart`, `EnterEvadeMode`). This is a deliberate design choice to create a "non-entity" in terms of threat generation and retaliation. It allows the creature to exist in the combat zone without interfering with player threat meters or boss mechanics.
2.  **Linkage to Lethon**: The private member `ObjectGuid m_lethonGuid` strongly ties this AI to the `boss_lethonAI` instance. Although the MAP does not show direct calls, this GUID is likely set during summoning (in `Reset` or a constructor not detailed in the MAP's "calls out") to allow the Shade to track the status of its parent boss.
3.  **Minimal State**: The AI only tracks `m_uiDelay`. This suggests its behavior is time-based or event-based with a simple cooldown/duration mechanic, rather than complex state machines.

## Member Reference

**EnterCombat**
Overrides the base class method with an empty body. Prevents the creature from performing any actions upon entering combat.

**AttackedBy**
Overrides the base class method with an empty body. Prevents the creature from reacting to incoming attacks (e.g., no counter-attacks or threat generation).

**AttackStart**
Overrides the base class method with an empty body. Prevents the creature from initiating attacks on targets.

**EnterEvadeMode**
Overrides the base class method with an empty body. Prevents the creature from performing any cleanup or movement logic when leaving combat.

---

<!-- machine-true, projected from graph.json -->

## Map — npc_spirit_shadeAI

*Source:* boss_dragon_of_nightmare.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| EnterCombat | method | — | — | — |
| AttackedBy | method | — | — | — |
| AttackStart | method | — | — | — |
| EnterEvadeMode | method | — | — | — |
