# FactionTemplateEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# FactionTemplateEntry

`FactionTemplateEntry` is a data structure within `DBCSstructure.h` that represents a row from the game's `FactionTemplate.dbc` file. It defines the complex relationship rules between factions, determining whether entities (creatures, players, game objects) are friendly, hostile, or neutral toward one another. Unlike simple binary relationships, this structure supports layered logic involving specific faction IDs, bitmask-based team affiliations, and special flags for contested zones.

The primary responsibility of `FactionTemplateEntry` is to provide boolean queries that resolve these relationships at runtime. It does not store mutable state beyond a single derived flag (`isEnemyOfAnother`) and relies entirely on its member variables (`enemyFaction`, `friendFaction`, `hostileMask`, etc.) to compute results. It is a passive data holder used extensively by combat AI, spell effects, and network update builders to determine interaction permissions.

## Member-by-Member Behavior

The methods in `FactionTemplateEntry` are grouped by the type of relationship query they perform. All methods are `const` and operate on two `FactionTemplateEntry` instances: the implicit `this` object and an explicit `entry` parameter (except for global checks like `IsHostileToPlayers`).

### Direct Faction Relationship Checks

**IsFriendlyTo**
Determines if `this` faction template is friendly with the `entry` faction template. The logic follows a strict priority order:
1.  **Explicit Enemies:** If `entry.faction` is non-zero, it iterates through `this->enemyFaction`. If any entry matches `entry.faction`, it returns `false` immediately. Explicit enmity overrides all other rules.
2.  **Explicit Friends:** If `entry.faction` is non-zero, it iterates through `this->friendFaction`. If any entry matches `entry.faction`, it returns `true`. Explicit friendship overrides mask-based logic.
3.  **Mask-Based Friendship:** If no explicit match is found, it checks bitwise intersections. It returns `true` if `this->friendlyMask` intersects with `entry.ourMask` OR if `this->ourMask` intersects with `entry.friendlyMask`. This allows for symmetric or asymmetric team-based friendships.
4.  **Default:** Returns `false` if none of the above conditions are met.

**IsHostileTo**
Determines if `this` faction template is hostile with the `entry` faction template. Similar to `IsFriendlyTo`, it prioritizes explicit lists over masks:
1.  **Explicit Enemies:** If `entry.faction` is non-zero, it checks `this->enemyFaction`. If a match is found, it returns `true`.
2.  **Explicit Friends:** If `entry.faction` is non-zero, it checks `this->friendFaction`. If a match is found, it returns `false`. Explicit friendship prevents hostility.
3.  **Mask-Based Hostility:** If no explicit match, it returns `true` if `this->hostileMask` intersects with `entry.ourMask`. Note that unlike friendship, this check is not symmetric in the code (it does not check `this->ourMask` against `entry->hostileMask`).

### Player and Team-Specific Checks

**IsHostileToPlayerTeam**
Checks if `this` faction is hostile toward the player teams (Alliance or Horde). It performs a bitwise AND between the hostility masks and the combined `FACTION_MASK_ALLIANCE | FACTION_MASK_HORDE`. It checks both directions:
1.  `this->hostileMask` against `entry.ourMask` (filtered for player teams).
2.  `this->ourMask` against `entry.hostileMask` (filtered for player teams).
This ensures that if either side marks the other as hostile within the context of player factions, the result is true.

**IsHostileToPlayers**
A simpler check that determines if `this` faction is inherently hostile to *any* player. It checks if `this->hostileMask` has the `FACTION_MASK_PLAYER` bit set. This is used to quickly filter out NPCs that should never engage players in combat regardless of specific faction alignment.

**IsNeutralToAll**
Determines if the faction is completely neutral. It returns `true` only if:
1.  None of the entries in `enemyFaction` are non-zero.
2.  `hostileMask` is zero.
3.  `friendlyMask` is zero.
If any hostility or explicit friendship exists, it is not neutral.

### Special Flags

**IsContestedGuardFaction**
Checks if the `factionFlags` field contains the `FACTION_TEMPLATE_FLAG_ATTACK_PVP_ACTIVE_PLAYERS` bit. This flag typically designates guards in contested zones who attack players actively engaging in PvP, regardless of their usual faction alignment.

**HasFactionFlag**
A generic utility that checks if a specific `flag` is set in `this->factionFlags`.

## Cross-Unit Boundaries

`FactionTemplateEntry` is a leaf node in the call graph; it does not call out to other units. However, it is heavily depended upon by core gameplay systems.

*   **CombatBotBaseAI/IsValidDispelTarget**: Uses `IsFriendlyTo` to determine if a target is friendly enough to be dispelled.
*   **GameObject/IsFriendlyTo** & **GameObject/IsHostileTo**: Delegates to `FactionTemplateEntry` to resolve object-specific faction interactions.
*   **Spell.Effects/EffectDispel**: Uses `IsFriendlyTo` to validate dispel targets.
*   **Spell.Main/CheckCast**: Uses `IsFriendlyTo` during spell casting validation.
*   **SpellEntry/IsPositiveEffect**: Uses `IsFriendlyTo` to determine if a spell effect is beneficial based on faction alignment.
*   **WorldObject.Object/BuildValuesUpdate**: Uses `IsFriendlyTo` to construct network updates for object visibility/reaction.
*   **WorldObject.Object/GetFactionReactionTo**: Uses `IsFriendlyTo`, `IsHostileTo`, and `IsContestedGuardFaction` to calculate the final reaction enum sent to the client.
*   **AiBotAI.Movement/IsPathSafe**: Uses `IsHostileTo` to determine if a path leads through hostile territory.
*   **WorldSession.MiscHandler/HandleSetSelectionOpcode**: Uses `IsHostileToPlayerTeam` to handle player selection changes.
*   **Unit.Main/IsHostileToPlayers**, **IsNeutralToAll**, **IsContestedGuard**: Delegate directly to the corresponding `FactionTemplateEntry` methods.
*   **Unit.Main/SetFactionTemplateId**: Uses `HasFactionFlag` to apply special behaviors when a unit's faction template is changed.

