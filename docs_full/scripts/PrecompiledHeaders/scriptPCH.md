# scriptPCH

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# scriptPCH

## Purpose & Responsibilities

`scriptPCH.h` is a precompiled header (PCH) include file for the scripting subsystem of the `wowvmangos` server. Its sole responsibility is to aggregate a large set of core engine headers into a single inclusion point. By including `scriptPCH.h`, other translation units within the scripting system gain immediate access to the fundamental classes and managers required to implement creature AI, game object logic, instance scripts, gossip menus, and other dynamic server behaviors.

This unit contains no executable logic, no member functions, and no data structures. It serves exclusively as a compilation optimization mechanism, reducing build times by allowing the compiler to parse these common dependencies once and reuse the resulting precompiled state.

## Member-by-Member Behavior

There are no members in this unit. The file consists entirely of `#include` directives and header guards.

## Cross-Unit Boundaries

As a header-only utility file with no functions or methods, `scriptPCH` does not participate in runtime call graphs. It does not call into other units, nor is it "called" by them in the functional sense. However, it establishes a compile-time dependency relationship: any source file that includes `scriptPCH.h` implicitly depends on the interfaces defined in the headers listed below.

The following core engine components are exposed through this PCH:
*   **Entity Management:** `Object`, `Unit`, `Creature`, `GameObject`, `TemporarySummon`.
*   **AI Systems:** `CreatureAI`, `ScriptedAI`, `ScriptedPetAI`, `ScriptedFollowerAI`, `ScriptedEscortAI`, `NullCreatureAI`, `TotemAI`.
*   **Scripting Infrastructure:** `ScriptMgr`, `ScriptedInstance`, `ScriptedGossip`.
*   **World State & Events:** `World`, `Weather`, `GameEventMgr`.
*   **Combat & Spells:** `Spell`, `SpellAuras`.
*   **Utility & Search:** `GridSearchers`, `Mail`, `ObjectMgr`.
*   **Specialized Content:** `BattleGroundAV` (Alterac Valley battleground logic).

## Data Model

This unit interacts with no database tables. It is a pure C++ header file containing no SQL queries or data access logic.

## Notable Implementation Details

*   **Precompiled Header Optimization:** The filename `scriptPCH.h` and the macro `SC_PRECOMPILED_H` indicate this file is intended to be compiled into a PCH binary (e.g., `.gch` or `.pch`). This is a standard C++ build optimization technique.
*   **Broad Scope:** The includes span nearly all major subsystems relevant to scripting (AI, Gossip, Instances, Spells). This suggests that most custom scripts in the `wowvmangos` codebase will need access to these core entities, justifying their aggregation in a single PCH.
*   **Specific Battleground Inclusion:** The inclusion of `BattleGroundAV.h` is notable. While most battlegrounds might be handled generically or via specific scripts, Alterac Valley (AV) has enough unique scripted logic that its header is considered part of the common scripting foundation.
*   **No Conditional Includes:** All headers are included unconditionally. There are no `#ifdef` blocks within this file, meaning every script compilation unit using this PCH will parse all these headers regardless of whether they use them. This is typical for PCHs but requires careful maintenance to avoid unnecessary bloat.

## Member Reference

This unit contains no members.

---

<!-- machine-true, projected from graph.json -->

## Map — scriptPCH

*Source:* scriptPCH.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
