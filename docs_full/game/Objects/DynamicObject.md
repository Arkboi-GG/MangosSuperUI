# DynamicObject

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DynamicObject

**Purpose & Responsibilities**

`DynamicObject` represents a transient, server-side entity that exists solely to visualize and manage the effects of spells in the game world. Unlike persistent entities like Players or Game Objects, a `DynamicObject` has no physical interaction volume (its bounding radius is zero) and typically disappears when its associated spell effect ends.

Its primary responsibilities are:
1.  **Visualization:** Rendering spell effects such as area-of-effect (AoE) circles, farsight portals, or channeling indicators at specific coordinates.
2.  **Area Effect Management:** For spells with a radius (e.g., `DYNAMIC_OBJECT_AREA_SPELL`), it tracks which units are within its area (`m_affected`) and facilitates the application or removal of spell auras on those units via the `DynamicObjectUpdater` notifier.
3.  **Lifecycle Coordination:** It synchronizes its existence with the casting `SpellCaster`. It handles deletion when its duration expires or when the caster removes it, ensuring that associated client-side animations and server-side state are cleaned up correctly.
4.  **Visibility Control:** It implements custom visibility rules, ensuring that the object is always visible to its caster and visible to others only within specific distance thresholds defined by the map and world settings.

This unit does not interact with any database tables; all state is held in memory during the object's lifetime.

## Member-by-Member Behavior

### Lifecycle and Initialization

*   **DynamicObject**: The constructor initializes the object as a `TYPEID_DYNAMICOBJECT`. It sets default values for internal state variables (`m_spellId`, `m_radius`, etc.) to zero or false. It configures update flags to ensure position data is sent to clients.
*   **Create**: This is the factory method for initializing a `DynamicObject`. It takes the caster, spell ID, effect index, coordinates, duration, radius, and type.
    *   It validates the position using `WorldObject.Object/IsPositionValid`. If invalid, it logs an error and returns false.
    *   It sets the object's entry to the spell ID and links the caster via `SetGuidValue`.
    *   It handles specific visual flags (`DYNAMICOBJECT_BYTES`). Notably, for spell ID 1543 ("Fusée éclairante"), it forces a specific byte value (`0x10`) and doubles the radius, likely to correct a visual discrepancy noted in the code comments.
    *   It retrieves the `SpellEntry` to determine if the effect is positive or channeled.
    *   It marks the object as active if it is a `DYNAMIC_OBJECT_FARSIGHT_FOCUS`.
*   **AddToWorld**: Registers the object in the map's object accessor for GUID lookup and calls the base `Object::AddToWorld`.
*   **RemoveFromWorld**: Erases the object from the map's accessor and notifies the viewpoint system via `ViewPoint/Event_RemovedFromWorld` before calling the base removal routine.
*   **Delete**: Sends a despawn animation to clients (`SendObjectDeSpawnAnim`) and adds the object to the global removal list (`AddObjectToRemoveList`). It is called by `SpellCaster/RemoveAllDynObjects` and `SpellCaster/RemoveDynObject`.

### State Accessors

*   **GetSpellId**: Returns the ID of the spell creating this object. Called by `SpellCaster` methods to identify the object.
*   **GetEffIndex**: Returns the specific effect index (0, 1, or 2) of the spell responsible for this object.
*   **GetDuration**: Returns the remaining alive duration in milliseconds.
*   **GetCasterGuid**: Returns the GUID of the object that cast the spell. Used by `WorldObject.Object/IsControlledByPlayer`.
*   **GetRadius**: Returns the effective radius of the area effect. Used by `Unit.SpellAuras/Update#3` to check if a unit is still within range.
*   **GetType**: Returns the `DynamicObjectType` (Portal, Area Spell, or Farsight Focus) derived from the stored byte value.
*   **IsChanneled**: Returns whether the underlying spell is a channeled spell.
*   **GetName**: Returns the string literal "DynamicObject".
*   **GetObjectBoundingRadius**: Overrides the base class to return `0.0f`, indicating the object has no physical collision or interaction size.
*   **GetGridRef**: Returns the grid reference used for spatial partitioning.
*   **GetFactionTemplateId**: Delegates to the caster's faction template ID.

### Caster Resolution

*   **GetCaster**: Resolves the `ObjectGuid` stored in the object to an actual `SpellCaster` pointer. It checks if the GUID is a Unit or a GameObject. If it's a GameObject, it retrieves the GameObject from the map. If the object is not found (e.g., deleted), it returns `nullptr`.
*   **GetUnitCaster**: Attempts to get a `Unit*` from the caster. If the caster is a `Unit`, it returns it directly. If the caster is a `GameObject`, it attempts to get the owner of that GameObject. This allows dynamic objects created by totems or other GOs to link back to the player who placed them.
*   **GetAffectingPlayer**: Wraps `GetUnitCaster` and casts the result to `Player*` using `Player.Main/ToPlayer`.