## Data Model

`FactionTemplateEntry` maps directly to the `FactionTemplate.dbc` file. It does not interact with SQL tables. The structure reflects the columns of this DBC file:

*   `ID`: The unique identifier for the faction template.
*   `faction`: The base faction ID associated with this template.
*   `factionFlags`: Bitmask for special behaviors (e.g., contested guard).
*   `ourMask`: Bitmask representing the team(s) this faction belongs to.
*   `friendlyMask`: Bitmask representing teams/factions this faction is friendly with.
*   `hostileMask`: Bitmask representing teams/factions this faction is hostile with.
*   `enemyFaction[4]`: Array of up to 4 specific faction IDs that are explicitly enemies.
*   `friendFaction[4]`: Array of up to 4 specific faction IDs that are explicitly friends.

## Notable Implementation Details

*   **Explicit Overrides Masks:** In both `IsFriendlyTo` and `IsHostileTo`, explicit entries in `enemyFaction` and `friendFaction` take precedence over mask-based calculations. This allows fine-grained control where a faction might generally be friendly with a team (via mask) but hostile to a specific sub-faction (via explicit ID).
*   **Asymmetric Hostility Check:** `IsHostileTo` only checks `this->hostileMask` against `entry.ourMask`. It does *not* check `this->ourMask` against `entry.hostileMask`. This implies that hostility defined by masks is directional unless both sides define it. In contrast, `IsHostileToPlayerTeam` *does* check both directions.
*   **Zero Faction Handling:** The checks for explicit friends/enemies only proceed if `entry.faction` is non-zero. If `entry.faction` is 0, it skips the array checks and falls back to mask logic. This handles cases where a template might not have a direct base faction ID.
*   **Neutral Definition:** `IsNeutralToAll` requires *both* `hostileMask` and `friendlyMask` to be zero, and no explicit enemies. This is a strict definition of neutrality; a faction with only friendly masks is not considered "neutral to all."
*   **Core-Assigned Flag:** The member `isEnemyOfAnother` is marked as "assigned by core." It is not computed by the methods in this struct but is likely set during DBC loading or initialization elsewhere to optimize repeated checks.

## Member Reference

**IsFriendlyTo**
Returns `true` if `this` is friendly with `entry`. Prioritizes explicit `enemyFaction` (returns `false`) and `friendFaction` (returns `true`) lists. Falls back to checking if `friendlyMask` intersects with `entry.ourMask` or vice versa.

**IsHostileTo**
Returns `true` if `this` is hostile with `entry`. Prioritizes explicit `enemyFaction` (returns `true`) and `friendFaction` (returns `false`) lists. Falls back to checking if `this->hostileMask` intersects with `entry.ourMask`.

**IsHostileToPlayerTeam**
Returns `true` if `this` is hostile to Alliance or Horde players. Checks both `this->hostileMask` vs `entry.ourMask` and `this->ourMask` vs `entry.hostileMask`, filtered by `FACTION_MASK_ALLIANCE | FACTION_MASK_HORDE`.

**IsHostileToPlayers**
Returns `true` if `this->hostileMask` includes the `FACTION_MASK_PLAYER` bit, indicating inherent hostility to all players.

**IsNeutralToAll**
Returns `true` if `enemyFaction` contains no non-zero entries, and both `hostileMask` and `friendlyMask` are zero.

**IsContestedGuardFaction**
Returns `true` if `factionFlags` includes `FACTION_TEMPLATE_FLAG_ATTACK_PVP_ACTIVE_PLAYERS`.

**HasFactionFlag**
Returns `true` if the specified `flag` is set in `factionFlags`.

---

<!-- machine-true, projected from graph.json -->

## Map — FactionTemplateEntry

*Source:* DBCStructure.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IsFriendlyTo | method | — | CombatBotBaseAI/IsValidDispelTarget, GameObject/IsFriendlyTo, Spell.Effects/EffectDispel, Spell.Main/CheckCast, SpellEntry/IsPositiveEffect, WorldObject.Object/BuildValuesUpdate, WorldObject.Object/GetFactionReactionTo | — |
| IsHostileTo | method | — | AiBotAI.Movement/IsPathSafe, GameObject/IsHostileTo, WorldObject.Object/GetFactionReactionTo | — |
| IsHostileToPlayerTeam | method | — | WorldSession.MiscHandler/HandleSetSelectionOpcode | — |
| IsHostileToPlayers | method | — | Unit.Main/IsHostileToPlayers | — |
| IsNeutralToAll | method | — | Unit.Main/IsNeutralToAll | — |
| IsContestedGuardFaction | method | — | Unit.Main/IsContestedGuard, WorldObject.Object/GetFactionReactionTo | — |
| HasFactionFlag | method | — | Unit.Main/SetFactionTemplateId | — |
