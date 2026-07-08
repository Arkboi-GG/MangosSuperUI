# blasted_lands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Architecture and Reference Documentation: `blasted_lands`

## Purpose & Responsibilities

The `blasted_lands` translation unit implements a specific game mechanic for the "Blasted Lands" zone within the World of Warcraft server emulation. Its sole responsibility is to handle the interaction logic for four specific Game Objects (GOs), collectively referred to as "Stones of Binding."

When a player interacts with one of these stones, the script identifies the corresponding "Servant" creature nearby and forces that creature to cast a specific spell (ID 12938) on itself. This likely represents a binding or summoning ritual mechanic inherent to the zone's lore or questlines. The unit acts as a bridge between the static world object (the stone) and the dynamic entity (the servant), triggering a state change via spellcasting.

## Member-by-Member Behavior

### Interaction Logic: `GOHello_go_stone_of_binding`

This function serves as the event handler for the "On Hello" (interaction) event triggered when a player clicks on one of the four supported Game Objects. It performs the following steps:

1.  **Identification**: It inspects the `GameObject` pointer (`pGo`) to determine its Entry ID using `Object::GetEntry`.
2.  **Target Resolution**: Based on the Entry ID, it uses a `switch` statement to identify which of the four servants is associated with this stone. It then searches for the nearest creature of the specific servant type within a 30.0 unit radius using `WorldObject::FindNearestCreature`.
    *   Stone 141812 $\rightarrow$ Servant of Razelikh (Entry 7668)
    *   Stone 141857 $\rightarrow$ Servant of Grol (Entry 7669)
    *   Stone 141858 $\rightarrow$ Servant of Allistarj (Entry 141858 maps to Servant 7670)
    *   Stone 141859 $\rightarrow$ Servant of Sevine (Entry 141859 maps to Servant 7671)
3.  **Action Execution**: If a valid creature is found, it instructs the creature to cast Spell ID 12938 on itself (`pCreature->CastSpell(pCreature, 12938, true)`). The third argument `true` indicates this is a triggered cast (likely bypassing some standard casting checks or cooldowns, depending on the engine's implementation of `CastSpell`).
4.  **Return Value**: It returns `false`. In the context of the ScriptDev2/MaNGOS framework, returning `false` typically indicates that the default handling should proceed or that the interaction did not consume the event in a way that prevents further processing, though often for GO hellos, the return value signifies whether the GO should remain open or reset. Given the lack of explicit reset logic here, it implies the action is instantaneous.

### Registration: `AddSC_blasted_lands`

This function registers the script with the server's scripting system. It allocates a new `Script` structure, assigns the name `"go_stone_of_binding"`, links the `GOHello_go_stone_of_binding` function to the `pGOHello` callback slot, and calls `Script::RegisterSelf` to make it active. This function is called by `ScriptLoader::AddScripts` during server startup.

## Cross-Unit Boundaries

*   **`Object::GetEntry`**: Called by `GOHello_go_stone_of_binding` to retrieve the unique identifier of the interacted GameObject. This allows the script to distinguish between the four different stones.
*   **`WorldObject::FindNearestCreature`**: Called by `GOHello_go_stone_of_binding` to locate the target NPC. This abstracts away the spatial query logic, relying on the core engine to find entities within the specified radius (30.0 units) and matching the specific Entry ID.
*   **`SpellCaster::CastSpell`**: Called by `GOHello_go_stone_of_binding` to trigger the effect. This delegates the complex logic of spell resolution, targeting, and visual effects to the core spell system.
*   **`Script::Script` / `Script::RegisterSelf`**: Called by `AddSC_blasted_lands` to integrate this custom logic into the global script manager.
*   **`ScriptLoader::AddScripts`**: Calls `AddSC_blasted_lands` to ensure this script is loaded when the server initializes.

## Data Model

This unit does not directly access any database tables. It relies entirely on runtime data structures (Game Objects and Creatures) that are presumably populated from the database during map loading, but no SQL queries or table references exist in this source file.

## Notable Implementation Details

*   **Hardcoded Associations**: The mapping between Stone Entry IDs and Servant Entry IDs is hardcoded in the `switch` statement. If the database entries for these objects change, this script will break unless updated.
*   **Radius Dependency**: The script assumes the Servant is always within 30.0 units of the Stone. If the Servant despawns, moves too far away, or is blocked by terrain (depending on how `FindNearestCreature` handles line-of-sight, though the `true` flag usually implies ignoreLOS or similar depending on engine version specifics, here it likely just means "find any"), the spell will not cast.
*   **No State Persistence**: There is no check to see if the spell has already been cast or if the servant is already bound. Repeated interactions will repeatedly attempt to cast the spell. Whether this is desirable depends on the design of Spell 12938 (e.g., if it has a long duration or is a one-time effect).
*   **Null Safety**: The code correctly checks `if (pCreature)` before attempting to cast the spell, preventing a crash if no servant is found.

## Member Reference

**GOHello_go_stone_of_binding**
Handles the player interaction with the "Stone of Binding" Game Objects. It determines the specific stone type via its Entry ID, finds the corresponding Servant creature within 30 units, and casts Spell 12938 on that servant. Returns `false`.

**AddSC_blasted_lands**
Registers the `go_stone_of_binding` script with the server's script manager by creating a `Script` instance, assigning the `GOHello` callback, and calling `RegisterSelf`.

---

<!-- machine-true, projected from graph.json -->

## Map — blasted_lands

*Source:* blasted_lands.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GOHello_go_stone_of_binding | function | Object/GetEntry, SpellCaster/CastSpell#2, WorldObject.Object/FindNearestCreature | — | — |
| AddSC_blasted_lands | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