### Visibility and Interaction

*   **IsVisibleForInState**: Determines if the object is visible to a detector.
    *   It is always visible to its own caster.
    *   For others, it checks if the view point is within a calculated distance. This distance is the maximum of the map's visibility distance (plus a grey-out buffer if applicable) and the object's visibility modifier.
*   **IsHostileTo**: Delegates hostility checks to the caster. If the caster is missing, it defaults to `false`.
*   **IsFriendlyTo**: Delegates friendliness checks to the caster. If the caster is missing, it defaults to `true`.
*   **IsCharmerOrOwnerPlayerOrPlayerItself**: Returns `true` if the caster GUID is a player.

### Area Effect Management

*   **AddAffected**: Adds a unit's GUID to the `m_affected` map with a timestamp of 0. This tracks units currently inside the AoE.
*   **RemoveAffected**: Removes a unit's GUID from the `m_affected` map. Called by `Unit.SpellAuras/Update#3` and `Unit.SpellAuras/_RemoveSpellAuraHolder` when a unit leaves the area or the aura is removed.
*   **NeedsRefresh**: Checks if a unit needs to be re-evaluated for the area effect. It returns `true` if the unit is not in `m_affected` or if the last update was more than 2000ms ago.

### Updates and Timing

*   **Update**: The core logic loop called periodically.
    1.  Calls `WorldObject.Object/Update`.
    2.  Retrieves the caster. If the caster is gone, it deletes itself.
    3.  Decrements `m_aliveDuration`.
    4.  Determines if the object should be deleted (`deleteThis`). It deletes if duration is 0 AND (it's not channeled OR the caster is not currently channeling this specific object). This prevents premature deletion of channeled spells.
    5.  Increments timestamps in `m_affected`.
    6.  If `m_radius` is non-zero, it uses `MaNGOS::DynamicObjectUpdater` to visit all objects in the radius. This notifier applies or removes auras on units entering/leaving the area.
    7.  **Special Case**: For spell IDs 13812, 14314, and 14315 (Explosive Traps), it sets `m_radius` to 0 after the first update. This is a hack to ensure the trap effect only triggers once, preventing repeated applications.
    8.  If `deleteThis` is true, it asks the caster to remove the object and calls `Delete()`.
*   **Delay**: Reduces the object's remaining duration and delays the corresponding spell auras on all affected units.
    *   It iterates through `m_affected`.
    *   For each unit, it finds the `SpellAuraHolder`.
    *   It checks if the aura holder has other effects (indices > `m_effIndex`) that are also persistent area auras or farsights. If so, it skips delaying that specific aura instance to avoid disrupting linked effects.
    *   Otherwise, it calls `Unit.Main/DelaySpellAuraHolder` on the target.
    *   If a unit is no longer in the world, it is removed from `m_affected`.

## Cross-Unit Boundaries

*   **SpellCaster**:
    *   *Called By*: `SpellCaster/GetDynObject`, `SpellCaster/GetDynObjects`, `SpellCaster/RemoveDynObject`, `SpellCaster/RemoveAllDynObjects`.
    *   *Collaboration*: The `SpellCaster` creates `DynamicObject`s for visual effects. It queries them for ID and effect index to manage state. It explicitly removes them when the spell ends or is cancelled. `DynamicObject::Update` also calls back to `SpellCaster/RemoveDynObjectWithGUID` to clean up references.
*   **Unit / Player**:
    *   *Called By*: `Unit.SpellAuras/Update#3`, `Unit.SpellAuras/_RemoveSpellAuraHolder`, `Player.Main/SetLongSight`.
    *   *Collaboration*: Units interact with `DynamicObject`s primarily through area effects. `Unit.SpellAuras` checks `GetRadius` and `NeedsRefresh` to determine if auras should be applied or removed. `Player.Main/SetLongSight` creates farsight objects.
*   **WorldObject / Object**:
    *   *Calls Out*: `WorldObject.Object/GetMap`, `WorldObject.Object/GetPositionX/Y/Z`, `WorldObject.Object/IsPositionValid`, `WorldObject.Object/Relocate`, `WorldObject.Object/SetFloatValue`, `WorldObject.Object/SetUInt32Value`, `WorldObject.Object/_Create`, `Object/GetObjectGuid`, `Object/SetEntry`, `Object/SetGuidValue`, `Object/IsInWorld`, `Object/RemoveFromWorld`, `Object/AddToWorld`.
    *   *Collaboration*: Inherits standard object lifecycle, positioning, and data storage capabilities.
*   **Map / ObjectAccessor**:
    *   *Calls Out*: `Map.Main/GetGameObject`, `Map.Main/GetUnit`, `Map.Main/GetVisibilityDistance`, `ObjectAccessor/GetUnit`.
    *   *Collaboration*: Uses the Map to resolve GUIDs to actual objects (casters, targets) and to query visibility distances.
*   **SpellMgr / SpellEntry**:
    *   *Calls Out*: `SpellMgr/GetSpellEntry`, `SpellMgr/Instance`, `SpellEntry/IsChanneledSpell`, `SpellEntry/IsPositiveEffect`.
    *   *Collaboration*: Retrieves spell data to configure the dynamic object's behavior (duration, type, positivity).
*   **DynamicObjectUpdater**:
    *   *Calls Out*: `DynamicObjectUpdater/DynamicObjectUpdater`.
    *   *Collaboration*: Instantiates this notifier class during `Update` to process all units within the object's radius, applying or removing auras as necessary.
*   **ViewPoint**:
    *   *Calls Out*: `ViewPoint/Event_RemovedFromWorld`.
    *   *Collaboration*: Notifies the viewpoint system when the object is removed, likely for camera or visibility culling purposes.

## Data Model

This unit does not interact with any database tables. All state is ephemeral and stored in memory.

## Notable Implementation Details

*   **Explosive Trap Hack**: In `Update`, spell IDs 13812, 14314, and 14315 have their radius set to 0.0f after the first update cycle. This prevents the area effect from continuously triggering, effectively making it a one-time trigger despite being implemented as a persistent area aura.
*   **Fusée Éclairante Visual Fix**: In `Create`, spell ID 1543 has its `DYNAMICOBJECT_BYTES` forced to `0x10` and its radius doubled. This addresses a known visual bug where the flare's diameter was incorrect.
*   **Channeling Protection**: In `Update`, the object checks if the caster is currently channeling this specific object (`GetChannelObjectGuid()`). If so, it prevents deletion even if the duration hits zero. This ensures the visual effect persists until the channeling spell explicitly ends, avoiding flickering or premature disappearance.
*   **Delayed Aura Logic**: The `Delay` method carefully inspects the `SpellAuraHolder` to see if other effects in the same spell share the same persistent nature. If they do, it avoids delaying them, preserving the synchronization between multiple effects of the same spell.
*   **Missing Caster Handling**: `IsHostileTo` and `IsFriendlyTo` handle the case where the caster might have been deleted but the dynamic object hasn't been cleaned up yet. They default to `false` (hostile) and `true` (friendly) respectively, providing safe fallbacks.
*   **Zero Bounding Radius**: `GetObjectBoundingRadius` returns 0.0f. This is crucial for pathfinding and collision detection, ensuring that dynamic objects do not block movement or register as solid obstacles.

## Member Reference

*   **DynamicObject**: Constructor; initializes object type, update flags, and default member variables.
*   **AddToWorld**: Registers object in map accessor and calls base `AddToWorld`.
*   **GetSpellId**: Returns the spell ID associated with this object.
*   **RemoveFromWorld**: Unregisters object from map accessor, notifies viewpoint, and calls base `RemoveFromWorld`.
*   **GetEffIndex**: Returns the spell effect index (0-2) responsible for this object.
*   **GetDuration**: Returns the remaining duration in milliseconds.
*   **GetCasterGuid**: Returns the GUID of the caster.
*   **IsCharmerOrOwnerPlayerOrPlayerItself**: Returns true if the caster is a player.
*   **GetRadius**: Returns the effective radius of the area effect.
*   **GetType**: Returns the `DynamicObjectType` enum value.
*   **IsChanneled**: Returns true if the spell is channeled.
*   **Create**: Factory method; validates position, sets entry/guid/values, retrieves spell data, and initializes state. Handles special cases for spell 1543.
*   **GetName**: Returns "DynamicObject".
*   **GetObjectBoundingRadius**: Returns 0.0f, indicating no physical size.
*   **GetGridRef**: Returns the grid reference.
*   **GetCaster**: Resolves caster GUID to a `SpellCaster*` (Unit or GameObject).
*   **GetUnitCaster**: Resolves caster to a `Unit*`, handling GameObject owners.
*   **GetAffectingPlayer**: Casts the unit caster to `Player*`.
*   **GetFactionTemplateId**: Returns the caster's faction template ID.
*   **Update**: Core logic; decrements duration, manages deletion based on channeling status, updates affected units via `DynamicObjectUpdater`, and handles special one-time trigger logic for explosive traps.
*   **Delete**: Sends despawn animation and adds object to removal list.
*   **AddAffected**: Adds a unit to the `m_affected` map.
*   **RemoveAffected**: Removes a unit from the `m_affected` map.
*   **Delay**: Reduces duration and delays auras on affected units, skipping linked persistent effects.
*   **IsVisibleForInState**: Checks visibility based on caster ownership and distance thresholds.
*   **IsHostileTo**: Delegates to caster; defaults to false if caster is missing.
*   **IsFriendlyTo**: Delegates to caster; defaults to true if caster is missing.
*   **NeedsRefresh**: Checks if a unit needs re-evaluation based on presence in `m_affected` and timestamp age (>2000ms).

---

<!-- machine-true, projected from graph.json -->

## Map — DynamicObject

*Source:* DynamicObject.cpp, DynamicObject.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DynamicObject | ctor | WorldObject.Object/WorldObject | Player.Main/SetLongSight, Spell.Effects/EffectAddFarsight, Spell.Effects/EffectPersistentAA | — |
| AddToWorld | method | Object/AddToWorld, Object/GetObjectGuid, Object/IsInWorld, WorldObject.Object/GetMap | — | — |
| GetSpellId | method | — | SpellCaster/GetDynObject, SpellCaster/GetDynObject#2, SpellCaster/GetDynObjects, SpellCaster/RemoveDynObject | — |
| RemoveFromWorld | method | Object/GetObjectGuid, Object/IsInWorld, Object/RemoveFromWorld, ViewPoint/Event_RemovedFromWorld, WorldObject.Object/GetMap, WorldObject.Object/GetViewPoint | — | — |
| GetEffIndex | method | — | SpellCaster/GetDynObject, SpellCaster/GetDynObjects | — |
| GetDuration | method | — | — | — |
| GetCasterGuid | method | — | WorldObject.Object/IsControlledByPlayer | — |
| IsCharmerOrOwnerPlayerOrPlayerItself | method | — | — | — |
| GetRadius | method | — | Unit.SpellAuras/Update#3 | — |
| GetType | method | — | — | — |
| IsChanneled | method | — | — | — |
| Create | method | Log.Main/Out, Object/GetObjectGuid, Object/SetEntry, Object/SetGuidValue, SpellEntry/IsChanneledSpell, SpellEntry/IsPositiveEffect, SpellMgr/GetSpellEntry, SpellMgr/Instance, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/IsPositionValid, WorldObject.Object/Relocate#2, WorldObject.Object/SetFloatValue, WorldObject.Object/SetMap, WorldObject.Object/SetObjectScale, WorldObject.Object/SetUInt32Value, WorldObject.Object/_Create#2 | Player.Main/SetLongSight, Spell.Effects/EffectAddFarsight, Spell.Effects/EffectPersistentAA | — |
| GetName | method | — | — | — |
| GetObjectBoundingRadius | method | — | — | — |
| GetGridRef | method | — | — | — |
| GetCaster | method | Map.Main/GetGameObject, ObjectAccessor/GetUnit, ObjectGuid/IsEmpty, ObjectGuid/IsGameObject, ObjectGuid/IsUnit, WorldObject.Object/GetMap | — | — |
| GetUnitCaster | method | GameObject/GetOwner, Object/IsUnit, Object/ToGameObject | — | — |
| GetAffectingPlayer | method | Player.Main/ToPlayer | — | — |
| GetFactionTemplateId | method | WorldObject.Object/GetFactionTemplateId | — | — |
| Update | method | DynamicObjectUpdater/DynamicObjectUpdater, Object/GetObjectGuid, Object/IsUnit, ObjectGuid/operator!=, SpellCaster/RemoveDynObjectWithGUID, Unit.Main/GetChannelObjectGuid, WorldObject.Object/Update | — | — |
| Delete | method | WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/SendObjectDeSpawnAnim | SpellCaster/RemoveAllDynObjects, SpellCaster/RemoveDynObject | — |
| AddAffected | method | Object/GetObjectGuid | — | — |
| RemoveAffected | method | Object/GetObjectGuid | Unit.SpellAuras/Update#3, Unit.SpellAuras/_RemoveSpellAuraHolder | — |
| Delay | method | Map.Main/GetUnit, SpellAuraHolder/GetSpellProto, Unit.Main/DelaySpellAuraHolder, Unit.Main/GetSpellAuraHolder, WorldObject.Object/GetMap | Spell.Main/DelayedChannel | — |
| IsVisibleForInState | method | Map.Main/GetVisibilityDistance, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/operator==, World/GetVisibleObjectGreyDistance, WorldObject.Object/GetMap, WorldObject.Object/GetVisibilityModifier, WorldObject.Object/IsWithinDistInMap | — | — |
| IsHostileTo | method | WorldObject.Object/IsHostileTo | — | — |
| IsFriendlyTo | method | WorldObject.Object/IsFriendlyTo | — | — |
| NeedsRefresh | method | Object/GetObjectGuid | — | — |
