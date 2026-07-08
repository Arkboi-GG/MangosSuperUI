# Player.Main

<!-- documentation: hand-written from Player.cpp/Player.h by the maintainer -->

# Player.Main

## Purpose & Responsibilities

`Player.Main` is the primary partial of the `Player` class — the server-side representation of a connected (or bot-driven) player character. It is the Player.cpp translation unit paired with the shared declaring header Player.h; the character's stat-recalculation slice lives in the sibling partial `Player.StatSystem` (StatSystem.cpp). With just over nine hundred members, this unit owns nearly everything a character *is* and *does* outside of combat math:

1. **Lifecycle** — construction from a `WorldSession`, creation of brand-new characters (`Create`, `SaveNewPlayer`), the per-tick `Update` heartbeat, disconnect handling, and destruction/cleanup.
2. **Persistence** — the entire load pipeline from the `characters` database at login (`LoadFromDB` plus the `_Load*` family fed by a `SqlQueryHolder`), the periodic and forced save pipeline (`SaveToDB` plus the `_Save*` family, all inside one transaction), and character deletion with soft-delete support (`DeleteFromDB`, `DeleteOldCharacters`).
3. **Inventory & items** — the vanilla slot-position model (equipment, backpack, bags, bank, buyback, keyring) with the full `CanStore*`/`Store*`/`Equip*`/`Destroy*`/`Swap*`/`Split*` state machine, enchant and duration bookkeeping, durability, vendors and buyback.
4. **Quests** — the 20-slot quest log, every `SatisfyQuest*` prerequisite check, accept/complete/reward/fail flows, and kill/item/cast/talk objective credit.
5. **World presence** — near and far teleportation (with the delayed-teleport semaphore machinery), zone/area updates, exploration, visibility lists, corpse/ghost/resurrection, taxi flights, transports, instance binding, and the dynamic continent-instance switching this fork uses for elastic world sharding.
6. **Progression & social** — XP and leveling, talents, spells and spell modifiers, cooldowns, skills, reputation and honor delegation (via `ReputationMgr`/`HonorMgr`), money with audit logging, groups, guilds, duels, PvP flagging, battleground queueing, chat channels, and GM/cheat tooling.

The unit is also the C++ anchor for this project's bot fleet: `IsBot`, the `PlayerAI` hooks (`AI`, `SetAI`, `RemoveTemporaryAI`, `SetControlledBy`), `SetSaveDisabled`, and `SelectRandomAppearance` exist so `PlayerBotAI/SpawnNewPlayer` and the party/combat bot layers can drive full `Player` objects through the same code paths as real clients.

Because header-inline members whose owner class matches the translation unit are attributed here, the member list spans both the heavyweight .cpp logic and the hundreds of one-line accessors defined inline in Player.h. A handful of members of *other* classes defined inside Player.cpp are attributed to this unit as well (see Notable Implementation Details): the four `Log::Player` logging overloads with their `PlayerLogHeaderToConsole`/`PlayerLogHeaderToFile`/`IsPlayerLoggingEnabledToDB`/`PlayerLogToDB` helpers, `ItemPosCount::isContainedIn`, the file-local GM visibility functors `SetGameMasterOnHelper`/`SetGameMasterOffHelper`, and the `DoPlayerLearnSpell` functor.

## Member-by-Member Behavior

### Construction & Character Creation

**Player#5**
The constructor. Binds the character to its `WorldSession`, registers the owned subsystems (`ReputationMgr`, `HonorMgr`, `Camera`, a `PlayerMenu` in `PlayerTalkClass`), clears honor data, pulls the last ticket counter from `TicketMgr`, seeds anticheat data through `WorldSession.Main/InitCheatData`, and initializes every timer, flag, and default from `World/getConfig`.

**~Player**
Destructor. Cancels any scheduled far teleport via `MapManager/CancelDelayedPlayerTeleport`, leaves all dungeon persistent states, and frees owned items, spell mods, and the duel record.

**CleanupsBeforeDelete**
Pre-removal pass run before the object leaves the world: cancels any open trade and completes an active duel, then chains to `Unit.Main/CleanupsBeforeDelete`. Invoked by `Map.Main/Remove#3` and `WorldSession.Main/LogoutPlayer`.

**Create**
Initializes a brand-new in-memory character that was never loaded from the database: race/class `PlayerInfo` from `ObjectMgr`, start position and map (`MapManager/CreateMap`), display IDs, base stats and power type, starting level/money from config. Max health/power recalculation is delegated across the partial boundary to `Player.StatSystem/UpdateMaxHealth#2` and `UpdateMaxPower#2`.

**SaveNewPlayer**
The fast character-creation path, also used by the bot spawner: writes a complete new `characters` row directly (REPLACE with starting level, money, position, taxi mask, health/powers, and starting gear serialized into `equipment_cache`) plus starting `character_spell`, `character_inventory`/`item_instance`, and `character_skills` rows — one transaction, no `Player` object ever constructed.

**BuildEnumData**
Static character-select renderer: converts one `characters` row (plus the `equipment_cache` blob and pet display data) into an `SMSG_CHAR_ENUM` entry, so the character list never joins `item_instance`.

**ValidateAppearance**
Static DBC check over `CharSections`/`CharacterFacialHairStyles` that rejects invalid race/gender/hair/face/skin combinations at character creation.

**SelectRandomAppearance**
Static roll of a random valid appearance from the `CharSections` DBC data (out-params for hair, hair color, face, facial hair, skin) — built for `PlayerBotAI/SpawnNewPlayer`.

**AddStartingItems**
Grants the race/class starting kit from PlayerInfo; also the entry point for bot auto-gearing via `CombatBotBaseAI/AutoEquipGear`.

**StoreNewItemInBestSlots**
Creates a new item by id and equips it if an equipment slot fits, else stores it in inventory — the premade/bot gearing primitive (optional enchant id).

**StoreNewItemInInventorySlot**
Forces a new item stack by id into the first free backpack slot, returning the created `Item*`.

**SatisfyItemRequirements**
Force-raises honor rank and/or reputation so a premade item's wear requirements are met — used when cloning or applying premade gear templates.

### Update Loop, Session & AI Hooks

**Update**
The per-tick heartbeat, called by the map. In order it services:

*   **Mirror timers** (`UpdateMirrorTimers`) — breath, fatigue, feign death, environmental.
*   **The delayed-teleport window** — `SetCanDelayTeleport(true)` around `Unit::Update` and the `PlayerAI` update, so a teleport triggered mid-spell-tick is parked instead of executed.
*   **PvP & duels** — PvP/contested flag timers, duel deletion, `UpdateDuelFlag`, `CheckDuelDistance`.
*   **Periodic detection & decay** — stealth detection every 2 s, item duration decay, deferred camera (`PLAYER_FARSIGHT`) writes.
*   **Quests & combat state** — timed-quest countdowns into `FailQuest`, melee attacking state.
*   **Rest & zones** — inn rest accrual every 10 s (`ComputeRest`), zone/area re-detection on a 1 s timer into `UpdateZone`/`UpdateArea`.
*   **Vitals & death** — `RegenerateAll` while alive, `JUST_DIED` → `KillPlayer`, the corpse-release countdown into `BuildPlayerRepop` + `ScheduleRepopAtGraveyard` (skipped in instances on post-1.10.2 builds, matching the 1.11 release-timer removal).
*   **Persistence & housekeeping** — the autosave countdown into `SaveToDB`, played-time accrual, sobering, enchant/homebind timers, cinematics, group out-of-range updates.
*   **Deferred teleports & sharding** — executing a parked delayed teleport; polling `MapManager/GetContinentInstanceId` and scheduling an instance switch when crossing a continent-shard boundary (deferred while in combat at a transition).
*   **Anticheat** — the movement-anticheat `Update` with sanctioning through the session, plus the deferred graveyard repop and area-check timer (`UpdateTerainEnvironmentFlags`, `CheckAreaExploreAndOutdoor`, cell preload).

**OnDisconnected**
Client-drop handling: resolves pending movement changes, snaps to the last client-confirmed position (**RelocateToLastClientPosition**), and clears movement-dependent states so the character does not keep gliding.

**AI**
Returns the currently installed `PlayerAI*` (null for a plain client-driven player).

**SetAI**
Installs a `PlayerAI*` into the AI slot; ownership conventions are the caller's responsibility.

**RemoveAI**
Deletes and clears the installed AI outright.

**RemoveTemporaryAI**
Restores the bot's own brain after a temporary AI (fear/charm) ends — deliberately never deletes a bot's `PlayerBotAI`, only the temporary controller.

**SetControlledBy**
Swaps in a `PlayerControlledAI` bound to the controlling unit for mind control, with the same bot-brain protection as `RemoveTemporaryAI`.

**SendInitialPacketsBeforeAddToMap**
First half of the world-entry packet burst, sent before map insertion: tutorials, spells, action buttons, initial spells and world states.

**SendInitialPacketsAfterAddToMap**
Second half of the world-entry burst, sent after map insertion: auras, movement flags, and state that requires the player to exist on the map.

**SendPacketsAtRelogin**
Reduced re-send subset used for a same-session relog (far teleport within one session) rather than a full login.

**GetSession**
Returns the owning `WorldSession*`.

**SetSession**
Rebinds the player to a different `WorldSession` — the bot-handover path.

**IsBot**
True when the session carries a `PlayerBotEntry` — the bot discriminator used across the whole file.

**AddSkippedUpdateTime**
Accumulates map-scheduler time this player's update skipped, to be consumed on the next `Update` tick.

**GetSkippedUpdateTime**
Returns the accumulated skipped-update time.

**ResetSkippedUpdateTime**
Zeroes the skipped-update accumulator.

**GetTotalPlayedTime**
Returns total played seconds (`PLAYED_TIME_TOTAL`).

**GetLevelPlayedTime**
Returns seconds played on the current level (`PLAYED_TIME_LEVEL`).

### Death, Corpse & Resurrection

**SetDeathState**
The Player override around the Unit transition. On `JUST_DIED` it clears drunkenness and combo points, drops any resurrect request, removes shapeshift before stat teardown, stables the pet with reagents, releases loot, preserves the self-resurrection spell id (falling back to **SelectResurrectionSpellId** for Soulstone/Reincarnation), and notifies the zone script.

**KillPlayer**
Driven from `Update` when death lands: switches to `CORPSE` state, arms the 6-minute release timer, applies the release-timer UI byte only on non-instanceable maps, notifies the anticheat, and recomputes the corpse reclaim delay.

**BuildPlayerRepop**
Spirit release: creates the corpse, places it on the map, applies ghost form, drops health to 1, and moves the player to `DEAD` state.

**CreateCorpse**
Builds the corpse object — appearance bytes, per-slot equipment display info, guild id, PvP/BG lootability flags — persisting it unless saving is disabled; first converts any leftover corpse to bones to guarantee uniqueness.

**RepopAtGraveyard**
Teleports the ghost (or a living player standing in a death zone) to the closest graveyard — battleground graveyards come from the BG script, otherwise `ObjectMgr/GetClosestGraveYard` — re-orienting toward the spirit healer on 1.8+ builds, and self-requeuing as the teleport `recover` callback on BG maps.

**ScheduleRepopAtGraveyard**
Deferred form of `RepopAtGraveyard` that waits until pending movement changes resolve before firing.

**ResurrectPlayer**
Returns to life: `ALIVE` state, ghost form removed, percentage health/mana/energy restore, `UpdateZone` re-run, camera/visibility refresh, and optional resurrection sickness scaled by level (one minute per level above the configured start, full ten minutes at 20+).

**ResurrectUsingRequestData**
Consumes a stored resurrect offer (**SetResurrectRequestData**, **ClearResurrectRequestData**, **IsRessurectRequestedBy**, **IsRessurectRequested**, **GetResurrector**), teleporting cross-map first when the caster was elsewhere.

**SpawnCorpseBones**
Converts the player's corpse to bones via the object accessor (loot expiry path).

**GetCorpse**
Looks up the player's corpse object through the object accessor.

**GetCorpseReclaimDelay**
Returns the corpse reclaim delay for the death type — the escalating 30/60/120 s PvP-death ladder tracked in `m_deathExpireTime`.

**UpdateCorpseReclaimDelay**
Advances the PvP-death reclaim ladder in `m_deathExpireTime` on each PvP death.

**SendCorpseReclaimDelay**
Sends the remaining reclaim countdown to the client (also used at load).

**GetDeathTimer**
Returns the remaining forced-release countdown (`m_deathTimer`).

**ApplyGhostForm**
Applies ghost visuals, ghost speed, and water-walk on spirit release.

**RemoveGhostForm**
Strips the ghost visuals/speed/water-walk at resurrect.

### Teleportation & Location

**TeleportTo**
The single entry point for all teleports. It validates coordinates, refuses BG maps without a BG id, releases loot, completes a duel as fled when leaving the map, and strips movement flags and channeled spells. Then it splits:

*   **Near (same map):** executes immediately — pet range check, optional combat stop, `MSG_MOVE_TELEPORT_ACK` round-trip armed via `MovementPacketSender`, the `recover` closure stored in case the client never acks — unless the delayed-teleport window is open, in which case the destination is parked for `Update`.
*   **Far (cross map):** revives a dead player whose corpse is on the target instance map, checks `MapManager/CanPlayerEnter` and instance-bind rights, resolves the continent instance id, and schedules a `ScheduledTeleportData` through `MapManager/ScheduleFarTeleport` — never removing the player mid-map-update.

**ExecuteTeleportFar**
The scheduled half, run by the map manager between updates: re-checks entry rights, stops combat, clears the summon-exploit window, leaves a wrong-map battleground, unsummons the pet, removes dynamic objects and world-leave auras, sends `SMSG_TRANSFER_PENDING`, removes the player from the old map, clears hostile refs (no cross-thread references), raises the far-teleport semaphore, and schedules `SendNewWorld`.

**SetCanDelayTeleport**
Marks the code region where a teleport request must be deferred instead of executed (set around handler bodies that cannot relocate mid-call).

**IsHasDelayedTeleport**
True when a deferred teleport is armed — deliberately refuses to fire for a player who died after arming, so a ghost is never yanked back from the graveyard.

**SetDelayedTeleportFlagIfCan**
Arms the delayed-teleport flag if the current region allows deferral; returns whether it armed.

**ExecuteTeleportNear**
Executes the deferred near teleport once the deferral window closes.

**ProcessDelayedOperations**
Runs the queued delayed operations — save, resurrect, spirit-heal aura, BG deserter and honorless-target casts, taxi resume — once a far teleport lands.

**ScheduleDelayedOperation**
Queues a `DelayedOperations` bit to run at far-teleport completion.

**RemoveDelayedOperation**
Clears a queued delayed-operation bit.

**IsBeingTeleported**
True while either teleport semaphore or a pending far teleport is active.

**IsBeingTeleportedNear**
True while the near-teleport semaphore is held.

**IsBeingTeleportedFar**
True while the far-teleport semaphore is held.

**SetSemaphoreTeleportNear**
Sets/clears the near-teleport semaphore.

**SetSemaphoreTeleportFar**
Sets/clears the far-teleport semaphore.

**SetPendingFarTeleport**
Flags a far teleport as accepted but not yet executed (`m_pendingFarTeleport`).

**GetTeleportDest**
Returns the armed teleport destination (`m_teleportDest`).

**RestorePendingTeleport**
Restores the pre-teleport location when a destination refuses entry mid-flight.

**TeleportToBGEntryPoint**
Convenience far teleport back to the stored battleground entry point.

**TeleportToHomebind**
Convenience far teleport to the homebind location (optionally triggering the hearth cooldown).

**SwitchInstance**
Same-map continent-instance hop — the elastic world-sharding path; rebinds the player to another instance id of the same map.

**SendNewWorld**
Sends `SMSG_NEW_WORLD` to complete a far teleport client-side.

**HandleReturnOnTeleportFail**
Puts the player back at the pre-teleport `WorldLocation` when the destination map rejects entry.

**UpdateZone**
The zone transition. On a real zone change it fires zone-script leave/enter, initial world states, and weather; it then:

*   Recomputes PvP enforcement from the zone's team (any hostile zone on PvP realms, enemy capitals everywhere) and flags PvP accordingly.
*   Toggles FFA state on FFA realms and sets city rest in capitals.
*   Destroys zone-limited items (alive players only past 1.6), re-homes built-in local chat channels (not while on a taxi), flags the group update, and reapplies zone-dependent auras.

**UpdateArea**
The finer-grained area pass under a zone: arena FFA flagging and tavern rest detection.

**CheckAreaExploreAndOutdoor**
Outdoor detection (transport-aware) with outdoors-only aura reconciliation under VMAP indoor checking, tavern-exit detection, and explored-bit setting with level-scaled exploration XP (`SendExplorationExperience`).

**UpdateTerainEnvironmentFlags**
Terrain-driven refresh of the liquid/underwater environment flags (name preserves the upstream 1.12 typo).

**SetPosition**
In-map relocation with zone/area re-check and visibility update; the `teleport` flag distinguishes an instant move from normal movement.

**GetCachedZoneId**
Returns the zone id cached by the last zone update (`m_zoneUpdateId`).

**GetCachedAreaId**
Returns the area id cached by the last area update (`m_areaUpdateId`).

**AddToWorld**
Grid insertion hook — reunites the player with a corpse on the same map and registers the packet broadcaster.

**RemoveFromWorld**
Grid removal hook — unregisters the broadcaster and detaches map-bound state.

**GetGridRef**
Returns the grid reference link used by the grid container.

**GetMapRef**
Returns the map reference link used by the map's player list.

**SetHomebindToLocation**
Sets the homebind location and area, persisting it to `character_homebind`.

**RelocateToHomebind**
Instantly relocates the in-memory position to the homebind point (map id + coordinates), without a teleport flow.

**GetHomeBindMap**
Returns the homebind map id.

**GetHomeBindAreaId**
Returns the homebind area id.

**SetBindPoint**
Sends the innkeeper bind-confirmation dialog for the given binder guid.

**SaveRecallPosition**
Stores the current map/position as the `.recall` point.

**GetRecallPosition**
Reads back the stored recall map and coordinates.

**SaveNoUndermapPosition**
Stores the last known safe position for the anti-undermap system.

**UndermapRecall**
Near-teleports back to the stored safe position (within 100 yd) when the player falls through the world; returns whether it recovered.

**HandleFall**
Applies fall damage from the recorded fall-start height, safe-fall and feather-fall aware.

**UpdateFallInformationIfNeed**
Refreshes the recorded fall-start height from movement info when the opcode/state warrants it.

**SetFallInformation**
Records the fall-start Z used by `HandleFall`.

**IsFalling**
True while a fall-start height is recorded.

**FallGround**
Forces the player to land (anticheat/death paths), by fall mode.

**IsLaunched**
True while in knockback flight.

**SetLaunched**
Sets/clears the knockback-flight state.

**SendSummonRequest**
Sends the 2-minute summon offer from a summoner, storing the target point.

**SetSummonPoint**
Stores the summon destination with its expiry window.

**SummonIfPossible**
Executes the accepted summon at answer time, re-checking expiry and battleground restrictions.

**SetMover**
Sets which unit the client steers (mind control/possess); null resets to self.

**GetMover**
Returns the current mover unit (never null — defaults to self).

**IsSelfMover**
True when the player steers itself (the normal case).

**GetConfirmedMover**
Returns the mover the client has actually confirmed via opcode, which may be null during handover.

**IsControlledByOwnClient**
True when the session's client-mover guid is this player.

**SetClientControl**
Grants or revokes client movement control over a target unit.

**HasMovementFlag**
Script access to `m_movementInfo` movement flags.

**SetTransport**
Transport boarding bookkeeping override — updates the transport link and boarding state.

**DismountCheck**
Forces a dismount where mounts are disallowed (indoor/transport rules).

**SetJustBoarded**
Marks the just-boarded state used by the 1.12 client transport-refresh workaround.

**HasJustBoarded**
Reads the just-boarded flag.

**IsOutdoorOnTransport**
Answers the outdoor question while aboard a transport (deck vs interior).

### XP, Leveling & Talents

**GiveXP**
Applies the personal XP-rate multiplier, honors the 1.8+ anti-addiction play-time flags (no XP / half XP), gates trial accounts at the trial level cap, adds the rested kill bonus (**GetXPRestBonus** consumes stored rest at 2×), logs via **SendLogXPGain**, and loops **GiveLevel** while the total crosses thresholds.

**GiveLevel**
The level-up transaction:

*   Writes a detailed character-log line including group and instance rosters, and raises a realm GM alert when the instance holds more players than the group — the mob-tag power-leveling heuristic.
*   Drops the player from now-invalid battleground queue brackets.
*   Recomputes base health/mana from `PlayerLevelInfo`/`PlayerClassLevelInfo` (stamina/intellect conversion via `GetHealthBonusFromStamina`/`GetManaBonusFromIntellect`, implemented in Player.StatSystem) and sends `SMSG_LEVELUP_INFO`.
*   Resets level played time, raises skill caps (**UpdateSkillsForLevel**), stores new create-stats, re-inits talents, triggers a full `UpdateAllStats` (Player.StatSystem), tops off health/mana/energy, and syncs the pet's level.

**InitStatsForLevel**
Resets every unit field to clean level baselines (used at login and `.levelup`); optionally reapplies mods around the reset.

**UpdateSkillsToMaxSkillsForLevel**
GM-leveling helper that maxes all capped-type skills to the level ceiling.

**LearnTalent**
Teaches a talent rank after validating the talent id, rank, tree, spent-points-in-tree requirement, and prerequisite rank.

**ResetTalents**
Full talent wipe: unlearns talent spells, refunds points, charges the escalating gold cost (unless `noCost`), and cleans pet talents and action buttons.

**CalculateTalentsPoints**
Points-from-level math: total talent points a character of this level owns.

**InitTalentForLevel**
Reconciles spent vs available points at level change, wiping talents when oversubscribed (de-level path).

**UpdateFreeTalentPoints**
Recomputes the free-points field from level and spent count, optionally resetting when inconsistent.

**GetResetTalentsCost**
Returns the current talent-wipe cost — the multiplier decays with time since the last reset.

**UpdateResetTalentsMultiplier**
Decays the reset-cost multiplier based on elapsed time since the previous wipe.

**SendTalentWipeConfirm**
Sends the trainer's talent-wipe confirmation dialog with the computed cost.

**GetFreeTalentPoints**
Reads the free talent points field (`PLAYER_CHARACTER_POINTS1`).

**SetFreeTalentPoints**
Writes the free talent points field.

### Spells, Spell Modifiers & Cooldowns

**AddSpell**
Spellbook core insert: validates against the DBC, maintains disabled/superseded rank bookkeeping (only the highest talent rank stays active), tracks incremental save state, cascades to skill lines via `UpdateSpellTrainedSkills`, and optionally casts passive-like spells at learn.

**LearnSpell**
Adds a spell through `AddSpell` plus client notification, re-enabling dependent higher ranks that were disabled.

**RemoveSpell**
Removes a spell from the book, optionally relearning the lower rank in its place, and notifies the client via `SendSpellRemoved`.

**ResetSpells**
Wipes the spellbook and relearns the default kit — the wipe-and-relearn path.

**LearnDefaultSpells**
Teaches the race/class default spell kit from PlayerInfo.

**LearnQuestRewardedSpells**
Login-time form: walks every rewarded quest and re-grants each quest-taught spell.

**LearnQuestRewardedSpells#2**
Re-grants the spell taught by one specific rewarded quest (casts the reward spell on self).

**LearnSpellHighRank**
Teaches the highest known chain rank via the `DoPlayerLearnSpell` functor and its `operator()`.

**CastHighestStealthRank**
Casts the best known Stealth rank (Vanish, Improved Sap support).

**ConvertSpell**
One-for-one spell replacement in the book — the race-change conversion primitive.

**ApplySpellMod**
The spell-modifier engine (template, instantiated for int32/uint32/float): folds flat then percent mods into a base value, dropping charges unless the 1.11 Nature's Grace rule keeps them. Carries the Nostalrius fix where a -100% cast-time mod (Nature's Swiftness) zeroes the cast outright, discarding accumulated flat increases, so Barkskin cannot leave a 1-second cast.

**AddSpellMod**
Registers/unregisters a `SpellModifier` with client notification of the changed mod bits.

**SendSpellMod**
Sends one spell-mod flat/percent update packet to the client.

**GetSpellMod**
Finds the registered modifier for an op/spell pair.

**IsAffectedBySpellmod**
Family/mask applicability check for a modifier against a spell, aware of charge interactions with the current cast.

**HasInstantCastingSpellMod**
True when a registered mod would make the given spell instant.

**DropModCharge**
Consumes one charge from a modifier for the given cast, marking it for removal when depleted.

**RemoveSpellMods**
Removes the charge-depleted modifiers consumed by a finished cast.

**RestoreSpellMods**
Rolls back charges consumed by a cast that failed (optionally scoped to one owner aura).

**RestoreAllSpellMods**
Rolls back consumed charges across all pending casts (aura-scoped variant of the recovery path).

**AddGCD**
Starts the global cooldown for a spell (optionally forced duration and client-synced).

**AddCooldown**
Starts spell/category/item cooldowns — item proto can override durations, and permanent trade-skill cooldowns are supported.

**RemoveSpellCooldown**
Clears one spell's cooldown, optionally syncing the client.

**RemoveSpellCategoryCooldown**
Clears a whole category cooldown, optionally syncing the client.

**RemoveAllCooldowns**
Clears every cooldown (or only notifies the client when `sendOnly`).

**LockOutSpells**
School lockout — the counterspell/kick path: locks all spells of the school mask for the duration.

**RemoveSpellLockout**
Lifts a school lockout, avoiding duplicate client packets via the already-sent set.

**SendClearCooldown**
Sends a single-spell cooldown-clear packet for a target.

**SendClearAllCooldowns**
Sends the clear-all-cooldowns packet for a target.

**SendSpellCooldown**
Sends one explicit spell-cooldown packet (spell, duration, target).

**_LoadSpellCooldowns**
Restores cooldowns from `character_spell_cooldown` at login, dropping already-expired rows.

**_SaveSpellCooldowns**
Persists active cooldowns to `character_spell_cooldown` inside the save transaction.

**HasSpell**
True when the spell is in the book (override of the Unit query).

**HasActiveSpell**
True when the spell is in the book and active (i.e. shown in the spellbook, not a superseded rank).

**GetSpellMap**
Const access to the `PlayerSpellMap`.

**GetSpellMap#2**
Mutable access to the `PlayerSpellMap`.

**GetSpellRank**
Returns the spell's rank value capped by level for level-scaled ranks.

**GetTrainerSpellState**
Classifies a trainer spell green/red/gray for this player (known, level, skill, money preconditions).

**IsSpellFitByClassAndRace**
`skill_line_ability` race/class fit check (optionally returning the required level).

**SendInitialSpells**
Pushes the whole spellbook and cooldown list to the client at login.

**InterruptSpellsWithCastItem**
Interrupts any current casts whose cast item is the given item (destroyed/traded mid-cast).

**SendChannelUpdate**
Sends the channel-duration update packet (0 ends the channel client-side).

**UpdateChannelStartPosition**
Records the position a channel started at, for the channel-interrupt movement check.

**GetItemSetEffect**
Returns the active `ItemSetEffect` tracker for a set id, if any pieces are worn.

**AddItemSetEffect**
Creates/returns the set-bonus tracker when the first piece of a set is equipped.

**RemoveItemSetEffect**
Drops the set-bonus tracker when the last piece is removed.

**IsImmuneToSpellEffect**
Player-specific effect-immunity override layered on the Unit version.

### Skills

**SetSkill**
Creates, updates, or removes (currVal=0) a skill line in the packed `PLAYER_SKILL_*` fields, cascading to spells via `UpdateSkillTrainedSpells` on removal or step change.

**GetSkill**
Unpacks a skill's value or max from the packed fields with selectable permanent/temporary bonus inclusion — the general form behind the named wrappers (`GetSkillValue`, `GetSkillValueBase`, `GetSkillValuePure`, `GetSkillMax`, `GetSkillMaxPure`).

**HasSkill**
True when the skill line exists on the character.

**ModifySkillBonus**
Adds/removes a permanent or temporary skill bonus in the packed bonus halves.

**GetSkillBonus**
Reads the permanent or temporary bonus half for a skill.

**UpdateSkillPro**
Chance-based skill increment toward max (profession model); returns whether it skilled up.

**SkillGainChance**
File-local helper mapping the gray/green/yellow thresholds to a skill-up chance.

**UpdateCraftSkill**
Crafting skill-up using per-spell gain data (`SkillLineAbility` gain chances).

**UpdateGatherSkill**
Gathering skill-up scaled by red-level distance with an optional multiplier.

**UpdateFishingSkill**
Fishing's own 1-in-N skill-up model based on current skill.

**UpdateSkill**
Plain stepwise skill-up used by generic skill gains.

**UpdateCombatSkills**
Weapon/defense skill gains on melee events, shaped by gray-level distance (`GetBaseWeaponSkillValue`, `GetBaseDefenseSkillValue`).

**UpdateSkillsForLevel**
Raises the cap of capped-type skills at level-up (auto-raising values for capped types).

**UpdateSpellTrainedSkills**
The spell-to-skill direction: grants or removes weapon/riding skills taught by spells.

**InitPrimaryProfessions**
Initializes the free primary-profession counter to the configured maximum.

**GetFreePrimaryProfessionPoints**
Reads the free primary-profession slots (`PLAYER_CHARACTER_POINTS2`).

**SetFreePrimaryProfessions**
Writes the free primary-profession slots.

### Stats Plumbing Owned Here

The recalculation formulas live in the sibling Player.StatSystem unit (StatSystem.cpp); this unit stores the aura-side bookkeeping those formulas read.

**HandleBaseModValue**
Applies a flat or percent change to a crit/block/dodge/parry base-mod group and triggers the matching rating update in Player.StatSystem.

**GetBaseModValue**
Reads one base-mod component (flat or percent) of a group.

**GetTotalBaseModValue**
Returns a group's flat value scaled by its percent component.

**GetTotalPercentageModValue**
Returns flat + percent components summed for a group.

**SetBaseModValue**
Directly writes a base-mod component (used by `Player.StatSystem/UpdateAllCritPercentages`).

**ApplyStatBuffMod**
Applies a flat stat buff into the positive or negative display field by sign.

**ApplyStatPercentBuffMod**
Applies a percent stat buff across both the positive and negative display fields.

**InitStatBuffMods**
Zeroes the positive/negative stat display fields.

**GetPosStat**
Reads the positive stat-buff display field for a stat.

**GetNegStat**
Reads the negative stat-buff display field for a stat.

**GetResistanceBuffMods**
Reads the positive or negative resistance-buff display field for a school.

**SetResistanceBuffMods**
Writes a resistance-buff display field.

**ApplyResistanceBuffModsMod**
Applies a flat signed change to a resistance-buff display field.

**ApplyResistanceBuffModsPercentMod**
Applies a percent change to a resistance-buff display field.

**UpdateDamageDonePercent**
Recomputes the school damage-done multiplier field from auras.

**RegenerateAll**
The 2-second regen tick fan-out to health and each power the class uses.

**Regenerate**
Per-power regeneration: interrupted-mana model for mana, out-of-combat rage decay, energy tick.

**RegenerateHealth**
Spirit-based health regeneration — halved while polymorphed.

**RewardRage**
Damage-to-rage conversion for the attacker or victim side.

**HandleFoodEmotes**
Plays the periodic eat/drink emote while sitting with food/drink auras.

**GetShieldBlockValue**
Block value from strength plus the block base-mod group (overrides the Unit version).

**GetMeleeCritFromAgility**
Class-specific agility→melee-crit ratio.

**GetDodgeFromAgility**
Class-specific agility→dodge ratio.

**SetRegularAttackTime**
Sets attack timers from the equipped weapons' proto attack times (optionally resetting the swing timer).

**SetCanParry**
Sets the parry capability flag and triggers `Player.StatSystem/UpdateParryPercentage`.

**CanParry**
Reads the parry capability flag.

**SetCanBlock**
Sets the block capability flag and triggers `Player.StatSystem/UpdateBlockPercentage`.

**CanBlock**
Reads the block capability flag.

**CanDualWield**
Reads the dual-wield capability flag.

**SetCanDualWield**
Sets the dual-wield capability flag.

**GetAmmoDPS**
Returns the cached ammo DPS applied to ranged damage.

**_ApplyAmmoBonuses**
Recomputes cached ammo DPS from the ammo item (with subclass compatibility) and refreshes ranged damage.

**CheckAmmoCompatibility**
Checks the ammo proto's subclass against the equipped ranged weapon.

**InitDataForForm**
Applies shapeshift-form attack times and power type, then refreshes attack power/damage.

**_ApplyItemMods**
Slot-level dispatcher applying/removing one equipped item's stats, auras, equip spells, and enchants.

**_ApplyItemBonuses**
The big proto-to-stat mapping: stats, armor, resistances, and damage from an item proto, slot-aware for ranged-AP classes.

**_ApplyWeaponDependentAuraMods**
Re-targets weapon-conditional auras when a weapon in the attack slot changes.

**_ApplyWeaponDependentAuraCritMod**
Applies/removes one weapon-conditional crit aura against the equipped weapon.

**_ApplyWeaponDependentAuraDamageMod**
Applies/removes one weapon-conditional damage aura against the equipped weapon.

**_RemoveAllItemMods**
Bulk removal of all equipped item effects (form/stat rebuild bracket, remove side).

**_ApplyAllItemMods**
Bulk reapplication of all equipped item effects (rebuild bracket, apply side).

**ApplyItemEquipSpell**
Applies/removes an item's on-equip spells, form-condition aware.

**ApplyEquipSpell**
Applies/removes a single equip spell entry for an item, honoring form conditions at form change.

**UpdateEquipSpellsAtFormChange**
Re-evaluates every equipped item's equip spells when the shapeshift form changes.

**CastItemCombatSpell**
Rolls chance-on-hit weapon procs — PPM derived from weapon speed — plus poisons and enchant procs on a melee hit.

**CastItemUseSpell**
Casts an item's use spell with its category cooldown handling.

### Items, Inventory & Trade

The storage model is positional: a 16-bit position packs bag and slot — equipment 0–18, bag slots 19–22, backpack 23–38, bank 39–62, bank bags 63–68, buyback 69–80, keyring 81+.

**IsInventoryPos**
Static classifier: is a packed position (bag<<8|slot) inside the inventory/backpack range.

**IsInventoryPos#2**
Static classifier, bag+slot form, for the inventory/backpack range.

**IsEquipmentPos**
Static classifier: is a packed position an equipment slot.

**IsEquipmentPos#2**
Static classifier, bag+slot form, for the equipment range.

**IsBankPos**
Static classifier: is a packed position inside the bank ranges.

**IsBankPos#2**
Static classifier, bag+slot form, for the bank ranges.

**IsBagPos**
Static classifier: is a packed position an equippable bag slot (bag bar or bank bag bar).

**IsMainHandPos**
Static classifier: is a packed position exactly the main-hand equipment slot.

**IsValidPos**
Validity check of a bag + slot against the character's actual bags and bag sizes (explicit-position aware).

**IsValidPos#2**
Packed-position wrapper of the validity check.

**GetItemByPos**
Item lookup by bag + slot (the worker; dereferences equipped bags).

**GetItemByPos#2**
Item lookup by packed position (bag<<8|slot wrapper).

**GetItemByGuid**
Item lookup by guid across inventory, bags, and bank.

**GetItemCount**
Counts copies of an item id across bags (optionally the bank), with an item to skip (trade/split source).

**HasItemCount**
True when at least `count` copies of the item id exist (optionally counting the bank).

**HasItemWithIdEquipped**
Worn-item test for an item id with a slot to exclude (unique-equipped checks during swaps).

**GetWeaponForAttack**
Returns the weapon item for an attack type (plain form, no filters).

**GetWeaponForAttack#2**
Filtered form: the weapon for an attack type with nonbroken/useable filters applied.

**GetWeaponForParry**
Returns the weapon that can currently parry (main hand, else usable off-hand weapon).

**GetAttackBySlot**
Static slot-to-attack-type mapping (`MAX_ATTACK` when the slot is not a weapon slot).

**CountFreeInventorySlots**
Counts free backpack + bag slots.

**GetHighestKnownArmorProficiency**
Returns the best wearable armor class from the proficiency mask (cloth→plate).

**_CanStoreItem**
The placement solver. Through **_CanStoreItem_InSpecificSlot**, **_CanStoreItem_InBag**, and **_CanStoreItem_InInventorySlots** it fills merge targets first, then specialized bags (quivers/pouches/keys, honoring the ignore-bag-filters flag), then free slots — emitting an `ItemPosCountVec` plan (its helper **isContainedIn** is a member of `ItemPosCount` defined in this file) or the precise `InventoryResult` error. **CanStoreNewItem** and **CanStoreItem** are the public faces; **CanStoreItems** simulates a whole set at once (trade acceptance); **_CanTakeMoreSimilarItems** (public: **CanTakeMoreSimilarItems**, **CanTakeMoreSimilarItems#2**) enforces unique/max-count limits.

**CanEquipItem**
Full equip legality for an existing item: slot resolution, proficiency, level, skill, combat/shapeshift restrictions, dual-wield awareness; returns an `InventoryResult` and the destination.

**CanEquipItem#2**
Proto-based worker behind the Item form: the full slot/proficiency/level/skill/combat/shapeshift rule set, usable before an item instance exists.

**CanEquipNewItem**
Equip legality for a not-yet-created item id (wraps a temporary item through `CanEquipItem`).

**CanEquipUniqueItem**
Unique-equipped enforcement against worn items, with a slot exception for swaps.

**CanUnequipItem**
Removal legality for one slot — bags must be empty, combat rules apply.

**CanUnequipItems**
Checks that `count` copies of an item id could be unequipped/removed legally.

**CanBankItem**
Bank placement solver, including bank-bag purchase slots; fills an `ItemPosCountVec` plan.

**CanUseItem**
Consumption/use gating by level, skill, reputation, and faction for an existing item.

**CanUseItem#2**
Use gating for a bare `ItemPrototype` (no item instance).

**CanUseAmmo**
Projectile variant of the use gate for an ammo item id.

**FindEquipSlot**
Resolves which equipment slot a proto goes to for this character (dual-wield/bag-bar aware), honoring a requested slot.

**StoreNewItem**
Creates a new item per a placement plan — updates quest counters, loot logging, and random property — and stores it.

**StoreItem**
Places an existing item per plan with stack merging.

**_StoreItem**
The single-slot physical move/clone primitive under the planners.

**EquipNewItem**
Creates and equips a new item id at a position.

**EquipItem**
Equips an item: visible bytes (`SetVisibleItemSlot`, `VisualizeItem`), stat application, `ApplyEquipCooldown`, and combat/cast interactions.

**QuickEquipItem**
Loading-time fast equip path — visible bytes without the full legality/effects pass.

**BankItem**
Alias of `StoreItem` for bank destinations.

**RemoveItem**
Detaches an item from a slot without destroying it (visible bytes cleared, stats removed).

**MoveItemFromInventory**
Transfer half used by trade/mail/auction: removes the item and marks it removed from `character_inventory`.

**MoveItemToInventory**
Return half of the transfer: places the item back, aware of whether its rows still exist in the character DB.

**DestroyItem**
Deletes an item at a position with enchant, duration, quest, and zone bookkeeping.

**DestroyItemCount**
Count-based removal from a specific item instance (stack decrement or delete).

**DestroyItemCount#2**
Count-based removal of an item id swept across bags, with unequip/bank options.

**DestroyEquippedItem**
Finds and destroys one worn copy of an item id.

**DestroyZoneLimitedItem**
Removes zone-limited items when leaving their zone.

**DestroyConjuredItems**
Removes conjured items (logout cleanup).

**SplitItem**
Splits `count` off a stack into a destination position.

**SwapItem**
The full two-position exchange with re-validation — merge, equip swaps, and bag-into-bag cases.

**AutoUnequipWeaponsIfNeed**
Forced weapon unequips on proficiency loss or disarm-legality change.

**AutoUnequipOffhandIfNeed**
Evicts the off-hand when a two-hander arrives (mails it on overflow).

**AutoUnequipItemFromSlot**
Single-slot eviction to bags, mailing the item when bags are full.

**AddItem**
GM/utility grant: creates and stores (or equips) an item id with count.

**AutoStoreLoot**
Rolls a loot template directly into bags (optionally broadcasting the received items).

**AddItemToBuyBackSlot**
Pushes a sold item into the 12-slot buyback carousel — the oldest entry is evicted and truly deleted (single slot pre-1.8).

**GetItemFromBuyBackSlot**
Reads a buyback slot.

**RemoveItemFromBuyBackSlot**
Clears a buyback slot, optionally deleting the item.

**GetBuyBackItemPrice**
Reads the stored buyback price field for a slot.

**BuyItemFromVendor**
Vendor purchase: stock and limited-quantity restock, price with reputation discount, extended honor/item costs, delivery through the placement planners.

**GetReputationPriceDiscount**
Reputation-based vendor (and taxi) price discount for the creature's faction.

**SendLoot**
Opens a loot window from any source:

*   **Gameobject** — locks/traps/fishing holes, loot generated from templates on first open.
*   **Item** — contained loot (disenchant-style) generated once.
*   **Corpse** — battleground insignia (**RemovedInsignia** converts the corpse to bones and builds the insignia loot).
*   **Creature** — body loot, skinning, or pickpocket, with group loot-method rules applied (round-robin, master, need-before-greed thresholds).

**SendLootRelease**
Sends the loot-window close packet.

**SendLootError**
Sends a loot refusal with reason.

**SendNotifyLootItemRemoved**
Notifies the open loot window that a slot was taken.

**SendNotifyLootMoneyRemoved**
Notifies the open loot window that the money was taken.

**SendLootMoneyNotify**
Sends the looted-money amount notification.

**IsAllowedToLoot**
Corpse-loot rights from tap/recipient rules.

**GetMaxLootDistance**
Loot reach, widened for large creatures.

**GetLootGuid**
Returns the guid of the currently open loot window.

**SetLootGuid**
Sets the open-loot-window guid.

**LootMoney**
Server-side money loot with group splitting and money logging.

**GetTrader**
Returns the trade counterparty, if a trade is open.

**GetTradeData**
Returns the open trade session object.

**TradeCancel**
Tears down the trade, optionally notifying the counterparty with a status.

**AddWeaponProficiency**
ORs a flag into the weapon proficiency mask.

**AddArmorProficiency**
ORs a flag into the armor proficiency mask.

**GetWeaponProficiency**
Reads the weapon proficiency mask.

**GetArmorProficiency**
Reads the armor proficiency mask.

**SendProficiency**
Sends the proficiency packet for an item class + subclass mask.

**IsTwoHandUsed**
True when a two-handed weapon occupies the main hand.

**CanBeDisarmed**
Disarm-legality query (final override).

**SetAmmo**
Sets the ammo field after compatibility validation and reapplies ammo bonuses.

**RemoveAmmo**
Clears the ammo field and its DPS contribution.

**SendNewItem**
Item-received broadcast: creation/receive source flags, with group broadcast for quest pushes.

**SendEquipError**
Sends the inventory refusal packet for an `InventoryResult`.

**SendBuyError**
Sends the vendor buy refusal packet.

**SendSellError**
Sends the vendor sell refusal packet.

**SendOpenContainer**
Opens a bag remotely on the client (disenchant/lockbox flows).

**OnReceivedItem**
Item-received hook — the scourge-invasion item script entry point.

**RemoveItemDependentAurasAndCasts**
Removes auras and interrupts casts that depended on a departing item.

**GetItemUpdateQueue**
The deferred item-update vector maintained by Item's friend hooks and flushed on update.

### Enchantments & Durability

**ApplyEnchantment**
Applies or removes one enchantment slot's effects on an item — procs, damage, stats, buffs, temporary-duration hookup — with condition checking (`ignore_condition` for form changes).

**ApplyEnchantment#2**
All-slots form: loops every enchantment slot of the item through the worker.

**AddEnchantmentDurations**
Registers all of an item's temporary enchants into the countdown list.

**AddEnchantmentDuration**
Registers one enchant slot's remaining duration into the countdown list.

**RemoveEnchantmentDurations**
Drops an item's entries from the countdown list (item leaving the character).

**RemoveAllEnchantments**
Strips a given enchantment slot from every item.

**UpdateEnchantTime**
Per-tick decay of the temporary-enchant countdown list, expiring finished enchants.

**SendEnchantmentDurations**
Login push of remaining temporary-enchant times.

**BuildEnchantmentLog**
Builds the enchant-applied packet (caster, item, spell, affiliation display).

**SendEnchantmentLog**
Sends the enchant-applied announcement.

**AddItemDurations**
Registers a limited-lifetime item into the duration list.

**RemoveItemDurations**
Removes an item from the duration list.

**UpdateItemDuration**
Per-tick decay of limited-lifetime items — real-time-only items honored via the flag.

**SendItemDurations**
Login push of remaining item lifetimes.

**DurabilityLossAll**
Percentage durability damage across equipment (optionally inventory too) — the death-penalty path.

**DurabilityLoss**
Percentage durability damage on one item.

**DurabilityPointsLossAll**
Flat-point durability damage across equipment (optionally inventory).

**DurabilityPointsLoss**
Flat-point durability damage on one item.

**DurabilityPointLossForEquipSlot**
One point of durability damage to a specific equipment slot.

**DurabilityRepairAll**
Repairs everything, priced per point from the `DurabilityCosts`/`DurabilityQuality` DBCs with the vendor discount; returns the cost.

**DurabilityRepair**
Repairs one position with the same DBC pricing; returns the cost.

### Gossip & Quest Giver Menus

**PrepareGossipMenu**
Assembles the menu for an NPC or gameobject from `gossip_menu`/`gossip_menu_option` with condition filtering, npcflag matching, and script hooks — hiding options the character cannot use (untrainable class, no pet to stable, and so on).

**SendPreparedGossip**
Frame selection for an interaction: plain gossip vs quest giver, auto-launching the quest menu for pure quest givers.

**OnGossipSelect**
Dispatches a chosen gossip row — vendor, taxi, trainer, banker, innkeeper binding, spirit healer, battlemaster, auctioneer, stable, tabard/petition — through the session handlers or the script system.

**GetGossipTextId**
Static greeting-text resolution from the source object's default gossip (npc_gossip lookup).

**GetGossipTextId#2**
Greeting-text resolution for a menu id, evaluating menu conditions.

**PrepareQuestMenu**
Builds the quest list from the giver's relations: involved quests by state, available quests filtered by eligibility.

**SendPreparedQuest**
Sends the right frame for the prepared menu — greeting, details, or reward.

**GetNextQuest**
Follow-up chain advancement: the next quest offered by the same ender.

### Quest System

**CanTakeQuest**
ANDs the entire `SatisfyQuest*` prerequisite family — status, level, skill, condition, class, race, reputation, previous quest, prev/next chain, breadcrumbs, exclusive group, timed, and log space — each rule optionally raising a client error.

**CanAddQuest**
`CanTakeQuest` plus source-item storability via `CanGiveQuestSourceItemIfNeed`.

**AddQuest**
Log insertion: writes the slot, zeroes objective counters, arms the timer (shared timed quests copy the pusher's remaining time), applies PvP-quest flagging, fires accept scripts and the quest start script, destroys a starter item that is not also an objective, grants the source item (**GiveQuestSourceItemIfNeed**), reconciles already-held objective items (**AdjustQuestReqItemCount**), casts quest-area spells, and refreshes interactive world objects (**UpdateForQuestWorldObjects**).

**RewardQuest**
The turn-in transaction:

*   Consumes objective items and the timed-quest entry; runs the Alterac Valley quest hook.
*   Grants choice and fixed reward items, then reputation (**RewardReputation**).
*   Awards XP below the level cap, or converts to money at cap; applies required/reward money through **LogModifyMoney**.
*   Sends reward mail (template id, optionally attributed to the quest starter), marks rewarded (repeatables reset to none), announces (**SendQuestReward**), and fires reward scripts and completion spell casts.

**CompleteQuest**
Marks a quest complete in log and status, lighting the turn-in state.

**IncompleteQuest**
Reverts a complete quest back to incomplete (objective loss).

**RemoveQuest**
Abandons a quest: clears the slot, returns or destroys the start item, drops the timed entry.

**RemoveQuestAtSlot**
Slot-indexed abandon used by the client's log-slot opcode.

**FailQuest**
Timed/event failure: marks failed, resets counters, and updates the log UI.

**FullQuestComplete**
GM fill-everything path (`.quest complete`): forces all objectives — kills, items, explore — to done.

**CanCompleteQuest**
Turn-in validator: every objective of the given quest satisfied.

**CanCompleteRepeatableQuest**
Repeatable-quest variant of the completion validator (item requirements re-checked each turn-in).

**CanRewardQuest**
Reward-eligibility check: completed, objectives verified, and reward-inventory space for fixed items.

**CanRewardQuest#2**
Reward form that additionally validates the chosen reward item's storability before `RewardQuest`.

**TakeOrReplaceQuestStartItems**
Takes back (or re-grants) a quest's start items on abandon/accept-replace flows.

**KilledMonster**
Kill-objective credit from a `CreatureInfo` (resolves the credit entry) and guid.

**KilledMonsterCredit**
Kill credit by entry — killcredit-entry aware — advancing matching quest counters.

**CastedCreatureOrGO**
Cast-objective credit for a spell on a creature or GO (original-caster gated).

**TalkedToCreature**
Speak-to-creature objective credit.

**ItemAddedQuestCheck**
Advances deliverable counters when items enter the inventory.

**ItemRemovedQuestCheck**
Rolls back deliverable counters when items leave the inventory.

**AreaExploredOrEventHappens**
Explore/event objective completion for one player.

**GroupEventHappens**
Explore/event completion propagated to eligible group members near the event object.

**GroupEventFailHappens**
Group-wide event failure propagation.

**MoneyChanged**
Re-evaluates money-objective quests after a coinage change.

**ReputationChanged**
Re-evaluates reputation-objective quests after a standing change.

**GetQuestStatus**
Returns the status of a quest id (`NONE` when unknown).

**GetQuestStatusData**
Returns the full `QuestStatusData` record for a quest id, if tracked.

**GetQuestStatusMap**
Direct access to the quest status map.

**SetQuestStatus**
Sets a quest's status, keeping log/timer state consistent.

**GetQuestRewardStatus**
True when the quest was already rewarded.

**IsActiveQuest**
True when the quest is taken or takeable-active for this player.

**IsCurrentQuest**
True when the quest is in the log (optionally filtered complete/incomplete).

**CanSeeStartQuest**
Visibility test for a quest starter — prerequisites minus level, for the marker display.

**CanShareQuest**
True when the quest is sharable and currently in the log.

**HasQuestForItem**
True when any active quest still needs the item id (loot-permission driver).

**HasQuestForGO**
True when any active quest still needs the GO (sparkle/loot-permission driver).

**GetQuestLevelForPlayer**
The quest's level, falling back to the player's own level for level-scaled (-1) quests.

**FindQuestSlot**
Finds the 20-slot log index of a quest id (`MAX_QUEST_LOG_SIZE` when absent).

**GetQuestSlotQuestId**
Reads the quest id stored in a packed log slot.

**SetQuestSlot**
Writes a log slot: quest id, zeroed counters, optional timer.

**SetQuestSlotCounter**
Writes one objective counter byte in a slot.

**SetQuestSlotState**
ORs a state flag (complete/fail) into a slot.

**RemoveQuestSlotState**
Clears a state flag from a slot.

**SetQuestSlotTimer**
Writes a slot's timer field.

**SwapQuestSlot**
Exchanges two log slots (client reorder).

**AddTimedQuest**
Inserts a quest id into the timed set.

**RemoveTimedQuest**
Removes a quest id from the timed set.

**GetQuestShareInfo**
Reads the pending quest-push bookkeeping (pusher guid + quest).

**SetQuestShareInfo**
Records a pending quest push from a guid.

**ClearQuestShareInfo**
Clears the pending quest-push record.

**GetInGameTime**
Reads the in-game-time value used by raid quest credit windows.

**SetInGameTime**
Writes the in-game-time value.

**SendQuestCompleteEvent**
Sends the quest-completed event packet.

**SendQuestReward**
Sends the reward frame packet with XP for the turn-in.

**SendQuestFailed**
Sends the generic quest-failed packet.

**SendQuestFailedAtTaker**
Sends the failure reason at the quest taker (default: requirements not met).

**SendQuestTimerFailed**
Sends the timer-expired failure packet.

**SendCanTakeQuestResponse**
Sends the can't-take reason code.

**SendQuestConfirmAccept**
Sends the escort/event accept-confirmation to a nearby receiver.

**SendPushToPartyResponse**
Sends the quest-push result (busy, invalid, already have…) back to the pusher.

**SendQuestUpdateAddItem**
Sends an item-objective counter update.

**SendQuestUpdateAddCreatureOrGo**
Sends a kill/use-objective counter update.

**IsAtGroupRewardDistance**
The shared close-enough-for-credit test against a reward source.

**RewardSinglePlayerAtKill**
Solo credit at a kill: XP, honor hooks, quest kill credit.

**RewardPlayerAndGroupAtEvent**
Distributes event credit to eligible group members near the source.

**RewardPlayerAndGroupAtCast**
Distributes cast credit to eligible group members near the source.

### Persistence — Load, Save, Delete

**LoadFromDB**
The login load pipeline, driven by a `SqlQueryHolder` of pre-fired `PLAYER_LOGIN_QUERY_*` results:

*   **Validation:** account ownership (bypassed for bots), transfer-lock refusal, forced rename when the stored name fails current rules.
*   **Field rebuild:** appearance, XP, money (clamped to **GetMaxMoney**), explored zones (**_LoadIntoDataField**), drunkenness re-sobered by offline time (fully sober after 15 minutes), play-time-limit flags, watched faction, ammo, action bars.
*   **Position repair:** invalid coordinates → homebind (**_LoadHomeBind**, itself falling back to the race start on bad data); saved-in-BG → entry point; transports sanity-checked to a ±250-unit box; dead instance binds → the entrance trigger, with a hardcoded Naxxramas exit because vanilla has no exit trigger; broken taxi strings → the flight's source node.
*   **The `_Load*` family in dependency order:** group, honor CP, bound instances, BG data, guild, skills (+ forgotten skills on 1.10.2+), auras, spells, talents and defaults, quest status, reputation, inventory, item loot, spell cooldowns — then quest item counters recounted from actual inventory (crash-recovery guard).
*   **Finalization:** `UpdateAllStats` (Player.StatSystem), saved health/powers restored capped at max, GM login-state config, BG queue re-add, **CreatePacketBroadcaster**, and the one-shot **UpdateOldRidingSkillToNew** 1.12 riding migration (tier decided by whether **_LoadInventory** saw an epic mount).

**_LoadInventory**
Rebuilds all containers from a bag-sorted `character_inventory` × `item_instance` join. Broken protos and failed loads are archived to `character_deleted_items` and mailed back rather than silently destroyed; expired-duration items are dropped; misplaced or unequippable items fall back to the mail system too.

**_LoadAuras**
Restores aura holders from `character_aura` with remaining durations (per-row via `LoadAura`).

**LoadAura**
Rebuilds one aura holder from its save struct, adjusting duration by offline time.

**_LoadSpells**
Restores the spellbook from `character_spell` (active/disabled bits honored).

**_LoadQuestStatus**
Restores the quest log, counters, and timed set from `character_queststatus`.

**_LoadSkills**
Restores skills from `character_skills` into the packed fields (holder form).

**LoadSkillsFromFields**
Field-array form of skill restoration (fields already populated).

**_LoadForgottenSkills**
Restores the weapon-skill memory from `character_forgotten_skills` (value re-grant on relearn).

**_LoadGroup**
Rejoins the saved group if membership is still valid.

**_LoadBoundInstances**
Restores instance binds from `character_instance`, deleting unresolvable or conflicting rows.

**_LoadBGData**
Restores battleground identity (instance, team, join position) from `character_battleground_data`.

**_LoadGuild**
Restores guild id/rank fields, clearing them when the membership row vanished.

**_LoadHomeBind**
Restores the homebind from `character_homebind`, falling back to the race/class start position.

**_LoadItemLoot**
Restores unopened container loot from `item_loot`.

**LoadCorpse**
Re-links the player to an existing corpse (or resurrects state when none).

**LoadPet**
Resummons the saved pet at login where the map allows.

**Initialize**
Pre-load object shell setup: assigns the guid and initializes update fields before `LoadFromDB` fills them.

**SaveToDB**
The save transaction. Resets the autosave timer, refuses to run for bots (**IsSavingDisabled** — the same flag that protects mid-race-change state), and defers when a far teleport is in flight. Then, in one per-guid transaction:

*   The entire `characters` row as a single REPLACE (~60 bound parameters) — position (teleport destination if mid-teleport), taxi mask and path, played time, rest bonus, logout time, talent-reset bookkeeping, flags, stable slots, cached zone, the honor block, drunk/health/powers, explored zones, and the visible-gear `equipment_cache` string.
*   The delta-based satellite passes: **_SaveBGData**, **_SaveInventory** (replays the item update queue with duplicate-guid diagnostics), **_SaveQuestStatus**, **_SaveSpells**, **_SaveSpellCooldowns**, **_SaveAuras** (**SaveAura** filters what qualifies), **_SaveSkills**, plus reputation and honor manager saves.
*   After commit: **_SaveStats** (config-gated to logout-only) and the pet save. **UpdateCharacterFlags** recomputes the persisted `character_flags` bitmask from live state.

**SaveInventoryAndGoldToDB**
Anti-duping fast save used around trades and loot: inventory + gold serialized in one transaction.

**SaveGoldToDB**
Fast single-column gold save.

**SavePositionInDB**
Static offline position writer (GM commands, unstuck) — updates `characters` directly by guid.

**DeleteFromDB**
Static delete/soft-delete: converts the corpse to bones, removes guild membership (disbanding an emptied leaderless guild), leaves the group, tears down petitions (**RemovePetitionsAndSigns**), then either:

*   **Method 0 (hard):** returns COD mail to senders, hands over mailed/gifted items, and deletes every `character_*` satellite row, `item_instance` rows, mail, and the `characters` row.
*   **Method 1 (soft, level-gated):** unlinks the row by moving `account`/`name` into `deleted_account`/`deleted_name` with `deleted_time`, leaving it recoverable.

**DeleteOldCharacters**
Static reaper using the configured keep-days: sweeps soft-deleted rows older than the window through the hard-delete path.

**DeleteOldCharacters#2**
Explicit keep-days form of the reaper.

**GetZoneIdFromDB**
Static single-row zone read for handlers without a loaded Player (recomputes and backfills when stale).

**GetLevelFromDB**
Static single-row level read by guid.

**LoadPositionFromDB**
Static position/map read by guid, reporting flight state.

**GetGuildIdFromDB**
Static guild-id read from `guild_member` by guid.

**GetRankFromDB**
Static guild-rank read by guid.

**BuildEnumData**
Builds one character-list entry for `SMSG_CHAR_ENUM` from the enum query row (equipment cache driven).

**UpdateOldRidingSkillToNew**
One-shot migration of the pre-1.12 riding skills to the new model (epic-mount aware).

**GetSaveTimer**
Reads the autosave countdown.

**SetSaveTimer**
Writes the autosave countdown.

**HasCharacterFlag**
Tests a persistent character flag.

**SetCharacterFlag**
Sets/clears a persistent character flag.

**UpdateCharacterFlags**
Recomputes the persisted flag set from current state (ghost, resting, …).

**IsSavingDisabled**
True when saving is disabled — the bot/race-change kill-switch.

**SetSaveDisabled**
Toggles the save kill-switch (bots, race change staging).

**GetShortDescription**
The "player:guid [username:accountId@IP]" logging string.

### Money & Player Action Logging

**GetMoney**
Reads coinage (`PLAYER_FIELD_COINAGE`).

**SetMoney**
Writes coinage and re-evaluates money-objective quests via `MoneyChanged`.

**ModifyMoney**
Signed coinage change clamped into [0, `GetMaxMoney()`].

**GetMaxMoney**
The coinage cap — standard, or the far lower trial-account cap.

**LogModifyMoney**
Auditable money change: above the configured threshold (always for GM sessions) writes the money-trades player log and world trade log with counterparty guids before applying.

**Player**
`Log::Player` overload — session-keyed, with subtype; fans out to the DB gate, `logs_player` insert, and file header writers.

**Player#2**
`Log::Player` overload — session-keyed, no subtype.

**Player#3**
`Log::Player` overload — account-keyed, with subtype.

**Player#4**
`Log::Player` overload — account-keyed, no subtype.

**PlayerLogHeaderToConsole**
File-local Log helper writing the account/character header for console-bound player-log lines.

**PlayerLogHeaderToFile**
File-local Log helper writing the account/character header for file-bound player-log lines.

**IsPlayerLoggingEnabledToDB**
File-local per-type config gate deciding whether a player-log type goes to the database.

**PlayerLogToDB**
File-local `logs_player` insert carrying account, IP, guid, name, map, and position.

### Groups, Guilds & Social

**GetGroup**
Returns the current group (mutable form).

**GetGroup#2**
Returns the current group (const form).

**GetGroupRef**
The group reference link used by the group's member list.

**GetSubGroup**
Returns the raid subgroup index.

**SetGroup**
Binds/unbinds the player to a group (optional subgroup).

**GetGroupInvite**
Returns the pending group invite.

**SetGroupInvite**
Stores the pending group invite.

**UninviteFromGroup**
Withdraws this player's pending invite.

**RemoveFromGroup**
Member convenience form removing self from the current group via the static overload.

**RemoveFromGroup#2**
Static removal of a guid from a group — used by deletion, kicks, and logout teardown.

**CanUninviteFromGroup**
Kick-rights validation: leader or assistant, and not inside a battleground.

**UpdateGroupLeaderFlag**
Sets/clears the leader UI flag byte on the player.

**IsInSameGroupWith**
True when both players share a party (same subgroup for raids).

**IsInSameRaidWith**
True when both players share the same group/raid object.

**IsGroupVisibleFor**
Group visibility rule used by stealth/invisibility group exceptions.

**GetNextRandomRaidMember**
Random eligible raid member within a radius (spell target selection).

**SendDestroyGroupMembers**
Client-side despawn of group member objects on ungroup (optionally including self).

**SendUpdateToOutOfRangeGroupMembers**
Flushes party-frame deltas to out-of-range members from the maintained masks.

**GetGroupUpdateFlag**
Reads the pending group-update mask.

**SetGroupUpdateFlag**
ORs a flag into the group-update mask.

**GetAuraUpdateMask**
Reads the pending aura-slot update mask for party frames.

**SetAuraUpdateSlot**
Marks one aura slot dirty in the mask.

**SetAuraUpdateMask**
Writes the whole aura-update mask.

**SetBattleGroundRaid**
Swaps the player into the BG raid while keeping the world group intact.

**RemoveFromBattleGroundRaid**
Restores the original world group after the BG raid.

**GetOriginalGroup**
Returns the preserved world group during a BG raid.

**GetOriginalGroupRef**
The reference link for the preserved world group.

**GetOriginalSubGroup**
Subgroup index in the preserved world group.

**SetOriginalGroup**
Stores the preserved world group (optional subgroup).

**SetLFGAreaId**
Sets the looking-for-group area id.

**GetLFGAreaId**
Reads the LFG area id.

**IsInLFG**
True when an LFG area is set.

**LeaveLFGChannel**
Leaves the LFG channel when LFG state ends.

**SetInGuild**
Writes the guild id field (`PLAYER_GUILDID`).

**SetRank**
Writes the guild rank field.

**GetGuildId**
Reads the guild id field.

**GetRank**
Reads the guild rank field.

**SetGuildIdInvited**
Stores the pending guild invite id.

**GetGuildIdInvited**
Reads the pending guild invite id.

**RemovePetitionsAndSigns**
Static petition/signature teardown by guid: charter cleanup on deletion and signature invalidation.

**GetSocial**
MasterPlayer-side social list (asserting form).

**FindSocial**
MasterPlayer-side social list (nullable form).

**IsAllowedWhisperFrom**
Friends-only whisper filter check against a sender guid.

**SetWhisperRestriction**
Toggles the friends-only whisper restriction extra-flag.

**IsEnabledWhisperRestriction**
Reads the whisper-restriction extra-flag.

**SetAcceptWhispers**
Toggles GM whisper acceptance.

**IsAcceptWhispers**
Reads the GM whisper-acceptance flag.

**GetExtraFlags**
Raw access to the extra-flags word.

### Chat & Channels

**Say**
Standard say broadcast (interfaction config respected).

**Yell**
Yell broadcast with the zone-scaled yell radius.

**TextEmote**
Text emote broadcast.

**GetYellRange**
Zone-scaled yell radius.

**SendSysMessage**
System message from a raw string (builds and sends the chat packet).

**SendSysMessage#2**
System message from a mangos string id (resolves the localized text first).

**PSendSysMessage**
printf-form system message from a raw format string.

**PSendSysMessage#2**
printf-form system message from a mangos string id.

**CanSpeak**
Mute check against the session's mute time.

**GetChatTag**
The AFK/DND/GM chat-tag byte for chat packets.

**ToggleAFK**
Toggles AFK (auto-cleared in battlegrounds); returns the new state.

**ToggleDND**
Toggles DND; returns the new state.

**IsAFK**
Reads the AFK player flag.

**IsDND**
Reads the DND player flag.

**GetName**
The character name (final override).

**SetName**
Sets the character name (rename flow).

**JoinedChannel**
Adds a channel to the joined list.

**LeftChannel**
Removes a channel from the joined list.

**CleanupChannels**
Leaves every channel at logout.

**UpdateLocalChannels**
Re-homes built-in zone channels on zone change.

**LearnLanguage**
Adds a language to the known-languages bitmask.

**RemoveLanguage**
Removes a language from the bitmask.

**KnowsLanguage**
Tests the known-languages bitmask (chat filtering).

### Reputation & Faction

**GetTeam**
The cached faction team (final override).

**GetTeamId**
Team as a `TeamId` index.

**TeamForRace**
Static race→team DBC lookup.

**GetFactionForRace**
Static race→faction-template DBC lookup.

**SetFactionForRace**
Caches the team and sets the faction template from race.

**GetReputationMgr**
Mutable access to the `ReputationMgr`.

**GetReputationMgr#2**
Const access to the `ReputationMgr`.

**GetReputationRank**
Rank with a faction by id.

**CalculateReputationGain**
Rate/config/aura-scaled reputation gain computation for a source and base value.

**RewardReputation**
Quest-source reputation grant, spillover-mask aware.

**RewardReputation#2**
Kill-source reputation grant — team-spillover aware, pet/player victim filtered.

**SetTemporaryAtWarWithFaction**
Marks a faction scripted-at-war for the session window.

**ClearTemporaryWarWithFactions**
Resets scripted at-war bits and notifies the client.

**SendFactionAtWar**
Sends the at-war flag change for one reputation row.

### PvP, Duels & Honor

**UpdatePvP**
Arms or clears PvP state on the 5-minute drop timer (overriding form forces).

**UpdatePvPContested**
Arms or clears contested-PvP state with its grace timer.

**UpdatePvPFlagTimer**
Ticks the PvP drop timer.

**UpdatePvPContestedFlagTimer**
Ticks the contested grace timer.

**SetPvPDesired**
Sets the persistent manual PvP flag.

**IsPvPDesired**
Reads the manual PvP flag.

**SetFFAPvP**
Sets/clears free-for-all PvP state.

**IsFFAPvP**
Reads the free-for-all flag.

**IsInInterFactionMode**
True under the cross-faction interaction toggle.

**IsOutdoorPvPActive**
Outdoor-PvP objective eligibility (PvP-flagged, alive, not in flight…).

**IsInDuelWith**
True when dueling the given player and the duel has started.

**UpdateDuelFlag**
Starts the duel when the countdown reaches zero (flag activation time).

**CheckDuelDistance**
The 10-second out-of-bounds forfeit beyond 75 yards from the duel flag, transport-aware.

**DuelComplete**
Duel resolution: flags, combat stop, duel-period aura cleanup, the beg emote, arbiter removal.

**SendDuelCountdown**
Sends the duel countdown packet.

**RewardHonor**
Honor for a kill victim, group-size weighted; dishonorable (civilian) and racial-leader honor are patch/config-gated under the accurate-timeline option.

**RewardHonorOnDeath**
Death-time honor distribution from the last minute of damage history — split per attacking group weighted by damage (members alive, in range, hostile) and per solo attacker.

**IsHonorOrXPTarget**
The gray-level filter: does this victim yield honor/XP.

**GetHonorMgr**
Mutable access to the `HonorMgr`.

**GetHonorMgr#2**
Const access to the `HonorMgr`.

**IsCityProtector**
True when holding the city-protector title.

**SetCityTitle**
Grants the city-protector title.

**RemoveCityTitle**
Removes the city-protector title.

### Battlegrounds

**InBattleGround**
True while a BG instance id is set in the BG data.

**GetBattleGroundId**
The current BG instance id.

**GetBattleGroundTypeId**
The current BG type id.

**GetBattleGround**
Resolves the `BattleGround*` object for the stored identity.

**SetBattleGroundId**
Sets the BG instance and type ids (marks BG data for save).

**InBattleGroundQueue**
True when any queue slot is occupied.

**GetQueuedBattleground**
The first occupied queue slot's type id.

**GetBattleGroundQueueTypeId**
Reads a queue slot by index.

**GetBattleGroundQueueIndex**
Finds the slot index of a queue type.

**IsInvitedForBattleGroundQueueType**
True when the queue slot carries an invite.

**IsInvitedForBattleGroundInstance**
True when invited to a specific BG instance id.

**InBattleGroundQueueForBattleGroundQueueType**
Membership test for a specific queue type.

**AddBattleGroundQueueId**
Occupies a free queue slot with the type; returns the slot index.

**RemoveBattleGroundQueueId**
Frees the queue slot holding the type.

**SetInviteForBattleGroundQueueType**
Stamps an instance invite onto the queue slot.

**HasFreeBattleGroundQueueId**
True when a queue slot is free.

**GetMinLevelForBattleGroundBracketId**
Static bracket→minimum-level math for a BG type.

**GetMaxLevelForBattleGroundBracketId**
Static bracket→maximum-level math for a BG type.

**GetBattleGroundBracketIdFromLevel**
Member form of the bracket mapping using the player's own level.

**GetBattleGroundBracketIdFromLevel#2**
Static level→bracket math for a BG type from the template's level bounds.

**GetBGAccessByLevel**
The level gate for a BG type.

**CanJoinToBattleground**
Deserter-aware join eligibility.

**CanUseBattleGroundObject**
Flag-pickup preconditions: not stealthed, invulnerable, or mounted, alive and in range.

**SendRaidGroupOnlyError**
Sends the raid-only-map refusal packet.

**SetBattleGroundEntryPoint**
Leader-relative entry-point capture — taxi/dungeon/portal aware, with homebind fallback.

**SetBattleGroundEntryPoint#2**
Stores an explicit entry point (map id + coordinates).

**GetBattleGroundEntryPoint**
Reads the stored entry point.

**LeaveBattleground**
BG removal with deserter handling and the optional entry-point teleport.

**SetBGTeam**
Cross-faction team assignment inside the BG (marks BG data for save).

**GetBGTeam**
The effective BG team — the assigned team, else the real team.

### Instances

**GetBoundInstance**
Looks up the bind for a map in the mutex-guarded bind table.

**GetBoundInstances**
Direct access to the per-map bind table.

**BindToInstance**
Creates or upgrades a bind against a `DungeonPersistentState`; permanent binds are persisted to `character_instance`.

**UnbindInstance**
Iterator-form bind removal: detaches from the persistent state and deletes the `character_instance` row.

**UnbindInstance#2**
Map-id form of bind removal (resolves the bind, then the iterator worker).

**GetBoundInstanceSaveForSelfOrGroup**
Entry resolution: own permanent bind preferred over the group's bind.

**ConvertInstancesToGroup**
Static solo-to-group bind migration, writing `group_instance` rows.

**ResetInstances**
Manual/on-change reset sweep over all binds with client feedback.

**ResetInstance**
Resets one bind (iterator form) for the given method.

**ResetPersonalInstanceOnLeaveDungeon**
Resets the personal instance when leaving a dungeon (elastic-instancing housekeeping).

**SendResetInstanceSuccess**
Sends the reset-succeeded packet for a map.

**SendResetInstanceFailed**
Sends the reset-failed packet with reason.

**SendResetFailedNotify**
Sends the generic reset-failed notify.

**SendRaidInfo**
Sends the raid-info UI (permanent binds with reset times).

**SendSavedInstances**
Sends the saved-instances handshake at login.

**SendInstanceResetWarning**
Sends a scheduled-reset warning for a map.

**SendTransferAborted**
Sends the map-entry refusal with reason.

**CheckInstanceCount**
Enforces the instances-per-hour cap.

**AddInstanceEnterTime**
Records an instance entry time for the per-hour cap.

**UpdateHomebindTime**
The 60-second not-bound-here eviction timer while inside a foreign instance.

**SetAutoInstanceSwitch**
Toggles elastic continent-instance switching for this player.

**GetSmartInstanceBindingMode**
Reads the smart-rebind toggle.

**SetSmartInstanceBindingMode**
Sets the smart-rebind toggle.

### Pets

**RemovePet**
Dismisses the pet with the given `PetSaveMode` (stable, current slot, delete…).

**RemoveMiniPet**
Dismisses the companion mini-pet.

**GetMiniPet**
Resolves the companion from its stored guid.

**_SetMiniPet**
Stores/clears the companion guid.

**AutoReSummonPet**
Resummons the pet after a state that stashed it (bot/utility path).

**UnsummonPetTemporaryIfAny**
Stashes the pet while mounted/teleporting/flying, remembering its number.

**ResummonPetTemporaryUnSummonedIfAny**
Restores the stashed pet after landing/dismount.

**IsPetNeedBeTemporaryUnsummoned**
True in states that require the pet stashed (mounted, taxi, not in world).

**GetTemporaryUnsummonedPetNumber**
Reads the stashed pet number.

**SetTemporaryUnsummonedPetNumber**
Writes the stashed pet number.

**PetSpellInitialize**
Sends the pet action bar/spells packet.

**CharmSpellInitialize**
Sends the charm action bar packet.

**PossessSpellInitialize**
Sends the possession action bar packet.

**RemovePetActionBar**
Clears the pet action bar client-side.

**SummonPossessedMinion**
Summons a possessed minion with camera/control transfer to it.

**UnsummonPossessedMinion**
Tears the possessed minion down and returns camera/control.

**ModPossessPet**
Toggles possession of the own pet (aura-driven), with remove-mode awareness.

**SendPetTameFailure**
Sends the tame-failure reason packet.

**SendPetSkillWipeConfirm**
Sends the pet untrain confirmation with cost.

### Taxi & Mounts

**ActivateTaxiPathTo**
Validated flight activation by node chain: death/combat/trade/stealth/shapeshift/cast checks (unless `nocheck`, taxi-cheat aware), endpoint knowledge, pricing with reputation discount, charging, and starting the flight-path movement generator.

**ActivateTaxiPathTo#2**
Stored-path-id form of flight activation (resolves the node pair from the path).

**TaxiStepFinished**
Multi-hop advancement — charging each leg — or landing: pet resummon, anticheat notification, and a clean control handoff for bots.

**ContinueTaxiFlight**
Mid-flight resume at login from the nearest node of the saved path.

**GetTaxi**
Mutable access to the `PlayerTaxi` mask/state.

**GetTaxi#2**
Const access to the `PlayerTaxi` state.

**InitTaxiNodes**
Seeds the race/level default taxi nodes.

**Mount**
Mount transition: stashes the pet (config-dependent) and applies the mount display; returns the result code.

**Unmount**
Dismount transition (aura-driven form aware); returns the result code.

**SendMountResult**
Sends the mount outcome packet.

**SendDismountResult**
Sends the dismount outcome packet.

### Rest

**SetRestBonus**
Clamped rest storage that also maintains the rested-state bytes and character flag.

**ComputeRest**
Elapsed-time→rest conversion: offline city/tavern at the full rate versus the slow trickle.

**GetXPRestBonus**
Consumes rest for kill XP at the 2× rate; returns the bonus applied.

**SetRestType**
Switches none/tavern/city resting, arming the inn area-trigger id.

**GetRestType**
Reads the rest type.

**GetRestBonus**
Reads the stored rest bonus.

**IsRested**
True after 10 s of continuous rest time.

**GetRestTime**
Reads the accumulated rest time.

**SetRestTime**
Writes the accumulated rest time.

**GetTimeInnEnter**
Reads the inn-enter timestamp.

**UpdateInnerTime**
Writes the inn-enter timestamp.

### Environment & Mirror Timers

**EnvironmentalDamage**
Deals typed environmental damage: fire-school absorb/resist for lava and fire, nature for slime, plain for exhaustion/drowning/fall (absorb only on ≤1.6 builds, matching the 1.7 change), sends the damage log, and applies the 10% durability loss when the damage kills.

**UpdateMirrorTimers**
Per-tick servicing of the breath/fatigue/feign/environmental mirror timers, with client sync.

**CheckMirrorTimerActivation**
Whether a timer type should start running (state preconditions).

**CheckMirrorTimerDeactivation**
Whether a running timer type should stop.

**OnMirrorTimerExpirationPulse**
The damage pulse once a bar empties (drowning, fatigue, lava).

**GetMirrorTimerMaxDuration**
The full duration for a timer type — breath from `GetWaterBreathingInterval` scaled by `SetWaterBreathingIntervalMultiplier`.

**GetMirrorTimerBuff**
The aura holder scaling a timer (water-breathing style buffs).

**FreezeMirrorTimers**
Pauses/resumes all timers (feign death).

**SendMirrorTimerStart**
Sends a timer-start packet (type, remaining, duration, scale, paused).

**SendMirrorTimerStop**
Sends a timer-stop packet.

**SendMirrorTimerPause**
Sends a timer pause/resume packet.

**SendMirrorTimers**
Re-sends all active timers (login/forced refresh).

**SetEnvironmentFlags**
Sets/clears a liquid/underwater flag byte with enter/exit side effects.

**IsUnderwater**
Reads the underwater flag.

**IsInWater**
Reads the in-water flag.

**IsInMagma**
Reads the in-magma flag.

**IsInHighSea**
Reads the high-sea (fatigue) flag.

**IsInHighLiquid**
Reads the deep-liquid flag.

### GM Tools, Cheats & Tickets

**SetGameMaster**
The full GM toggle: GM faction swap, PvP immunity, combat drop, and visibility rebuild of GM-invisible units through the file-local camera functors **SetGameMasterOnHelper**/**SetGameMasterOffHelper** and their call operators **operator()#3**/**operator()#2**.

**IsGameMaster**
True when GM mode (`PLAYER_EXTRA_GM_ON`) is active.

**SetGMChat**
Toggles the GM chat tag (moderator+), with optional chat notification.

**IsGMChat**
Reads the GM chat-tag flag (security-gated).

**SetGMVisible**
Toggles GM invisibility, with optional notification.

**IsGMVisible**
True unless GM-invisible.

**GetGMInvisibilityLevel**
Reads the GM invisibility tier (which security levels can still see the GM).

**SetGMInvisibilityLevel**
Sets the GM invisibility tier.

**SetTaxiCheater**
Toggles the all-taxi-nodes cheat extra-flag.

**IsTaxiCheater**
Reads the taxi-cheat flag.

**SetAcceptTicket**
Toggles GM ticket acceptance (extra-flag).

**IsAcceptTickets**
True when a GM-security session has ticket acceptance on.

**GetGMTicketCounter**
Reads the last-seen ticket counter for GM ticket routing.

**SetGMTicketCounter**
Writes the ticket routing counter.

**SetPvPDeath**
Marks the next death as a PvP death (corpse typing extra-flag).

**SetCheatFly**
GM fly cheat — sends the client movement packets; optional notification.

**SetCheatFixedZ**
Fixed-Z cheat toggle (no falling).

**SetCheatBeastmaster**
Beastmaster cheat — tame anything (adjusts unit flags).

**SetCheatGod**
God mode — untargetable/invulnerable unit-flag adjustment.

**SetCheatNoCooldown**
No-cooldown cheat toggle.

**SetCheatInstantCast**
Instant-cast cheat toggle.

**SetCheatNoPowerCost**
No-power-cost cheat toggle.

**SetCheatDebuffImmunity**
Debuff-immunity cheat toggle.

**SetCheatAlwaysCrit**
Always-crit cheat toggle.

**SetCheatNoCastCheck**
Skip-cast-checks cheat toggle.

**SetCheatAlwaysProc**
Always-proc cheat toggle.

**SetCheatTriggerPass**
Pass-area-triggers cheat toggle.

**SetCheatIgnoreTriggers**
Ignore-area-triggers cheat toggle.

**SetCheatDebugTargetInfo**
Debug-target-info cheat toggle.

**GetCheatOptions**
Reads the raw cheat-option bitmask.

**HasCheatOption**
Tests one cheat-option bit.

**EnableCheatOption**
Sets a cheat-option bit.

**RemoveCheatOption**
Clears a cheat-option bit.

**SetCheatOption**
Sets or clears a cheat-option bit by flag.

**GetCheatData**
The session's `MovementAnticheat` instance.

**GetSelectedGobj**
Reads the GM-selected gameobject guid.

**SetSelectedGobj**
Stores the GM-selected gameobject guid.

**SetEscortingGuid**
Stores the escort-debug guid.

**GetEscortingGuid**
Reads the escort-debug guid.

### Targets, Interaction & Visibility

**GetSelectionGuid**
Reads the current selection guid.

**SetSelectionGuid**
Sets the selection guid and mirrors it into the Unit target guid.

**GetSelectedUnit**
Resolves the selection as a `Unit*`.

**GetSelectedCreature**
Resolves the selection as a `Creature*`.

**GetSelectedPlayer**
Resolves the selection as a `Player*`.

**GetObjectByTypeMask**
Typed object resolution by guid against a `TypeMask`.

**CanInteractWithQuestGiver**
Interaction gate for a quest source (creature, GO, or item).

**GetNPCIfCanInteractWith**
Resolves an NPC by guid if interactable under the npcflag mask (alive, in range, no hostility).

**CanInteractWithNPC**
The npcflag-mask interaction test against a resolved creature.

**GetGameObjectIfCanInteractWith**
Resolves a GO by guid if interactable for the type within activation range.

**CanInteractWithGameObject**
The GO interaction test by type and activation range.

**FindNearestInteractableNpcWithFlag**
Nearest interactable NPC carrying the npcflags.

**CanUseBank**
Banker-proximity check that remembers the current banker guid.

**CanSeeHealthOf**
Health-information rights over a target (beast lore / party rules).

**CanSeeSpecialInfoOf**
Special-info (beast lore) rights over a target.

**UpdateVisibilityOf**
Per-target visible-set reconciliation: add/remove the target for this viewer with the file-local insert helper (which also marks quest-activated gameobjects) and broadcaster registration.

**UpdateVisibilityOf#2**
Packet-building template form of the reconciliation, batching create/destroy blocks into an `UpdateData`.

**UpdateVisibilityOf_helper**
File-local insert helper for the visible set that also marks quest-activated gameobjects.

**AddBroadcastListener**
File-local registration of this player's broadcaster with a target's fan-out list.

**RemoveBroadcastListener**
File-local removal of the broadcaster registration.

**IsInVisibleList**
Lock-guarded membership test of the visible set.

**IsInVisibleList_Unsafe**
Lock-free membership test (caller holds the lock or accepts the race).

**IsVisibleInGridForPlayer**
Grid visibility answer: ghosts see spirit healers, GM tiers respected.

**IsVisibleGloballyFor**
Who-list visibility for a viewer (GM tiers, invisibility).

**RefreshBitsForVisibleUnits**
Re-marks update-mask bits for visible units after dynamic-flag changes.

**LeaveCombatWithFarAwayCreatures**
Leash-range threat dropping against far-away creatures on continents.

**HandleStealthedUnitsDetection**
The periodic stealth-reveal pulse against nearby stealthed units.

**GetCamera**
The player's `Camera` object.

**ScheduleCameraUpdate**
Defers the `PLAYER_FARSIGHT` write for a clean client view transfer.

**GetFarSightGuid**
Reads the far-sight guid field.

**SetLongSight**
Arms Eagle-Eye style sight-range extension from an aura.

**UpdateLongSight**
Applies the extended sight range to the camera position.

**GetLongSight**
Reads the long-sight spell id.

**SetWorldMask**
Nostalrius phasing-mask override, refreshing visibility.

**CanAutoAttackTarget**
Auto-attack target validation returning a typed result.

**GetLastSwingErrorMsg**
Reads the last swing-refusal result (dedup for messaging).

**SetSwingErrorMsg**
Stores the last swing-refusal result.

**SendAttackSwingNotInRange**
Sends the not-in-range swing error.

**SendAttackSwingNotStanding**
Sends the not-standing swing error.

**SendAttackSwingDeadTarget**
Sends the dead-target swing error.

**SendAttackSwingCantAttack**
Sends the can't-attack swing error.

**SendAttackSwingCancelAttack**
Sends the cancel-attack packet.

**SendAttackSwingBadFacingAttack**
Sends the bad-facing swing error.

**SendAutoRepeatCancel**
Cancels auto-shot client-side.

**SendFeignDeathResisted**
Sends the feign-death-resisted notification.

**SetCannotBeDetectedTimer**
Arms the scripted no-aggro window.

**CanBeDetected**
False while the no-aggro window runs.

**AddComboPoints**
Adds combo points on a target with client sync (retargeting resets).

**ClearComboPoints**
Clears combo points and syncs the client.

**SetComboPoints**
Pushes the current combo state to the client field.

**GetComboPoints**
Reads the combo point count.

**GetComboTargetGuid**
Reads the combo target guid.

**SendMessageToSet**
Nearby broadcast of a packet (optionally to self) via the broadcaster.

**SendMessageToSetInRange**
Radius-limited nearby broadcast.

**SendMessageToSetInRange#2**
Own-team-only radius broadcast.

**SendDirectMessage**
Direct send to this player's session.

**SendInitWorldStates**
Zone-entry world states: BG scoreboards, war effort, invasion counters.

**SendUpdateWorldState**
Single world-state value update.

**DeletePacketBroadcaster**
Tears down the per-player packet fan-out object.

**CreatePacketBroadcaster**
Creates the per-player packet fan-out object.

**GetPacketBroadcaster**
Shared handle to the broadcaster.

**SetDrunkValue**
Applies inebriation: visual byte, invisibility-detection side effects, and source-item tracking.

**GetDrunkValue**
Reads the drunk value.

**GetDrunkenstateByValue**
Static classifier of a drunk value into sober/tipsy/drunk/smashed.

**HandleSobering**
Decays drunkenness by 256 per 10-second pulse.

**GetAuctionAccessMode**
Reads which faction's auction house is usable (own/enemy/goblin) from the extra flags.

**SetAuctionAccessMode**
Sets the auction-house access extra-flag bits.

**ScheduleStandUp**
Defers a stand-up to the next update (packet-order correctness).

**IsStandUpScheduled**
Reads the deferred stand-up flag.

**ClearScheduledStandUp**
Clears the deferred stand-up flag.

**IsActionButtonDataValid**
Static validation of an action-button payload (spell known, item exists…).

**HasScheduledEvent**
True when the player's event queue has pending events.

**CanWalk**
Capability answer: always true for players.

**CanSwim**
Capability answer: always true for players.

**CanFly**
Capability answer: true while GM-flying.

**SetFly**
Applies/removes the fly movement state (GM fly).

**BuildCreateUpdateBlockForPlayer**
Viewer-specific create block, including held/visible items.

**DestroyForPlayer**
Viewer-specific destroy block, including the item objects.

**CinematicStart**
Starts a cinematic: spawns the invisible ghost camera flown along `CinematicWaypointEntry` paths to stream cells around the camera.

**CinematicEnd**
Ends the cinematic and despawns the ghost camera.

**SendCinematicStart**
Sends the cinematic-start packet for a sequence id.

**GetCurrentCinematicEntry**
Reads the running cinematic entry.

**UpdateCinematic**
Advances the ghost camera along the waypoint path each tick.

### Race Change (custom)

**ChangeRace**
A live race/faction conversion, refused for bots and save-disabled characters:

*   Converts spells (**ChangeSpellsForRace**, via **ConvertSpell** and the file-local **GetPriestSpellForRace** racial-priest table), swaps race bytes, relearns defaults, re-teams.
*   Converts reputations (**ChangeReputationsForRace**, mirroring capital standings via the file-local **GetCapitalReputationForRace** and swapping faction-pair standings), quests (**ChangeQuestsForRace**, remapping faction-specific chains), and items (**ChangeItemsForRace**, applying the cross-faction item table to every container including bank and mail).
*   Unmounts faction mounts, saves once atomically, and on a team change rewrites homebind to the new capital and clears now-invalid instance binds. Saving stays disabled throughout so a crash cannot persist a half-converted character.

## Cross-Unit Boundaries

*   **Unit.Main / SpellCaster / WorldObject.Object / Object:**
    *   *Relationship:* the inheritance chain. Nearly every member reads or writes base-class state (`GetLevel`, `SetHealth`, position, update fields).
    *   *Calls Into:* `Update` wraps `Unit::Update`; `SetDeathState`/`CleanupsBeforeDelete` extend the Unit versions; damage flows through `SpellCaster/DealDamageMods` and `Unit.Main/DealDamage` (as in `EnvironmentalDamage`).
*   **Player.StatSystem** (sibling partial, StatSystem.cpp):
    *   *Calls Into:* `Create` and login use `UpdateMaxHealth#2`/`UpdateMaxPower#2`; `GiveLevel` and `LoadFromDB` invoke `UpdateAllStats`; level math uses `GetHealthBonusFromStamina`/`GetManaBonusFromIntellect`; equipment changes trigger the `UpdateStats`/`UpdateArmor`/`UpdateDamagePhysical` family — all implemented in Player.StatSystem.
    *   *Reason:* the stat recalculation formulas live there; the aura bookkeeping they read (base mods, stat buffs) is stored here.
*   **WorldSession.* (handler partials):**
    *   *Called By:* the largest caller population. `WorldSession.CharacterHandler/HandlePlayerLogin` drives construction and `LoadFromDB`; movement, item, quest, spell, trade, NPC, taxi, group, and BG handlers call the corresponding public members; `WorldSession.Main/LogoutPlayer` runs the teardown.
    *   *Calls Into:* outbound traffic goes back through `WorldSession.Main/SendPacket` and the `PlayerBroadcaster`.
*   **Map.Main / MapManager / MapPersistentStateMgr:**
    *   *Called By:* the map calls `Update` and `Map.Main/Remove#3`.
    *   *Calls Into:* teleports are brokered by `MapManager` (`ScheduleFarTeleport`, `CancelDelayedPlayerTeleport`, `CreateMap`, `GetContinentInstanceId`, `CanPlayerEnter`); instance binding talks to `DungeonPersistentState`.
*   **ObjectMgr:**
    *   *Calls Into:* the static-data source everywhere — `GetPlayerInfo`, quest templates, taxi nodes/paths, graveyards, area triggers, faction entries, the player cache, guid generation.
*   **PlayerBotAI / CombatBotBaseAI / PartyBotAI / PlayerBotMgr:**
    *   *Called By:* `PlayerBotAI/SpawnNewPlayer` calls the constructor, `Create`, and `SelectRandomAppearance`; `CombatBotBaseAI` gears bots through `AddStartingItems`/`StoreNewItemInBestSlots`/`SatisfyItemRequirements`; `PartyBotAI/CloneFromPlayer` clones gear the same way.
    *   *Reason:* the bot layer drives full `Player` objects through the same code paths as real clients; `LoadFromDB` gives the bot AI a `BeforeAddToMap` hook, and `SaveToDB` is a deliberate no-op for them.
*   **AiBotAI.* (the Barrens Chat fleet):**
    *   *Called By:* quest flows (`AddQuest`, `CanTakeQuest`, `RewardQuest`, the `SatisfyQuest*` set), inventory (`GetItemByPos`, `DestroyItem`, `AutoStoreLoot`), economy (`GetMoney`, `ModifyMoney`, `DurabilityRepairAll`), death handling (`ResurrectPlayer`, `SpawnCorpseBones`), chat (`Say`, `Yell`), training (`LearnSpell`, `GetTrainerSpellState`, `IsSpellFitByClassAndRace`), and flight (`ActivateTaxiPathTo`, `GetTaxi`).
*   **HonorMgr / ReputationMgr:**
    *   *Relationship:* owned subsystems — honor rank math and CP persistence, reputation standings. This unit feeds them (`RewardHonor*`, `RewardReputation*`, load/save) and persists their scalar block in `characters`.
*   **Group / Guild / GuildMgr / SocialMgr / MasterPlayer:**
    *   *Calls Into:* group membership and party updates, guild fields and deletion cleanup, and the master-server-side social list reached through `GetSocial`.
*   **BattleGround / BattleGroundMgr / BattleGroundAV:**
    *   *Calls Into:* queueing, brackets, entry points, leave handling, and the Alterac Valley quest-completion hook inside `RewardQuest`.
*   **SpellMgr / Spell / SpellModifier / SpellAuraHolder / DBCStores:**
    *   *Calls Into:* spell entries and rank chains, cast execution, talent modifiers, aura holders for load/save, and the DBC stores backing appearance validation, skills, taxis, and races.
*   **Item / Bag / ItemPrototype / LootMgr / TradeData / MailDraft:**
    *   *Calls Into:* item objects and their DB rows, container iteration, prototypes, loot-template filling, the trade session, and mail delivery for quest rewards, deletion returns, and overflow items.
*   **Log.Main / World:**
    *   *Calls Into:* file/console logging and configuration; `World` also carries the money-trade log and realm-wide GM announcements.
    *   *Relationship:* the `Log::Player` DB/file logging overloads are physically defined in this TU (see Notable Implementation Details).
*   **ScriptMgr / ZoneScriptMgr / GameEventMgr / scourge_invasion / world_event_wareffort:**
    *   *Calls Into:* quest accept/reward scripts, zone enter/leave scripts, and world-event world-state feeds (`SendInitWorldStates`, `OnReceivedItem`).
*   **Anticheat (MovementAnticheat):**
    *   *Calls Into:* per-tick evaluation with sanctioning in `Update`, death notification, teleport/movement logging, safe-position tracking.
*   **ChannelMgr / Channel / Chat:**
    *   *Calls Into:* zone chat channel membership and packet building.
*   **Camera / PlayerBroadcaster / MovementBroadcaster / MovementPacketSender:**
    *   *Calls Into:* view management, the per-player packet fan-out object created at login, and controller-aware teleport/movement packets.
*   **TicketMgr / AccountMgr:**
    *   *Calls Into:* ticket counters and account security levels for the GM feature set.

## Data Model

This unit is the primary writer of the character database. All access goes through `CharacterDatabase` (and `LogsDatabase` for `logs_player`) using prepared statements inside per-guid transactions. Verified column definitions are in the machine-true SCHEMA section at the end of this document.

*   **`characters`** — the spine. `LoadFromDB` reads the full row (column order documented in a comment above the query); `SaveToDB` REPLACEs it wholesale: identity/appearance, `level`/`xp`/`money`, position and `map`/`instance` (or the teleport destination mid-teleport), transport columns, `known_taxi_mask`/`current_taxi_path`, `online`, played time, `rest_bonus`/`logout_time`, talent-reset bookkeeping, `extra_flags`/`stable_slots`/`character_flags`, `death_expire_time`, the seven honor columns, `watched_faction`, `drunk`, saved `health`/`power1..5`, `explored_zones`, `equipment_cache` (visible gear for the character list — `BuildEnumData` never joins `item_instance`), `ammo_id`, `action_bars`, `world_phase_mask`, `create_time`. `SaveNewPlayer` inserts a reduced new-character row; `DeleteFromDB` deletes it or unlinks it into `deleted_account`/`deleted_name`/`deleted_time`; static readers pull single columns.
*   **`character_inventory` + `item_instance`** — inventory layout and item rows (`_LoadInventory`/`_SaveInventory`); deletion sweeps both, and broken items are archived to **`character_deleted_items`** and mailed back.
*   **`item_loot`** — generated-but-unopened container loot (`_LoadItemLoot`).
*   **`character_spell`**, **`character_spell_cooldown`** — the spellbook and unexpired cooldowns (`spell_expire_time`/`category_expire_time`/`item_id` exactly as the schema shows).
*   **`character_aura`** — restorable aura holders with remaining durations.
*   **`character_skills`** (+ **`character_forgotten_skills`**) — skill lines, plus the 1.10.2+ weapon-skill memory.
*   **`character_queststatus`** — the quest log (delta-saved by uState).
*   **`character_reputation`**, **`character_honor_cp`** — persisted via the owned managers.
*   **`character_homebind`**, **`character_battleground_data`** — homebind location and BG identity/entry point.
*   **`character_instance`** + **`group_instance`** — permanent instance binds, and the solo-to-group migration written by `ConvertInstancesToGroup`.
*   **`character_stats`** — the `_SaveStats` armory-style dump matching the verified column list (config-gated to logout).
*   **`character_social`** — read on the master-player side; deletion cleans it.
*   **`mail`** / **`mail_items`** — COD returns and item hand-off during deletion, reward mail via `MailDraft`, and overflow-item delivery.
*   **`guild_member`** / **`guild_eventlog`** — membership reads and deletion cleanup (the event log is touched through the Guild object).
*   **`petition`** / **`petition_sign`** — charter ownership and signatures, torn down by `RemovePetitionsAndSigns`.
*   **`logs_player`** — player-action auditing inserted by `PlayerLogToDB`, with the `type` enum from the schema.

## Notable Implementation Details

*   **The delayed-teleport window.** `Update` brackets `Unit::Update`/AI with `SetCanDelayTeleport(true/false)`; `TeleportTo` called inside that window parks the destination instead of executing, and `IsHasDelayedTeleport` deliberately refuses to fire for a player who died after arming (`m_bHasBeenAliveAtDelayedTeleport`) so a ghost is never yanked back from the graveyard. Far teleports are never executed inline at all — they are scheduled through the map manager and run between map updates in `ExecuteTeleportFar`, because removing a player mid-map-update is unsafe.
*   **Bots never save.** `m_saveDisabled` (set for temporary bots and during `ChangeRace`) short-circuits `SaveToDB` at the top; corpses of save-disabled characters are also not persisted. This is the exact mechanism that lets the Barrens Chat fleet run full `Player` objects with zero database footprint.
*   **One giant REPLACE.** The character row is saved as a single REPLACE statement with ~60 bound parameters inside a per-guid transaction, followed by the delta-based `_Save*` passes. `equipment_cache` denormalizes visible gear purely so the character-select screen never has to join `item_instance`.
*   **Login self-repair.** `LoadFromDB` is defensive end to end: invalid coordinates → homebind; saved-in-BG → entry point; dead instance bind → entrance trigger (with a hardcoded Naxxramas exit because vanilla has no exit trigger); bad transport offsets (>250 units) → homebind; broken taxi strings → flight source node; stale quest item counters recounted from inventory; unloadable items mailed back via `character_deleted_items` instead of destroyed; drunkenness decayed by offline time (sober after 15 minutes).
*   **Riding-skill migration.** `UpdateOldRidingSkillToNew` performs the 1.12 conversion of per-mount-type skills into Apprentice/Journeyman Riding once per character, keyed off a character flag, with the tier decided by whether `_LoadInventory` saw an epic mount.
*   **Soft delete.** `CONFIG_UINT32_CHARDELETE_METHOD` 1 unlinks characters (moving `account`/`name` into `deleted_*` columns) instead of deleting, level-gated, with `DeleteOldCharacters` as the reaper — and full deletion still returns COD mail to senders first.
*   **Money is audited.** `ModifyMoney` clamps into `[0, GetMaxMoney()]` (trial accounts get a far lower cap), and `LogModifyMoney` routes every significant change through the money-trades log with counterparty guids; GM transactions are logged regardless of threshold.
*   **`ApplySpellMod`'s -100% rule.** A percent cast-time mod of -100 (Nature's Swiftness) zeroes the cast outright, discarding accumulated flat increases — the Nostalrius fix for Barkskin + Nature's Swiftness leaving a 1-second cast — and on 1.11+ builds Nature's Grace charges are not consumed by casts already made instant.
*   **Elastic continent instancing.** `Update` polls `GetContinentInstanceId` and schedules an instance switch when the player crosses a continent-shard boundary (deferred while in combat at a transition), a Nostalrius-era scalability feature most vanilla cores lack.
*   **Level-up surveillance.** `GiveLevel` logs group and instance rosters and raises a realm GM alert when a player levels inside a dungeon with more instance members than group members — a mob-tag power-leveling heuristic.
*   **Foreign members live here.** Because `.cpp`-defined members are attributed to the defining TU, this unit's MAP legitimately contains members of other classes defined in Player.cpp: the four `Log::Player` overloads (`Player`, `Player#2`, `Player#3`, `Player#4`) with `PlayerLogHeaderToConsole`, `PlayerLogHeaderToFile`, `IsPlayerLoggingEnabledToDB`, `PlayerLogToDB`; `ItemPosCount::isContainedIn`; the free helpers `ToPlayer`/`ToPlayer#2`, `SkillGainChance`, `UpdateVisibilityOf_helper`, `AddBroadcastListener`/`RemoveBroadcastListener`, `GetPriestSpellForRace`/`GetCapitalReputationForRace`; and the functors `SetGameMasterOnHelper`/`SetGameMasterOffHelper`/`DoPlayerLearnSpell` with their `operator()` members. `Player#5` — counter-intuitively — is the actual `Player` constructor.
*   **1.12-typo fidelity.** Several member names carry upstream misspellings preserved for source fidelity: `IsRessurectRequested*`, `HasItemFitToSpellReqirements`, `UpdateTerainEnvironmentFlags`.
*   **Client-build conditionals everywhere.** `SUPPORTED_CLIENT_BUILD` gates dozens of behaviors to the emulated patch level: buyback slot count (1 pre-1.8), keyring size scaling (post-1.10.2), instance release timers (removed in 1.11), graveyard facing (1.8), fall/drowning absorb removal (1.7), play-time limits (post-1.7), self-res spell display (1.6), and the progressive honor/DK availability under the accurate-timeline config.

## Member Reference

- **Player#5**: Constructor — binds the WorldSession, initializes managers (reputation, honor, camera, gossip menu), timers, and default state.
- **~Player**: Destructor — cancels scheduled teleports, leaves persistent states, frees items, spell mods, and the duel record.
- **CleanupsBeforeDelete**: Pre-removal cleanup (trade, duel) chained to the Unit version; called on map removal and logout.
- **ValidateAppearance**: Static DBC check that a race/gender hair/face/skin combination is legal.
- **SelectRandomAppearance**: Static roll of a random valid appearance from CharSections DBC data (bot spawning).
- **Create**: Initializes a brand-new in-memory character from race/class PlayerInfo — position, map, display, base stats, start money/level.
- **AddStartingItems**: Grants the race/class starting kit; also used by bot auto-gearing.
- **StoreNewItemInBestSlots**: Equips a new item if possible, else stores it; the premade/bot gearing primitive.
- **StoreNewItemInInventorySlot**: Forces a new item into a free backpack slot.
- **SatisfyItemRequirements**: Force-raises honor rank/reputation so a premade item can be worn.
- **EnvironmentalDamage**: Deals typed environmental damage with school-correct absorb/resist and 10% durability loss on death.
- **GetAuctionAccessMode**: Reads which faction's auction house is usable (own/enemy/goblin) from the extra flags.
- **SetAuctionAccessMode**: Sets the auction-house access extra-flag bits.
- **HandleSobering**: Decays drunkenness by 256 per 10-second pulse.
- **GetDrunkenstateByValue**: Static classifier of a drunk value into sober/tipsy/drunk/smashed.
- **SetDrunkValue**: Applies inebriation — visual byte, invisibility-detection side effects, source-item tracking.
- **IsAcceptTickets**: True when a GM-security session has ticket acceptance on.
- **SetAcceptTicket**: Toggles GM ticket acceptance (extra-flag).
- **IsGameMaster**: True when GM mode is on.
- **IsGMChat**: GM chat-tag flag (moderator+).
- **IsTaxiCheater**: Reads the taxi-cheat flag.
- **SetTaxiCheater**: Toggles the all-taxi-nodes cheat extra-flag.
- **SetPvPDeath**: Marks the next death as PvP for corpse typing.
- **IsGMVisible**: False when GM invisibility is active.
- **GetWaterBreathingInterval**: Breath timer base scaled by the breathing multiplier.
- **SetWaterBreathingIntervalMultiplier**: Aura-driven breath-duration multiplier, refreshing mirror timers.
- **GetCheatOptions**: Reads the raw cheat-option bitmask.
- **HasCheatOption**: Tests one cheat-option bit.
- **EnableCheatOption**: Sets a cheat-option bit.
- **RemoveCheatOption**: Clears a cheat-option bit.
- **SetCheatOption**: Sets or clears a cheat-option bit by flag.
- **SetEnvironmentFlags**: Maintains the in-water/magma/slime/underwater flag byte with enter/exit side effects.
- **GetGMInvisibilityLevel**: Reads the GM invisibility tier (which security levels can still see the GM).
- **SetGMInvisibilityLevel**: Sets the GM invisibility tier.
- **GetGMTicketCounter**: Reads the last-seen ticket counter for GM ticket routing.
- **SetGMTicketCounter**: Writes the ticket routing counter.
- **CanTakeMoreSimilarItems**: Unique/max-count admission check for an existing Item (wraps `_CanTakeMoreSimilarItems` with the item's entry and count).
- **CanTakeMoreSimilarItems#2**: Unique/max-count admission check by entry + count (no item instance).
- **SendMirrorTimerStart**: Sends a timer-start packet (type, remaining, duration, scale, paused).
- **SendMirrorTimerStop**: Sends a timer-stop packet.
- **SendMirrorTimerPause**: Sends a timer pause/resume packet.
- **GetWeaponForAttack**: Convenience overload returning the weapon for an attack type without usability filters.
- **FreezeMirrorTimers**: Pauses/resumes all mirror timers (feign death).
- **GetItemUpdateQueue**: Exposes the deferred item-update vector maintained by Item's friend hooks.
- **IsMainHandPos**: Static test for the main-hand equipment position.
- **IsInventoryPos#2**: Static classifier, bag+slot form, for the inventory/backpack range.
- **IsEquipmentPos#2**: Static classifier, bag+slot form, for the equipment range.
- **IsBankPos#2**: Static classifier, bag+slot form, for the bank ranges.
- **SendMirrorTimers**: (Re)sends all active mirror timers, optionally forced.
- **IsValidPos#2**: Instance-aware validity check of a bag+slot pair (explicit-position strictness flag).
- **GetBankBagSlotCount**: Reads the purchased bank-bag slot count byte.
- **SetBankBagSlotCount**: Writes the purchased bank-bag slot count byte.
- **CanStoreNewItem**: Public placement solver for a new item id + count into a bag/slot, filling an `ItemPosCountVec` plan.
- **CanStoreItem**: Public placement solver for an existing Item into a bag/slot.
- **UpdateMirrorTimers**: Per-tick mirror-timer engine — activation, decay, and expiration damage pulses.
- **BankItem**: Alias of StoreItem for bank destinations.
- **GetBuyBackItemPrice**: Reads the buyback price field for a slot (single slot pre-1.8).
- **CheckMirrorTimerActivation**: Whether a timer type should start running (state preconditions).
- **CheckMirrorTimerDeactivation**: Whether a running timer type should stop.
- **GetMaxKeyringSize**: Level-scaled keyring capacity (post-1.10.2 builds).
- **AddWeaponProficiency**: ORs a flag into the weapon proficiency mask.
- **AddArmorProficiency**: ORs a flag into the armor proficiency mask.
- **GetWeaponProficiency**: Reads the weapon proficiency mask.
- **GetArmorProficiency**: Reads the armor proficiency mask.
- **IsTwoHandUsed**: True when a two-handed weapon occupies the main hand.
- **GetTradeData**: The active trade session object, if any.
- **GetMoney**: Coinage field reader.
- **ModifyMoney**: Delta with clamping into [0, GetMaxMoney()].
- **OnMirrorTimerExpirationPulse**: Applies the periodic drowning/fatigue/environmental damage when a bar has emptied.
- **SetMoney**: Writes coinage and re-evaluates money-objective quests via MoneyChanged.
- **GetLootGuid**: Returns the guid of the currently open loot window.
- **SetLootGuid**: Sets the open-loot-window guid.
- **GetMirrorTimerMaxDuration**: Max duration per timer type (breath from the breathing interval).
- **GetMirrorTimerBuff**: The aura holder that modifies a given mirror timer, if any.
- **IsCityProtector**: True when holding the city-protector title.
- **SetCityTitle**: Grants the city-protector title.
- **RemoveCityTitle**: Removes the city-protector title.
- **GetQuestSlotQuestId**: Reads the quest id stored in a packed log slot.
- **SetQuestSlot**: Writes a log slot: quest id, zeroed counters, optional timer.
- **SetQuestSlotCounter**: Writes one objective counter byte in a slot.
- **SetQuestSlotState**: ORs a state flag (complete/fail) into a slot.
- **RemoveQuestSlotState**: Clears a state flag from a slot.
- **SetQuestSlotTimer**: Writes a slot's timer field.
- **SwapQuestSlot**: Exchanges two log slots (client reorder).
- **CanAutoAttackTarget**: Validates a melee swing target (alive, attackable, facing/range handled by caller states).
- **GetQuestLevelForPlayer**: Quest level, or player level for scaling-level quests.
- **Update**: The per-tick heartbeat — see Member-by-Member Behavior for the full sequence.
- **GetQuestStatusMap**: The whole quest-status map (bots serialize their quest log from this).
- **GetQuestShareInfo**: Reads the pending quest-push bookkeeping (pusher guid + quest).
- **SetQuestShareInfo**: Records a pending quest push from a guid.
- **ClearQuestShareInfo**: Clears the pending quest-push record.
- **GetInGameTime**: Reads the in-game-time value used by raid quest credit windows.
- **SetInGameTime**: Writes the in-game-time value.
- **AddTimedQuest**: Inserts a quest id into the timed set.
- **RemoveTimedQuest**: Removes a quest id from the timed set.
- **HasCharacterFlag**: Tests a persistent character flag.
- **SetCharacterFlag**: Sets/clears a persistent character flag.
- **GetSaveTimer**: Reads the autosave countdown.
- **SetSaveTimer**: Writes the autosave countdown.
- **IsSavingDisabled**: True when saving is disabled — the bot/race-change kill-switch.
- **SetSaveDisabled**: Toggles the save kill-switch (bots, race change staging).
- **_SetMiniPet**: Raw mini-pet guid setter for Pet/Spell internals.
- **GetTemporaryUnsummonedPetNumber**: Reads the stashed pet number.
- **SetTemporaryUnsummonedPetNumber**: Writes the stashed pet number.
- **GetSpellMap#2**: Mutable access to the `PlayerSpellMap`.
- **GetSpellMap**: Const access to the `PlayerSpellMap`.
- **OnDisconnected**: Client-drop handling — resolve pending movement, relocate to last confirmed position.
- **RelocateToLastClientPosition**: Snaps the server position to the anticheat's last client-confirmed one.
- **GetFreeTalentPoints**: Reads the free talent points field (`PLAYER_CHARACTER_POINTS1`).
- **SetFreeTalentPoints**: Writes the free talent points field.
- **GetSafePosition**: Anticheat-confirmed (transport-aware) position for movement resolution.
- **SetWorldMask**: Phasing mask override that also refreshes visibility.
- **UpdateCinematic**: Flies the invisible cinematic ghost camera along waypoints, streaming cells around it.
- **InitStatBuffMods**: Zeroes the positive/negative stat-buff fields.
- **SetPersonalXpRate**: Sets the per-character XP multiplier (negative means unset).
- **GetPersonalXpRate**: Reads the per-character XP multiplier.
- **GetComboPoints**: Reads the combo point count.
- **GetComboTargetGuid**: Reads the combo target guid.
- **CinematicStart**: Starts a cinematic: spawns the invisible ghost camera flown along `CinematicWaypointEntry` paths to stream cells around the camera.
- **CinematicEnd**: Ends the cinematic and despawns the ghost camera.
- **CanParry**: Reads the parry capability flag.
- **CanBlock**: Reads the block capability flag.
- **CanDualWield**: Reads the dual-wield capability flag.
- **SetCanDualWield**: Sets the dual-wield capability flag.
- **SetDeathState**: Player death-state override — drunk/combo/pet/self-res-spell handling around the Unit transition.
- **ApplyStatBuffMod**: Applies a flat stat buff into the positive or negative display field by sign.
- **ApplyStatPercentBuffMod**: Applies a percent stat buff across both the positive and negative display fields.
- **GetPosStat**: Reads the positive stat-buff display field for a stat.
- **GetNegStat**: Reads the negative stat-buff display field for a stat.
- **GetResistanceBuffMods**: Reads the positive or negative resistance-buff display field for a school.
- **SetResistanceBuffMods**: Writes a resistance-buff display field.
- **ApplyResistanceBuffModsMod**: Applies a flat signed change to a resistance-buff display field.
- **ApplyResistanceBuffModsPercentMod**: Applies a percent change to a resistance-buff display field.
- **GetAmmoDPS**: Cached ammo DPS contribution.
- **SetBaseModValue**: Direct base-mod write (crit/block/dodge/parry groups).
- **GetTotalPercentageModValue**: Flat + percent sum for a base-mod group.
- **GetFreePrimaryProfessionPoints**: Reads the free primary-profession slots (`PLAYER_CHARACTER_POINTS2`).
- **SetFreePrimaryProfessions**: Writes the free primary-profession slots.
- **GetBaseDefenseSkillValue**: Pure defense skill value.
- **GetSkillValue**: Skill value including permanent and temporary bonuses.
- **GetSkillValueBase**: Skill value including only the permanent bonus.
- **GetSkillValuePure**: Raw skill value with no bonuses.
- **GetSkillMax**: Skill maximum including bonuses.
- **GetSkillMaxPure**: Raw skill maximum with no bonuses.
- **GetSkillBonusPermanent**: Reads the permanent bonus half of a skill's packed bonus field.
- **GetSkillBonusTemporary**: Reads the temporary bonus half.
- **AutoReSummonPet**: Resummons the temporarily stashed pet (taxi landing).
- **SetJustBoarded**: Marks the just-boarded state used by the 1.12 client transport-refresh workaround.
- **HasJustBoarded**: Reads the just-boarded flag.
- **SetCanDelayTeleport**: Marks the code region where a teleport request must be deferred instead of executed (set around handler bodies that cannot relocate mid-call).
- **IsHasDelayedTeleport**: True when a deferred teleport is armed — deliberately refuses to fire for a player who died after arming, so a ghost is never yanked back from the graveyard.
- **SetDelayedTeleportFlagIfCan**: Arms the delayed-teleport flag if the current region allows deferral; returns whether it armed.
- **ScheduleDelayedOperation**: Queues a DELAYED_* operation for after-teleport processing.
- **BuildEnumData**: Static character-select row renderer from the characters row + equipment_cache.
- **GetCachedZoneId**: Returns the zone id cached by the last zone update (`m_zoneUpdateId`).
- **GetCachedAreaId**: Returns the area id cached by the last area update (`m_areaUpdateId`).
- **GetGridRef**: Returns the grid reference link used by the grid container.
- **GetMapRef**: Returns the map reference link used by the map's player list.
- **GetTeleportDest**: The stored teleport destination.
- **IsBeingTeleported**: True while either teleport semaphore or a pending far teleport is active.
- **IsBeingTeleportedNear**: True while the near-teleport semaphore is held.
- **IsBeingTeleportedFar**: True while the far-teleport semaphore is held.
- **SetPendingFarTeleport**: Flags a far teleport as accepted but not yet executed (`m_pendingFarTeleport`).
- **SetFallInformation**: Records the fall-start Z used by `HandleFall`.
- **IsFalling**: True while a fall-start height is recorded.
- **IsControlledByOwnClient**: Whether the session's client currently steers this character.
- **SetMover**: Sets which unit the client steers (mind control/possess); null resets to self.
- **GetMover**: Returns the current mover unit (never null — defaults to self).
- **ToggleAFK**: Flips AFK (auto-cleared by BG rules).
- **IsSelfMover**: True when the player moves itself.
- **GetFarSightGuid**: Current far-sight object guid.
- **GetRecallPosition**: Reads the stored recall location.
- **ToggleDND**: Flips Do-Not-Disturb.
- **RelocateToHomebind**: Instant relocate (not a teleport) to the homebind point.
- **GetChatTag**: AFK/DND/GM chat tag byte.
- **IsInVisibleList_Unsafe**: Lock-free visible-set membership test.
- **GetCamera**: The player's camera object.
- **GetLongSight**: Active long-sight spell id.
- **SwitchInstance**: Hop to another instance id of the same map (thread-safety caveat documented in the header).
- **CanWalk**: Capability answer: always true for players.
- **CanSwim**: Capability answer: always true for players.
- **CanFly**: Capability answer: true while GM-flying.
- **SaveNoUndermapPosition**: Stores the last known safe position for the anti-undermap system.
- **UndermapRecall**: Near-teleports back to the stored safe position (within 100 yd) when the player falls through the world; returns whether it recovered.
- **GetHomeBindMap**: Returns the homebind map id.
- **GetHomeBindAreaId**: Returns the homebind area id.
- **SetSummonPoint**: Arms a 2-minute summon offer at a location.
- **IsLaunched**: True while in knockback flight.
- **SetLaunched**: Sets/clears the knockback-flight state.
- **TeleportTo**: The universal teleport entry point — near executes/defers, far schedules through the map manager.
- **IsUnderwater**: Reads the underwater flag.
- **IsInWater**: Reads the in-water flag.
- **IsInMagma**: Reads the in-magma flag.
- **IsInHighSea**: Reads the high-sea (fatigue) flag.
- **IsInHighLiquid**: Reads the deep-liquid flag.
- **UpdateInnerTime**: Sets the inn-enter timestamp for rest accrual.
- **GetRestBonus**: Reads the stored rest bonus.
- **GetRestType**: Reads the rest type.
- **GetTimeInnEnter**: Reads the inn-enter timestamp.
- **IsRested**: True after 10 s of continuous rest time.
- **GetRestTime**: Reads the accumulated rest time.
- **SetRestTime**: Writes the accumulated rest time.
- **GetTaxi**: Mutable access to the `PlayerTaxi` mask/state.
- **GetTaxi#2**: Const access to the `PlayerTaxi` state.
- **InitTaxiNodes**: Seeds race/level default taxi nodes.
- **GetCurrentCinematicEntry**: Active cinematic id.
- **GetLastSwingErrorMsg**: Reads the last swing-refusal result (dedup for messaging).
- **SetSwingErrorMsg**: Stores the last swing-refusal result.
- **SetCannotBeDetectedTimer**: Arms the scripted no-aggro window.
- **CanBeDetected**: False while the no-aggro window runs.
- **AI**: Returns the currently installed `PlayerAI*` (null for a plain client-driven player).
- **SetAI**: Installs a `PlayerAI*` into the AI slot; ownership conventions are the caller's responsibility.
- **GetSession**: The owning WorldSession.
- **IsBot**: True when the session is bot-backed.
- **ExecuteTeleportFar**: The scheduled half of a far teleport — entry re-check, world removal, transfer packets, semaphore raise.
- **GetTotalPlayedTime**: Returns total played seconds (`PLAYED_TIME_TOTAL`).
- **GetLevelPlayedTime**: Returns seconds played on the current level (`PLAYED_TIME_LEVEL`).
- **AddSkippedUpdateTime**: Accumulates map-scheduler time this player's update skipped, to be consumed on the next `Update` tick.
- **GetSkippedUpdateTime**: Returns the accumulated skipped-update time.
- **ResetSkippedUpdateTime**: Zeroes the skipped-update accumulator.
- **ScheduleStandUp**: Defers a stand-up to the next update (packet-order correctness).
- **IsStandUpScheduled**: Reads the deferred stand-up flag.
- **ClearScheduledStandUp**: Clears the deferred stand-up flag.
- **GetSelectedGobj**: Reads the GM-selected gameobject guid.
- **SetSelectedGobj**: Stores the GM-selected gameobject guid.
- **GetSelectionGuid**: Reads the current selection guid.
- **SetSelectionGuid**: Sets the selection guid and mirrors it into the Unit target guid.
- **SetResurrectRequestData**: Stores an incoming resurrect offer: resurrector guid, map, position, restored health/mana.
- **ClearResurrectRequestData**: Clears the stored resurrect offer.
- **IsRessurectRequestedBy**: True when the stored offer came from the given guid (name preserves the upstream typo).
- **IsRessurectRequested**: True while a resurrect offer is stored (upstream typo preserved).
- **GetResurrector**: Returns the offering resurrector's guid.
- **RemoveDelayedOperation**: Clears a queued DELAYED_* bit.
- **HasScheduledEvent**: Whether the event queue holds anything.
- **SetEscortingGuid**: Stores the escort-debug guid.
- **GetEscortingGuid**: Reads the escort-debug guid.
- **GetDrunkValue**: Raw drunkenness value.
- **GetDeathTimer**: Remaining corpse-release countdown.
- **SendNewWorld**: Sends SMSG_NEW_WORLD to complete a far teleport.
- **IsEnabledWhisperRestriction**: Reads the whisper-restriction extra-flag.
- **SetWhisperRestriction**: Toggles the friends-only whisper restriction extra-flag.
- **IsAcceptWhispers**: Reads the GM whisper-acceptance flag.
- **SetAcceptWhispers**: Toggles GM whisper acceptance.
- **GetExtraFlags**: The whole persistent extra-flags word.
- **HandleReturnOnTeleportFail**: Restores the pre-teleport location when the destination refused entry.
- **IsAFK**: Reads the AFK player flag.
- **IsDND**: Reads the DND player flag.
- **GetName**: The character name (final override).
- **SetName**: Sets the character name (rename flow).
- **LearnLanguage**: Adds a language to the known-languages bitmask.
- **RemoveLanguage**: Removes a language from the bitmask.
- **KnowsLanguage**: Tests the known-languages bitmask (chat filtering).
- **RestorePendingTeleport**: Re-fires a teleport that was pending when interrupted.
- **TeleportToBGEntryPoint**: Teleport back to the stored battleground entry point.
- **GetTeam**: The cached faction team (final override).
- **GetTeamId**: Team as a `TeamId` index.
- **ProcessDelayedOperations**: Executes queued save/resurrect/deserter/honorless operations after a teleport lands.
- **GetReputationMgr**: Mutable access to the `ReputationMgr`.
- **GetReputationMgr#2**: Const access to the `ReputationMgr`.
- **SetTemporaryAtWarWithFaction**: Marks a faction as scripted-at-war until cleared.
- **IsPvPDesired**: Reads the manual PvP flag.
- **IsFFAPvP**: Reads the free-for-all flag.
- **AddToWorld**: Grid insertion hook — reunites the player with a corpse on the same map and registers the packet broadcaster.
- **RemoveFromWorld**: Grid removal hook — unregisters the broadcaster and detaches map-bound state.
- **IsInDuelWith**: True when dueling the given player.
- **GetHonorMgr**: Mutable access to the `HonorMgr`.
- **GetHonorMgr#2**: Const access to the `HonorMgr`.
- **InBattleGround**: True while a BG instance id is set in the BG data.
- **GetBattleGroundId**: The current BG instance id.
- **GetBattleGroundTypeId**: The current BG type id.
- **RewardRage**: Converts damage dealt/taken into rage.
- **InBattleGroundQueue**: True when any queue slot is occupied.
- **GetQueuedBattleground**: The first occupied queue slot's type id.
- **GetBattleGroundQueueTypeId**: Reads a queue slot by index.
- **GetBattleGroundQueueIndex**: Finds the slot index of a queue type.
- **HandleFoodEmotes**: Periodic eat/drink emote while sitting with food/drink auras.
- **IsInvitedForBattleGroundQueueType**: True when the queue slot carries an invite.
- **InBattleGroundQueueForBattleGroundQueueType**: Membership test for a specific queue type.
- **SetBattleGroundId**: Sets the BG instance and type ids (marks BG data for save).
- **AddBattleGroundQueueId**: Occupies a free queue slot with the type; returns the slot index.
- **RemoveBattleGroundQueueId**: Frees the queue slot holding the type.
- **RegenerateAll**: The 2-second regen tick fan-out (health and all powers).
- **SetInviteForBattleGroundQueueType**: Stamps an instance invite onto the queue slot.
- **IsInvitedForBattleGroundInstance**: True when invited to a specific BG instance id.
- **Regenerate**: Per-power regeneration (interrupted-mana model, rage decay, energy tick).
- **GetBattleGroundEntryPoint**: The stored join position.
- **SetBGTeam**: Cross-faction team assignment inside the BG (marks BG data for save).
- **GetBGTeam**: The effective BG team — the assigned team, else the real team.
- **GetCheatData**: The session's movement-anticheat instance.
- **GetPacketBroadcaster**: The per-player packet fan-out object.
- **RegenerateHealth**: Spirit-based health regen (halved while polymorphed, boosted by carry-over).
- **GetBoundInstances**: The instance-bind map.
- **SetAutoInstanceSwitch**: Toggles elastic continent-instance switching for this player.
- **GetSmartInstanceBindingMode**: Reads the smart-rebind toggle.
- **SetSmartInstanceBindingMode**: Sets the smart-rebind toggle.
- **GetGroupInvite**: Returns the pending group invite.
- **SetGroupInvite**: Stores the pending group invite.
- **GetGroup**: Returns the current group (mutable form).
- **GetGroup#2**: Returns the current group (const form).
- **GetGroupRef**: The group reference link used by the group's member list.
- **GetSubGroup**: Returns the raid subgroup index.
- **GetGroupUpdateFlag**: Reads the pending group-update mask.
- **SetGroupUpdateFlag**: ORs a flag into the group-update mask.
- **GetAuraUpdateMask**: Reads the pending aura-slot update mask for party frames.
- **SetAuraUpdateSlot**: Marks one aura slot dirty in the mask.
- **SetAuraUpdateMask**: Writes the whole aura-update mask.
- **IsInSameRaidWith**: Same-raid test.
- **RemoveFromGroup**: Instance form — leaves the current group.
- **CanUseBank**: Banker-proximity check that remembers the current banker guid.
- **SetLFGAreaId**: Sets the looking-for-group area id.
- **GetLFGAreaId**: Reads the LFG area id.
- **IsInLFG**: True when an LFG area is set.
- **GetOriginalGroup**: Returns the preserved world group during a BG raid.
- **GetOriginalGroupRef**: The reference link for the preserved world group.
- **GetOriginalSubGroup**: Subgroup index in the preserved world group.
- **CanInteractWithQuestGiver**: Type-dispatched interaction check for quest sources.
- **SetInGuild**: Writes the guild id field (`PLAYER_GUILDID`).
- **SetRank**: Writes the guild rank field.
- **SetGuildIdInvited**: Stores the pending guild invite id.
- **GetGuildId**: Reads the guild id field.
- **GetRank**: Reads the guild rank field.
- **GetGuildIdInvited**: Reads the pending guild invite id.
- **FindNearestInteractableNpcWithFlag**: Nearest NPC with the given npcflag within interaction range.
- **ToPlayer**: Free inline safe downcast from `Object*` to `Player*` (null when not a player).
- **ToPlayer#2**: Const form of the safe downcast.
- **GetNPCIfCanInteractWith**: Resolves an NPC by guid if interactable under the npcflag mask (alive, in range, no hostility).
- **CanInteractWithNPC**: The npcflag-mask interaction test against a resolved creature.
- **GetGameObjectIfCanInteractWith**: Resolves a GO by guid if interactable for the type within activation range.
- **CanInteractWithGameObject**: The GO interaction test by type and activation range.
- **CanSeeHealthOf**: Health-information rights over a target (beast lore / party rules).
- **CanSeeSpecialInfoOf**: Special-info (beast lore) rights over a target.
- **SetGameMasterOnHelper**: File-local camera functor applying GM-on visibility (GM faction, no target) to viewed units.
- **operator()#3**: `SetGameMasterOnHelper`'s call operator, applied per viewed unit.
- **SetGameMasterOffHelper**: File-local camera functor restoring faction-based visibility when GM mode turns off.
- **operator()#2**: `SetGameMasterOffHelper`'s call operator, applied per viewed unit.
- **SetGMChat**: Toggles the GM chat tag with optional notification.
- **SetGameMaster**: Full GM mode toggle — GM faction, PvP immunity, combat drop, visibility rebuild.
- **SetGMVisible**: GM invisibility toggle (aura-style stealth at the GM invisibility level).
- **SetCheatFly**: GM fly cheat — sends the client movement packets; optional notification.
- **SetCheatFixedZ**: Fixed-Z cheat toggle (no falling).
- **SetCheatBeastmaster**: Beastmaster cheat — tame anything (adjusts unit flags).
- **SetCheatGod**: God mode — untargetable/invulnerable unit-flag adjustment.
- **SetCheatNoCooldown**: No-cooldown cheat toggle.
- **SetCheatInstantCast**: Instant-cast cheat toggle.
- **SetCheatNoPowerCost**: No-power-cost cheat toggle.
- **SetCheatDebuffImmunity**: Debuff-immunity cheat toggle.
- **SetCheatAlwaysCrit**: Always-crit cheat toggle.
- **SetCheatNoCastCheck**: Skip-cast-checks cheat toggle.
- **SetCheatAlwaysProc**: Always-proc cheat toggle.
- **SetCheatTriggerPass**: Pass-area-triggers cheat toggle.
- **SetCheatIgnoreTriggers**: Ignore-area-triggers cheat toggle.
- **SetCheatDebugTargetInfo**: Debug-target-info cheat toggle.
- **IsAllowedWhisperFrom**: Whisper-restriction check combining the flag with the friend list.
- **IsGroupVisibleFor**: Group visibility rule used by stealth/invisibility group exceptions.
- **IsInSameGroupWith**: True when both players share a party (same subgroup for raids).
- **UninviteFromGroup**: Cancels a pending invite.
- **RemoveFromGroup#2**: Static removal of a guid from a group (used by deletion and kicks).
- **SendLogXPGain**: SMSG_LOG_XPGAIN with kill/rested split.
- **GiveXP**: Applies rate/play-time/trial gates and rested bonus, looping GiveLevel across thresholds.
- **GiveLevel**: The level-up transaction — logging, GM alert heuristic, BG bracket requeue, base stats, client packet, skills/talents/stats refresh, pet sync.
- **UpdateFreeTalentPoints**: Reconciles spent vs. available talent points (wiping when oversubscribed).
- **InitTalentForLevel**: Recomputes talent points for the level and prunes overspend.
- **InitStatsForLevel**: Resets all unit fields to clean level baselines (login, .levelup).
- **SendInitialSpells**: Transmits the spellbook and active cooldowns at login.
- **AddSpell**: Core spellbook mutation with rank supersession, skill-line updates, and save-state tracking.
- **IsNeedCastPassiveLikeSpellAtLearn**: Whether a learned spell should be immediately cast like a passive.
- **LearnSpell**: AddSpell plus client notify and dependent-rank re-enable.
- **RemoveSpell**: Unlearns with optional low-rank relearn and dependent/skill cascade.
- **_LoadSpellCooldowns**: Restores cooldowns from `character_spell_cooldown` at login, dropping already-expired rows.
- **_SaveSpellCooldowns**: Persists active cooldowns to `character_spell_cooldown` inside the save transaction.
- **UpdateResetTalentsMultiplier**: Decays the talent-reset cost multiplier over elapsed time.
- **GetResetTalentsCost**: Current gold cost from the decayed multiplier.
- **ResetTalents**: Full talent wipe with cost, pet talent wipe, and action-button cleanup.
- **BuildCreateUpdateBlockForPlayer**: Create-block for a viewer plus held-item blocks.
- **DestroyForPlayer**: Destroy-block counterpart including items.
- **HasSpell**: True when the spell is in the book (override of the Unit query).
- **HasActiveSpell**: True when the spell is in the book and active (i.e.
- **GetTrainerSpellState**: Green/red/gray classification of a trainer entry for this character.
- **DeleteFromDB**: Static delete/soft-delete — COD mail return, guild/group/petition teardown, satellite-table sweep or unlink.
- **DeleteOldCharacters**: Static reaper using the configured keep-days: sweeps soft-deleted rows older than the window through the hard-delete path.
- **DeleteOldCharacters#2**: Explicit keep-days form of the reaper.
- **SetFly**: GM flight toggle via movement packets.
- **ApplyGhostForm**: Applies ghost visuals, ghost speed, and water-walk on spirit release.
- **RemoveGhostForm**: Strips the ghost visuals/speed/water-walk at resurrect.
- **BuildPlayerRepop**: Creates and places the corpse, applies ghost state, arms reclaim delay.
- **ResurrectPlayer**: Returns to life with percentage restore, zone refresh, and level-scaled resurrection sickness.
- **KillPlayer**: CORPSE transition — 6-minute release timer, reclaim-delay update, anticheat death notice.
- **CreateCorpse**: Builds the Corpse object (appearance, equipment display, guild, PvP/BG flags) and persists it.
- **SpawnCorpseBones**: Converts the corpse to bones via the object accessor.
- **GetCorpse**: Looks up this player's corpse.
- **DurabilityLossAll**: Percentage durability damage across equipment (optionally inventory too) — the death-penalty path.
- **DurabilityLoss**: Percentage durability damage on one item.
- **DurabilityPointsLossAll**: Flat-point durability damage across equipment (optionally inventory).
- **DurabilityPointsLoss**: Flat-point durability damage on one item.
- **DurabilityPointLossForEquipSlot**: One point of durability damage to a specific equipment slot.
- **DurabilityRepairAll**: Repairs everything, priced per point from the `DurabilityCosts`/`DurabilityQuality` DBCs with the vendor discount; returns the cost.
- **DurabilityRepair**: Repairs one position with the same DBC pricing; returns the cost.
- **ScheduleRepopAtGraveyard**: Defers graveyard repop until pending movement changes resolve.
- **RepopAtGraveyard**: Ghost (or hazard-zone) teleport to the closest graveyard, BG-aware, spirit-healer facing on 1.8+.
- **JoinedChannel**: Adds a channel to the joined list.
- **LeftChannel**: Removes a channel from the joined list.
- **CleanupChannels**: Leaves all channels at logout/transfer.
- **UpdateLocalChannels**: Re-homes built-in zone channels on zone change.
- **LeaveLFGChannel**: Drops the LFG channel when leaving LFG state.
- **HandleBaseModValue**: Applies a flat/percent base-mod delta and re-derives the affected rating (Player.StatSystem update calls).
- **GetBaseModValue**: Reads one base-mod component (flat or percent) of a group.
- **GetTotalBaseModValue**: Returns a group's flat value scaled by its percent component.
- **GetShieldBlockValue**: Strength-derived block value with base mods.
- **GetMeleeCritFromAgility**: Class-specific agility→melee-crit ratio.
- **GetDodgeFromAgility**: Class-specific agility→dodge ratio.
- **SetRegularAttackTime**: Sets base attack times from equipped weapon protos.
- **UpdateSkill**: Plain stepwise skill-up roll.
- **SkillGainChance**: File-local helper converting gray/green/yellow thresholds into a gain chance.
- **UpdateCraftSkill**: Crafting skill-up using per-spell gain data.
- **UpdateGatherSkill**: Gathering skill-up with red-level scaling and multiplier.
- **UpdateFishingSkill**: Fishing's own 1-in-N gain model.
- **UpdateSkillPro**: The underlying chance-based skill increment.
- **UpdateCombatSkills**: Weapon/defense skill-ups from combat, gray-level shaped.
- **UpdateSkillsForLevel**: Raises skill caps on level-up (auto-raising capped-type values).
- **UpdateSkillsToMaxSkillsForLevel**: Maxes all skills (GM leveling).
- **SetSkill**: Creates/updates/deletes a skill line with spell cascade.
- **HasSkill**: Skill presence test.
- **GetSkill**: Unpacks value/max with selectable bonus inclusion.
- **ModifySkillBonus**: Adds/removes a permanent or temporary skill bonus in the packed bonus halves.
- **GetSkillBonus**: Reads the permanent or temporary bonus half for a skill.
- **UpdateSkillTrainedSpells**: Learns/unlearns spells granted by a skill value/step change.
- **UpdateSpellTrainedSkills**: Learns/unlearns skills granted by a spell (weapon/riding skills).
- **IsActionButtonDataValid**: Static validation of an action-button payload (spell known, macro exists…).
- **SetPosition**: In-map relocate with zone/area re-check trigger.
- **SaveRecallPosition**: Stores the current location as the recall point.
- **SendMessageToSet**: Nearby broadcast of a packet (optionally to self) via the broadcaster.
- **SendMessageToSetInRange**: Radius-limited nearby broadcast.
- **SendMessageToSetInRange#2**: Own-team-only radius broadcast.
- **SendDirectMessage**: Sends a packet to this client only.
- **SendCinematicStart**: The cinematic-start opcode plus camera setup.
- **IsOutdoorOnTransport**: Outdoor answer while aboard a transport (model-dependent).
- **CheckAreaExploreAndOutdoor**: Outdoor/aura reconciliation, tavern-exit detection, explored-bit setting with exploration XP.
- **TeamForRace**: Static race→team DBC lookup.
- **GetFactionForRace**: Static race→faction-template DBC lookup.
- **SetFactionForRace**: Caches the team and sets the faction template from race.
- **GetReputationRank**: Rank shortcut through the reputation manager.
- **CalculateReputationGain**: Rate/config/aura-scaled reputation delta computation.
- **RewardReputation#2**: Kill-source reputation grant — team-spillover aware, pet/player victim filtered.
- **RewardReputation**: Quest-source reputation grant, spillover-mask aware.
- **GetGuildIdFromDB**: Static guild-id read from `guild_member` by guid.
- **GetRankFromDB**: Static guild-rank read by guid.
- **GetZoneIdFromDB**: Static single-row zone read for handlers without a loaded Player (recomputes and backfills when stale).
- **GetLevelFromDB**: Static single-row level read by guid.
- **DismountCheck**: Force-dismount where mounts are disallowed.
- **SetTransport**: Boarding/unboarding bookkeeping over the Unit version.
- **UpdateArea**: Area-level pass — arena FFA, tavern rest type.
- **UpdateZone**: Zone transition — zone scripts, world states, weather, PvP enforcement, rest, zone-limited items, channels, auras.
- **CheckDuelDistance**: Arms/executes the 10-second out-of-bounds duel forfeit beyond 75 yards.
- **IsOutdoorPvPActive**: Whether outdoor-PvP objectives should count this player.
- **DuelComplete**: Duel resolution — flags, combat stop, duel-period aura cleanup, beg emote, arbiter removal.
- **_ApplyItemMods**: Slot-level dispatcher for item stats, enchants, and equip spells.
- **_ApplyItemBonuses**: The proto→stat/armor/resistance/damage application (slot-aware ranged AP classes).
- **_ApplyWeaponDependentAuraMods**: Re-targets weapon-conditional auras when a weapon in the attack slot changes.
- **_ApplyWeaponDependentAuraCritMod**: Applies/removes one weapon-conditional crit aura against the equipped weapon.
- **_ApplyWeaponDependentAuraDamageMod**: Applies/removes one weapon-conditional damage aura against the equipped weapon.
- **UpdateDamageDonePercent**: Recomputes a school's damage-done multiplier field.
- **ApplyItemEquipSpell**: Applies/removes an item's on-equip spells, form-condition aware.
- **ApplyEquipSpell**: Applies/removes a single equip spell entry for an item, honoring form conditions at form change.
- **UpdateEquipSpellsAtFormChange**: Re-evaluates equip/set spells whose shapeshift conditions changed.
- **CastItemCombatSpell**: Chance-on-hit weapon procs (PPM from weapon speed) plus poison/enchant procs.
- **CastItemUseSpell**: Casts an item's use spells with category cooldown handling.
- **GetItemSetEffect**: Returns the active `ItemSetEffect` tracker for a set id, if any pieces are worn.
- **AddItemSetEffect**: Creates/returns the set-bonus tracker when the first piece of a set is equipped.
- **RemoveItemSetEffect**: Drops the set-bonus tracker when the last piece is removed.
- **_RemoveAllItemMods**: Bulk removal of all equipped item effects (form/stat rebuild bracket, remove side).
- **_ApplyAllItemMods**: Bulk reapplication of all equipped item effects (rebuild bracket, apply side).
- **_ApplyAmmoBonuses**: Recomputes ammo DPS contribution.
- **CheckAmmoCompatibility**: Ammo subclass must match the equipped ranged weapon.
- **RemovedInsignia**: BG corpse-insignia looting (converts corpse to bones, builds insignia loot).
- **SendLootRelease**: Sends the loot-window close packet.
- **SendLootError**: Sends a loot refusal with reason.
- **SendLoot**: Resolves any loot source (GO/item/corpse insignia/skinning/pickpocket/creature), fills or reuses Loot, applies group loot rules, opens the window.
- **SendNotifyLootMoneyRemoved**: Notifies the open loot window that the money was taken.
- **SendLootMoneyNotify**: Sends the looted-money amount notification.
- **SendNotifyLootItemRemoved**: Notifies the open loot window that a slot was taken.
- **SendUpdateWorldState**: Single world-state value push.
- **SendInitWorldStates**: Zone-entry world-state block (BG scoreboards, war effort, invasion counters).
- **GetXPRestBonus**: Consumes rest for a kill's bonus XP (2× up to the stored amount).
- **ComputeRest**: Converts elapsed (offline or inn) time into rest bonus at place-dependent rates.
- **SetBindPoint**: Sends the innkeeper bind confirmation packet.
- **SendTalentWipeConfirm**: Sends the trainer's talent-wipe confirmation dialog with the computed cost.
- **SendPetSkillWipeConfirm**: Sends the pet untrain confirmation with cost.
- **FindEquipSlot**: Resolves a proto's inventory type to concrete equipment slots (dual-wield aware).
- **CanUnequipItems**: Whether N of an item id could be unequipped (trade validation).
- **GetItemCount**: Counts an item across inventory (optionally bank), skipping a given item.
- **GetItemByGuid**: Item lookup by guid across inventory, bags, and bank.
- **GetItemByPos#2**: Item lookup by packed position (bag<<8|slot wrapper).
- **GetItemByPos**: Item lookup by bag + slot (the worker; dereferences equipped bags).
- **GetWeaponForAttack#2**: Weapon for an attack type with broken/usable filters.
- **GetWeaponForParry**: The weapon that can parry (main or off hand).
- **CanBeDisarmed**: Disarm legality (weapon present).
- **GetAttackBySlot**: Static slot→attack-type mapping.
- **GetHighestKnownArmorProficiency**: Best wearable armor class from the proficiency mask.
- **IsInventoryPos**: Static classifier: is a packed position (bag<<8|slot) inside the inventory/backpack range.
- **IsEquipmentPos**: Static classifier: is a packed position an equipment slot.
- **IsBankPos**: Static classifier: is a packed position inside the bank ranges.
- **IsBagPos**: Static classifier: is a packed position an equippable bag slot (bag bar or bank bag bar).
- **IsValidPos**: Validity check of a bag + slot against the character's actual bags and bag sizes (explicit-position aware).
- **HasItemCount**: At-least-N ownership test (optionally bank).
- **HasItemWithIdEquipped**: Worn-item count test with slot exclusion.
- **_CanTakeMoreSimilarItems**: Unique/max-count core with no-space reporting.
- **_CanStoreItem_InSpecificSlot**: Placement-solver pass for one explicit bag+slot (merge-first, swap aware).
- **_CanStoreItem_InBag**: Placement-solver pass across one bag, with merge and specialized-bag filters and skip positions.
- **_CanStoreItem_InInventorySlots**: Placement-solver pass across a backpack slot range, with merge and skip positions.
- **_CanStoreItem**: The full placement solver emitting an ItemPosCountVec plan or a precise error.
- **CanStoreItems**: Simulated placement of an item set (trade acceptance).
- **CanEquipNewItem**: Equip legality for a not-yet-created item id (wraps a temporary item through `CanEquipItem`).
- **CanEquipItem**: Full equip legality for an existing item: slot resolution, proficiency, level, skill, combat/shapeshift restrictions, dual-wield awareness; returns an `InventoryResult` and the destination.
- **CanEquipItem#2**: Proto-based worker behind the Item form: the full slot/proficiency/level/skill/combat/shapeshift rule set, usable before an item instance exists.
- **CanUnequipItem**: Removal legality for one position (bags empty, combat rules).
- **CanBankItem**: Bank placement including bank-bag slot purchases.
- **CanUseItem**: Consumption/use gating by level, skill, reputation, and faction for an existing item.
- **CanUseItem#2**: Use gating for a bare `ItemPrototype` (no item instance).
- **CanUseAmmo**: The projectile variant.
- **SetAmmo**: Sets the ammo field after compatibility validation and reapplies ammo bonuses.
- **RemoveAmmo**: Clears the ammo field and its DPS contribution.
- **StoreNewItem**: Creates and places a new item per plan (quest counters, loot logging, random property).
- **StoreItem**: Places an existing item per plan (stack merging).
- **_StoreItem**: Single-slot physical placement/merge.
- **EquipNewItem**: Creates and equips a new item id at a position.
- **EquipItem**: Equips an item: visible bytes (`SetVisibleItemSlot`, `VisualizeItem`), stat application, `ApplyEquipCooldown`, and combat/cast interactions.
- **QuickEquipItem**: Loading-time equip fast path.
- **SetVisibleItemSlot**: Writes an equipped item's visible fields (entry, enchants, creator) for a slot.
- **VisualizeItem**: Sets an item into an equipment slot's visible fields and binds it to the player.
- **RemoveItem**: Detaches an item from a slot without destroying it.
- **MoveItemFromInventory**: Transfer half used by trade/mail/auction: removes the item and marks it removed from `character_inventory`.
- **MoveItemToInventory**: Return half of the transfer: places the item back, aware of whether its rows still exist in the character DB.
- **DestroyItem**: Full single-slot deletion with enchant/duration/quest/zone bookkeeping.
- **DestroyItemCount#2**: Count-based removal of an item id swept across bags, with unequip/bank options.
- **DestroyItemCount**: Count-based removal from a specific item instance (stack decrement or delete).
- **DestroyEquippedItem**: Targets worn items by id.
- **DestroyZoneLimitedItem**: Removes items limited to another zone.
- **DestroyConjuredItems**: Removes conjured items (logout expiry).
- **SplitItem**: Divides a stack into a target position.
- **SwapItem**: Exchanges two positions with full re-validation (merge, equip swap, bag-in-bag cases).
- **AddItemToBuyBackSlot**: Pushes a sold item into the 12-slot buyback carousel — the oldest entry is evicted and truly deleted (single slot pre-1.8).
- **GetItemFromBuyBackSlot**: Reads a buyback slot.
- **RemoveItemFromBuyBackSlot**: Clears a buyback slot, optionally deleting the item.
- **SendEquipError**: Sends the inventory refusal packet for an `InventoryResult`.
- **SendOpenContainer**: Opens a bag remotely on the client (disenchant/lockbox flows).
- **SendBuyError**: Sends the vendor buy refusal packet.
- **SendSellError**: Sends the vendor sell refusal packet.
- **GetTrader**: The trade counterparty.
- **TradeCancel**: Tears the trade session down.
- **UpdateItemDuration**: Decays limited-lifetime items (real-time-only filter).
- **UpdateEnchantTime**: Decays temporary enchant durations.
- **AddEnchantmentDurations**: Registers all of an item's temporary enchants into the countdown list.
- **RemoveEnchantmentDurations**: Drops an item's entries from the countdown list (item leaving the character).
- **RemoveAllEnchantments**: Strips a given enchantment slot from every item.
- **AddEnchantmentDuration**: Registers one enchant slot's remaining duration into the countdown list.
- **ApplyEnchantment#2**: All-slots form: loops every enchantment slot of the item through the worker.
- **ApplyEnchantment**: Applies or removes one enchantment slot's effects on an item — procs, damage, stats, buffs, temporary-duration hookup — with condition checking (`ignore_condition` for form changes).
- **BuildEnchantmentLog**: Builds the enchant-applied packet (caster, item, spell, affiliation display).
- **SendEnchantmentLog**: Sends the enchant-applied announcement.
- **SendEnchantmentDurations**: Login push of remaining temporary-enchant times.
- **SendItemDurations**: Login push of remaining item lifetimes.
- **SendNewItem**: Item-received broadcast (creation/receive source, optional group broadcast).
- **PrepareGossipMenu**: Builds the condition-filtered gossip menu from DB rows and script hooks.
- **SendPreparedGossip**: Chooses gossip vs. quest-giver frame and transmits.
- **OnGossipSelect**: Dispatches a chosen gossip row to the right subsystem or script.
- **GetGossipTextId**: Static greeting-text resolution from the source object's default gossip (npc_gossip lookup).
- **GetGossipTextId#2**: Greeting-text resolution for a menu id, evaluating menu conditions.
- **PrepareQuestMenu**: Quest list from giver relations filtered by state and eligibility.
- **SendPreparedQuest**: Greeting/details/reward frame selection.
- **IsActiveQuest**: Taken-or-takeable test.
- **IsCurrentQuest**: In-log test with completed/incomplete filter.
- **GetNextQuest**: Follow-up quest at the same ender.
- **CanSeeStartQuest**: Pre-level visibility of a quest.
- **CanTakeQuest**: ANDs the entire SatisfyQuest* prerequisite family.
- **CanAddQuest**: Adds log space and source-item storability.
- **CanCompleteQuest**: Turn-in validator: every objective of the given quest satisfied.
- **CanCompleteRepeatableQuest**: Repeatable-quest variant of the completion validator (item requirements re-checked each turn-in).
- **CanRewardQuest**: Reward-eligibility check: completed, objectives verified, and reward-inventory space for fixed items.
- **CanRewardQuest#2**: Reward form that additionally validates the chosen reward item's storability before `RewardQuest`.
- **CountFreeInventorySlots**: Free backpack+bag slot count.
- **SendPetTameFailure**: Tame-failure reason packet.
- **AddQuest**: Log insertion — counters, timer, PvP flag, scripts, source item, counter reconciliation, area spells.
- **FullQuestComplete**: GM-style completion filling every objective.
- **CompleteQuest**: Marks complete (auto-reward for special repeatables).
- **IncompleteQuest**: Reverts to incomplete.
- **RemoveQuest**: Abandons a quest: clears the slot, returns or destroys the start item, drops the timed entry.
- **RemoveQuestAtSlot**: Slot-indexed abandon used by the client's log-slot opcode.
- **RewardQuest**: The turn-in transaction — items, reputation, XP or max-level money, mail, spells, scripts, status.
- **FailQuest**: Failed state with timer UI reset.
- **SatisfyQuestSkill**: Prerequisite check: required skill and value (optional client error).
- **SatisfyQuestCondition**: Prerequisite check: the quest's `conditions` table entry.
- **SatisfyQuestLevel**: Prerequisite check: minimum level.
- **SatisfyQuestLog**: Prerequisite check: a free slot in the 20-slot quest log.
- **SatisfyQuestPreviousQuest**: Prerequisite check: required previous quests completed.
- **SatisfyQuestBreadcrumbQuest**: Prerequisite check: the breadcrumb's target quest is still takeable.
- **SatisfyQuestDependentBreadcrumbQuests**: Prerequisite check: no dependent breadcrumb quest is currently active.
- **SatisfyQuestClass**: Prerequisite check: class mask.
- **SatisfyQuestRace**: Prerequisite check: race mask.
- **SatisfyQuestReputation**: Prerequisite check: minimum/maximum reputation window.
- **SatisfyQuestStatus**: Prerequisite check: the quest is not already in the log.
- **SatisfyQuestTimed**: Prerequisite check: no other timed quest is running.
- **SatisfyQuestExclusiveGroup**: Prerequisite check: no conflicting quest from the exclusive group taken or done.
- **SatisfyQuestNextChain**: Prerequisite check: the next quest in the chain is not already taken/done.
- **SatisfyQuestPrevChain**: Prerequisite check: no earlier quest of the chain is still in progress.
- **CanGiveQuestSourceItemIfNeed**: Source-item storability check, recognizing an already-held max stack as satisfied.
- **GiveQuestSourceItemIfNeed**: Grants the quest's source item when storable (per the check above).
- **TakeOrReplaceQuestStartItems**: Returns or destroys quest start items on abandon.
- **GetQuestRewardStatus**: Rewarded flag for a quest.
- **GetQuestStatusData**: Returns the full `QuestStatusData` record for a quest id, if tracked.
- **GetQuestStatus**: Returns the status of a quest id (`NONE` when unknown).
- **CanShareQuest**: Push-to-party legality.
- **SetQuestStatus**: Direct status write with world-object refresh.
- **AdjustQuestReqItemCount**: Recounts deliverable objectives from actual inventory.
- **FindQuestSlot**: Log slot for a quest id (0 finds a free slot).
- **AreaExploredOrEventHappens**: Explore/event objective completion.
- **GroupEventHappens**: Explore/event completion propagated to eligible group members near the event object.
- **GroupEventFailHappens**: Group-wide event failure propagation.
- **ItemAddedQuestCheck**: Advances deliverable counters when items enter the inventory.
- **ItemRemovedQuestCheck**: Rolls back deliverable counters when items leave the inventory.
- **KilledMonster**: Kill-objective credit from a `CreatureInfo` (resolves the credit entry) and guid.
- **KilledMonsterCredit**: Kill credit by entry — killcredit-entry aware — advancing matching quest counters.
- **CastedCreatureOrGO**: Cast-objective credit.
- **TalkedToCreature**: Speak-to objective credit.
- **LogModifyMoney**: Threshold/GM-gated audit logging around ModifyMoney with counterparty guids.
- **GetMaxMoney**: Money cap (trial accounts capped far lower).
- **MoneyChanged**: Re-evaluates negative-money quests on coinage change.
- **ReputationChanged**: Re-evaluates reputation-objective quests.
- **HasQuestForItem**: Does any log quest still need this item.
- **SendQuestCompleteEvent**: Sends the quest-completed event packet.
- **SendQuestReward**: Sends the reward frame packet with XP for the turn-in.
- **SendQuestFailedAtTaker**: Sends the failure reason at the quest taker (default: requirements not met).
- **SendQuestFailed**: Sends the generic quest-failed packet.
- **SendQuestTimerFailed**: Sends the timer-expired failure packet.
- **SendCanTakeQuestResponse**: Sends the can't-take reason code.
- **SendQuestConfirmAccept**: Sends the escort/event accept-confirmation to a nearby receiver.
- **SendPushToPartyResponse**: Sends the quest-push result (busy, invalid, already have…) back to the pusher.
- **SendQuestUpdateAddItem**: Sends an item-objective counter update.
- **SendQuestUpdateAddCreatureOrGo**: Sends a kill/use-objective counter update.
- **Initialize**: Pre-creates the object shell for a guid before loading.
- **_LoadBGData**: Restores battleground identity and entry point from character_battleground_data.
- **LoadPositionFromDB**: Static position/in-flight reader.
- **_LoadIntoDataField**: Parses a space-separated string into consecutive update fields (explored zones).
- **LoadFromDB**: The full login load pipeline — see Member-by-Member Behavior.
- **UpdateOldRidingSkillToNew**: One-shot 1.12 riding-skill migration (epic-mount detection sets the tier).
- **SendPacketsAtRelogin**: Re-sends the login packet subset on same-session relog.
- **IsAllowedToLoot**: Corpse loot rights from tap/recipient/round-robin rules.
- **GetMaxLootDistance**: Widened loot reach for large creatures.
- **_LoadAuras**: Restores saved aura holders with remaining durations.
- **LoadAura**: Rebuilds one aura holder row.
- **LoadCorpse**: Re-links or repop-places a dead character at login.
- **_LoadInventory**: Rebuilds containers from the inventory×item join; broken/expired/misplaced items archived or mailed, epic-mount detection.
- **_LoadItemLoot**: Restores unopened container loot from item_loot.
- **LoadPet**: Resummons the saved pet at login.
- **_LoadQuestStatus**: Rebuilds quest statuses, log slots, and the timed set.
- **_LoadSpells**: Loads the spellbook rows.
- **_LoadGroup**: Rejoins the saved group (or clears stale membership).
- **_LoadBoundInstances**: Rebuilds instance binds, deleting unresolvable/conflicting rows.
- **GetBoundInstance**: Bind lookup for a map.
- **UnbindInstance#2**: Map-id form of bind removal (resolves the bind, then the iterator worker).
- **UnbindInstance**: Iterator-form bind removal: detaches from the persistent state and deletes the `character_instance` row.
- **BindToInstance**: Creates/upgrades a bind, persisting permanent ones.
- **GetBoundInstanceSaveForSelfOrGroup**: Which persistent state governs entry (own permanent > group).
- **SendRaidInfo**: Sends the raid-info UI (permanent binds with reset times).
- **SendSavedInstances**: Sends the saved-instances handshake at login.
- **ConvertInstancesToGroup**: Migrates solo binds into group binds (group_instance).
- **_LoadHomeBind**: Loads/repairs the homebind row (race-start fallback).
- **_LoadGuild**: Restores guild fields, clearing them when membership vanished.
- **SaveNewPlayer**: Static direct-to-DB new-character writer (characters + starting spells/items/skills) without a Player object.
- **UpdateCharacterFlags**: Recomputes the persisted character_flags bitmask from live state.
- **SaveToDB**: The save transaction — the big characters REPLACE plus the _Save* family; no-op for bots.
- **SaveInventoryAndGoldToDB**: Anti-duping fast save used around trades and loot: inventory + gold serialized in one transaction.
- **SaveGoldToDB**: Fast single-column gold save.
- **_SaveAuras**: Multi-row insert of restorable auras.
- **SaveAura**: Filters which aura holders qualify for saving.
- **_SaveInventory**: Replays the item update queue into character_inventory/item_instance with duplication diagnostics.
- **_SaveQuestStatus**: Delta-based quest status persistence.
- **_SaveSkills**: Delta-based skill persistence.
- **_SaveSpells**: Delete+insert of changed spell rows (dependent spells skipped).
- **_SaveStats**: The character_stats armory dump (config-gated to logout).
- **CanSpeak**: Mute check.
- **SavePositionInDB**: Static offline position writer.
- **SendAttackSwingNotInRange**: Sends the not-in-range swing error.
- **SendAttackSwingNotStanding**: Sends the not-standing swing error.
- **SendAttackSwingDeadTarget**: Sends the dead-target swing error.
- **SendAttackSwingCantAttack**: Sends the can't-attack swing error.
- **SendAttackSwingCancelAttack**: Sends the cancel-attack packet.
- **SendAttackSwingBadFacingAttack**: Sends the bad-facing swing error.
- **SendAutoRepeatCancel**: Cancels auto-shot client-side.
- **SendFeignDeathResisted**: Feign-death resist notification.
- **SendExplorationExperience**: Exploration XP award packet.
- **SendFactionAtWar**: At-war checkbox update.
- **SendResetFailedNotify**: Instance-reset-failed notify.
- **ResetInstances**: Manual/on-change reset sweep over all binds with client feedback.
- **ResetInstance**: Resets one bind (iterator form) for the given method.
- **ResetPersonalInstanceOnLeaveDungeon**: Clears a non-permanent bind on leaving the dungeon.
- **SendResetInstanceSuccess**: Sends the reset-succeeded packet for a map.
- **SendResetInstanceFailed**: Sends the reset-failed packet with reason.
- **CheckInstanceCount**: Enforces the instances-per-hour cap.
- **AddInstanceEnterTime**: Records an instance entry time for the per-hour cap.
- **UpdatePvPFlagTimer**: Ticks the PvP drop timer.
- **UpdatePvPContestedFlagTimer**: Ticks the contested grace timer.
- **SetPvPDesired**: Persistent manual PvP flag.
- **SetFFAPvP**: Free-for-all PvP state.
- **IsInInterFactionMode**: Cross-faction interaction toggle state.
- **UpdateDuelFlag**: Starts the duel once the countdown elapses.
- **RemovePet**: Dismisses/saves the active pet per save mode.
- **RemoveMiniPet**: Dismisses the companion mini-pet.
- **GetMiniPet**: Resolves the companion from its stored guid.
- **Say**: Standard say broadcast (interfaction config respected).
- **Yell**: Yell broadcast with the zone-scaled yell radius.
- **TextEmote**: Text emote broadcast.
- **GetYellRange**: The zone-scaled yell radius.
- **SendSysMessage#2**: System message from a mangos string id (resolves the localized text first).
- **SendSysMessage**: System message from a raw string (builds and sends the chat packet).
- **PSendSysMessage#2**: printf-form system message from a mangos string id.
- **PSendSysMessage**: printf-form system message from a raw format string.
- **PetSpellInitialize**: Sends the pet action bar/spells packet.
- **PossessSpellInitialize**: Sends the possession action bar packet.
- **CharmSpellInitialize**: Sends the charm action bar packet.
- **RemovePetActionBar**: Clears the pet bar.
- **SummonPossessedMinion**: Summons a possessed minion with camera/control transfer to it.
- **UnsummonPossessedMinion**: Tears the possessed minion down and returns camera/control.
- **HasInstantCastingSpellMod**: Detects a pending instant-cast modifier for a spell.
- **IsAffectedBySpellmod**: Family/mask spell-mod applicability (charge-interaction aware).
- **AddSpellMod**: Registers/unregisters a talent spell modifier with client notification.
- **SendSpellMod**: The flat/pct modifier packet.
- **GetSpellMod**: Modifier lookup by op and spell id.
- **RestoreSpellMods**: Rolls back charges consumed by a cast that failed (optionally scoped to one owner aura).
- **RestoreAllSpellMods**: Rolls back consumed charges across all pending casts (aura-scoped variant of the recovery path).
- **RemoveSpellMods**: Finalizes charge consumption after a successful cast.
- **DropModCharge**: Consumes one modifier charge (tracked against the spell).
- **SendProficiency**: Weapon/armor proficiency packet.
- **RemovePetitionsAndSigns**: Static petition/signature teardown for a guid.
- **SetRestBonus**: Clamps and stores rest, maintaining rested-state bytes and flags.
- **ActivateTaxiPathTo**: Validated flight activation by node chain: death/combat/trade/stealth/shapeshift/cast checks (unless `nocheck`, taxi-cheat aware), endpoint knowledge, pricing with reputation discount, charging, and starting the flight-path movement generator.
- **ActivateTaxiPathTo#2**: Stored-path-id form of flight activation (resolves the node pair from the path).
- **ContinueTaxiFlight**: Resumes a saved mid-flight route at login.
- **Mount**: Mount transition: stashes the pet (config-dependent) and applies the mount display; returns the result code.
- **Unmount**: Dismount transition (aura-driven form aware); returns the result code.
- **SendMountResult**: Sends the mount outcome packet.
- **SendDismountResult**: Sends the dismount outcome packet.
- **InitDataForForm**: Shapeshift-form attack times and power type.
- **BuyItemFromVendor**: Vendor purchase — stock, restock, price with discount, extended costs, delivery.
- **SendRaidGroupOnlyError**: Raid-required map refusal packet.
- **UpdateHomebindTime**: The 60-second not-bound-here eviction timer.
- **UpdatePvP**: Arms or clears PvP state on the 5-minute drop timer (overriding form forces).
- **UpdatePvPContested**: Arms or clears contested-PvP state with its grace timer.
- **SetBattleGroundEntryPoint#2**: Stores an explicit entry point (map id + coordinates).
- **SetBattleGroundEntryPoint**: Leader-relative entry-point capture — taxi/dungeon/portal aware, with homebind fallback.
- **LeaveBattleground**: BG removal with deserter handling and entry-point teleport.
- **CanJoinToBattleground**: Deserter-aware queue eligibility.
- **IsVisibleInGridForPlayer**: Grid visibility (ghosts see spirit healers; GM tiers).
- **IsVisibleGloballyFor**: Who-list/global visibility rights.
- **UpdateVisibilityOf**: Per-target visible-set reconciliation: add/remove the target for this viewer with the file-local insert helper (which also marks quest-activated gameobjects) and broadcaster registration.
- **UpdateVisibilityOf#2**: Packet-building template form of the reconciliation, batching create/destroy blocks into an `UpdateData`.
- **UpdateVisibilityOf_helper**: File-local visible-set insert helper (marks quest-activated GOs).
- **AddBroadcastListener**: File-local registration of this player's broadcaster with a target's fan-out list.
- **RemoveBroadcastListener**: File-local removal of the broadcaster registration.
- **LeaveCombatWithFarAwayCreatures**: Drops threat links beyond leash range on continents.
- **SetLongSight**: Arms Eagle-Eye style sight-range extension from an aura.
- **UpdateLongSight**: Applies the extended sight range to the camera position.
- **ScheduleCameraUpdate**: Deferred PLAYER_FARSIGHT write for clean client view transfer.
- **InitPrimaryProfessions**: Resets the free primary-profession counter.
- **SetComboPoints**: Pushes the current combo state to the client field.
- **AddComboPoints**: Adds combo points on a target with client sync (retargeting resets).
- **ClearComboPoints**: Clears combo points and syncs the client.
- **SetGroup**: Links/unlinks the group reference.
- **SendInitialPacketsBeforeAddToMap**: First half of the world-entry packet burst, sent before map insertion: tutorials, spells, action buttons, initial spells and world states.
- **SendInitialPacketsAfterAddToMap**: Second half of the world-entry burst, sent after map insertion: auras, movement flags, and state that requires the player to exist on the map.
- **SendUpdateToOutOfRangeGroupMembers**: Flushes party-frame delta masks.
- **SendTransferAborted**: Map-entry refusal packet.
- **SendInstanceResetWarning**: Scheduled-reset warning packet.
- **ApplyEquipCooldown**: 30-second equip cooldown on use-items.
- **ResetSpells**: Wipes and relearns the spellbook from scratch.
- **LearnDefaultSpells**: Teaches the race/class default kit.
- **LearnQuestRewardedSpells#2**: Re-grants the spell taught by one specific rewarded quest (casts the reward spell on self).
- **LearnQuestRewardedSpells**: Login-time form: walks every rewarded quest and re-grants each quest-taught spell.
- **SetSemaphoreTeleportNear**: Sets/clears the near-teleport semaphore.
- **SetSemaphoreTeleportFar**: Sets/clears the far-teleport semaphore.
- **GetBattleGround**: Resolves the current BattleGround object.
- **GetBGAccessByLevel**: Level gate for a BG type.
- **GetMinLevelForBattleGroundBracketId**: Static bracket→minimum-level math for a BG type.
- **GetMaxLevelForBattleGroundBracketId**: Static bracket→maximum-level math for a BG type.
- **GetBattleGroundBracketIdFromLevel**: Member form of the bracket mapping using the player's own level.
- **GetBattleGroundBracketIdFromLevel#2**: Static level→bracket math for a BG type from the template's level bounds.
- **GetReputationPriceDiscount**: Rank-based vendor/taxi discount.
- **IsSpellFitByClassAndRace**: skill_line_ability racemask/classmask check.
- **HasQuestForGO**: Does any log quest still need this gameobject.
- **UpdateForQuestWorldObjects**: Refreshes dynamic-flag update blocks for quest-relevant objects.
- **SendSummonRequest**: Sends the summon offer and arms its expiry.
- **SummonIfPossible**: Executes an accepted summon (BG/expiry checks).
- **RemoveItemDurations**: Removes an item from the duration list.
- **AddItemDurations**: Registers a limited-lifetime item into the duration list.
- **AutoUnequipWeaponsIfNeed**: Forces weapons off on proficiency/disarm changes.
- **AutoUnequipOffhandIfNeed**: Frees the off hand when a two-hander arrives.
- **AutoUnequipItemFromSlot**: Forces one slot to the pack (mails on overflow).
- **GetZoneScript**: The active zone script.
- **HasItemFitToSpellReqirements**: An equipped item satisfies a spell's item requirements (ignore-item aware).
- **RemoveItemDependentAurasAndCasts**: Strips auras/casts that depended on a departing item.
- **SelectResurrectionSpellId**: Chooses the passive self-res spell (Soulstone/reincarnation).
- **IsHonorOrXPTarget**: Gray-level honor/XP eligibility filter.
- **RewardSinglePlayerAtKill**: Solo kill credit (XP, quest credit).
- **RewardPlayerAndGroupAtEvent**: Distributes event credit to eligible group members near the source.
- **RewardPlayerAndGroupAtCast**: Distributes cast credit to eligible group members near the source.
- **IsAtGroupRewardDistance**: The shared close-enough-for-credit test.
- **GetBaseWeaponSkillValue**: Pure skill value for the weapon in a given attack slot.
- **ResurrectUsingRequestData**: Consumes a stored resurrect offer (cross-map teleport first).
- **SetClientControl**: Grants/revokes client movement control over a unit.
- **GetConfirmedMover**: The mover the client has actually acknowledged (nullable).
- **UpdateZoneDependentAuras**: Zone-conditional aura reconciliation on zone change (applies/removes `spell_area` auras).
- **UpdateAreaDependentAuras**: Area-conditional aura reconciliation on area change.
- **GetCorpseReclaimDelay**: Returns the corpse reclaim delay for the death type — the escalating 30/60/120 s PvP-death ladder tracked in `m_deathExpireTime`.
- **UpdateCorpseReclaimDelay**: Advances the PvP-death reclaim ladder in `m_deathExpireTime` on each PvP death.
- **SendCorpseReclaimDelay**: Sends the remaining reclaim countdown to the client (also used at load).
- **GetNextRandomRaidMember**: Random in-range raid member.
- **CanUninviteFromGroup**: Kick-rights validation.
- **UpdateGroupLeaderFlag**: Leader UI flag maintenance.
- **SetBattleGroundRaid**: Swaps the player into the BG raid while keeping the world group intact.
- **RemoveFromBattleGroundRaid**: Restores the original world group after the BG raid.
- **SetOriginalGroup**: Sets the preserved non-BG group reference.
- **UpdateTerainEnvironmentFlags**: Recomputes liquid/underwater flags from terrain data.
- **SetCanParry**: Sets the parry capability flag and triggers `Player.StatSystem/UpdateParryPercentage`.
- **SetCanBlock**: Sets the block capability flag and triggers `Player.StatSystem/UpdateBlockPercentage`.
- **isContainedIn**: ItemPosCount helper — is this position already in a placement plan (defined in this file).
- **CanUseBattleGroundObject**: Flag-pickup preconditions (not stealthed/invulnerable/mounted…).
- **AutoStoreLoot**: Rolls a loot template directly into bags.
- **CalculateTalentsPoints**: Total talent points for the level.
- **DoPlayerLearnSpell**: File-local functor teaching a spell to a player — the highest-rank learning vehicle.
- **operator()**: `DoPlayerLearnSpell`'s call operator: teaches the captured spell to the visited player.
- **LearnSpellHighRank**: Teaches the highest known rank chain of a spell.
- **GetSpellRank**: Level-capped spell rank for scaling.
- **_LoadSkills**: Restores skills from `character_skills` into the packed fields (holder form).
- **LoadSkillsFromFields**: Field-array form of skill restoration (fields already populated).
- **_LoadForgottenSkills**: The 1.10.2+ weapon-skill memory rows.
- **CanEquipUniqueItem**: Unique-equipped enforcement with slot exception.
- **LearnTalent**: Tree/rank/points validation before teaching a talent rank.
- **UpdateFallInformationIfNeed**: Refreshes the fall-start height from movement info.
- **HandleFall**: Applies fall damage from recorded height (safe-fall aware).
- **FallGround**: Forces a landing state (anticheat).
- **UnsummonPetTemporaryIfAny**: Stashes the pet while mounted/teleporting/flying, remembering its number.
- **ResummonPetTemporaryUnSummonedIfAny**: Restores the stashed pet after landing/dismount.
- **IsPetNeedBeTemporaryUnsummoned**: True in states that require the pet stashed (mounted, taxi, not in world).
- **_SaveBGData**: Persists battleground identity and entry point.
- **SendClearCooldown**: Sends a single-spell cooldown-clear packet for a target.
- **SendClearAllCooldowns**: Sends the clear-all-cooldowns packet for a target.
- **SendSpellCooldown**: Sends one explicit spell-cooldown packet (spell, duration, target).
- **SendSpellRemoved**: Spell-unlearned client notification.
- **SendChannelUpdate**: Channeled-cast duration update.
- **UpdateChannelStartPosition**: Refreshes the allowed-movement anchor for moving channels.
- **HasMovementFlag**: Script access to movement flags.
- **SetHomebindToLocation**: Writes the homebind (persisted to character_homebind).
- **TeleportToHomebind**: Hearthstone-style teleport home.
- **GetSelectedUnit**: Resolves the selection as a `Unit*`.
- **GetSelectedCreature**: Resolves the selection as a `Creature*`.
- **GetSelectedPlayer**: Resolves the selection as a `Player*`.
- **GetObjectByTypeMask**: Guid resolution under a type-mask constraint.
- **SetRestType**: Switches none/tavern/city rest (tavern arms the inn trigger id).
- **SendDuelCountdown**: The 3-second duel countdown packet.
- **RemoveAI**: Detaches the current PlayerAI.
- **RemoveTemporaryAI**: Restores the bot's own AI after a temporary one ends (never deletes the bot brain).
- **SetControlledBy**: Swaps in a PlayerControlledAI for mind control (bot-AI protected).
- **ChangeRace**: Live race/faction conversion — spells, reputations, quests, items, homebind, binds — save-disabled throughout.
- **GetPriestSpellForRace**: File-local racial priest-spell table.
- **GetCapitalReputationForRace**: File-local race→capital faction table.
- **ConvertSpell**: Swaps one known spell for another.
- **ChangeSpellsForRace**: Race-change pass converting race-linked spells via the conversion tables.
- **ChangeItemsForRace**: Race-change pass mapping cross-faction items, including bank and equipped gear.
- **ChangeReputationsForRace**: Race-change pass transposing reputations between faction pairs.
- **ChangeQuestsForRace**: Race-change pass swapping faction-specific quests.
- **IsImmuneToSpellEffect**: Player-specific spell-effect immunity (GM immunity et al.) over the Unit version.
- **AddItem**: GM/utility item grant (store or mail on overflow).
- **SendDestroyGroupMembers**: Despawns group members client-side on ungroup.
- **RefreshBitsForVisibleUnits**: Update-mask refresh for all visible units (dynamic-flag changes).
- **SetSession**: Rebinds the session pointer (bot handover).
- **InterruptSpellsWithCastItem**: Cancels casts whose cast item is leaving.
- **GetShortDescription**: The "player:guid [account@IP]" logging string.
- **LootMoney**: Group-splits looted money server-side with logging.
- **RewardHonor**: Dishonorable-kill and racial-leader honor grants (patch/config-gated).
- **RewardHonorOnDeath**: Distributes honor shares from the last minute of damage history at death.
- **OnReceivedItem**: Scourge-invasion item-received hook.
- **HasFreeBattleGroundQueueId**: Whether a queue slot is free.
- **TaxiStepFinished**: Advances multi-hop flights (charging each leg) or lands; bot-aware handoff.
- **HandleStealthedUnitsDetection**: Periodic proximity reveal of stealthed units.
- **IsInVisibleList**: Lock-guarded visible-set membership test.
- **GetSocial**: The MasterPlayer-side social list (asserts master presence).
- **FindSocial**: Nullable social-list variant.
- **DeletePacketBroadcaster**: Tears down the per-player packet fan-out object.
- **CreatePacketBroadcaster**: Creates the per-player packet fan-out object.
- **AddGCD**: Starts the global cooldown (optionally client-updated).
- **AddCooldown**: Computes and stores spell/category/item cooldowns (proto overrides, permanent cooldowns).
- **RemoveSpellCooldown**: Clears one spell's cooldown, optionally syncing the client.
- **RemoveSpellCategoryCooldown**: Clears a whole category cooldown, optionally syncing the client.
- **RemoveAllCooldowns**: Clears every cooldown (or only notifies the client when `sendOnly`).
- **LockOutSpells**: School lockout (counterspell/kick).
- **RemoveSpellLockout**: Lifts a school lockout, avoiding duplicate clear packets.
- **CastHighestStealthRank**: Casts the best known Stealth rank (Vanish, Improved Sap).
- **PlayerLogHeaderToConsole**: File-local Log helper writing the account/character header for console-bound player-log lines.
- **PlayerLogHeaderToFile**: File-local Log helper writing the account/character header for file-bound player-log lines.
- **IsPlayerLoggingEnabledToDB**: File-local per-type config gate for DB player logging.
- **PlayerLogToDB**: File-local logs_player insert with account, IP, guid, name, map, and position.
- **Player**: `Log::Player` overload — session-keyed, with subtype; fans out to the DB gate, `logs_player` insert, and file header writers.
- **Player#2**: `Log::Player` overload — session-keyed, no subtype.
- **Player#3**: `Log::Player` overload — account-keyed, with subtype.
- **Player#4**: `Log::Player` overload — account-keyed, no subtype.
- **ClearTemporaryWarWithFactions**: Resets scripted at-war bits and notifies the client.

---

<!-- machine-true, projected from graph.json -->

## Map — Player.Main

*Source:* Player.cpp, Player.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Player#5 | ctor | Camera/Camera, GossipDef/PlayerMenu, HonorMgr/ClearHonorData, HonorMgr/HonorMgr, ReputationMgr/ReputationMgr, shared_Util/urand, TicketMgr/GetLastTicketId, TicketMgr/instance, Unit.Main/Unit, World/getConfig#4, WorldSession.Main/GetSecurity, WorldSession.Main/InitCheatData | PlayerBotAI/SpawnNewPlayer, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| ~Player | dtor | DungeonPersistentState/RemovePlayer, Errors/PrintStacktraceAndThrow, MapManager/CancelDelayedPlayerTeleport | — | — |
| CleanupsBeforeDelete | method | Unit.Main/CleanupsBeforeDelete | Map.Main/Remove#3, WorldSession.Main/LogoutPlayer | — |
| ValidateAppearance | method | DBCStores/GetCharFacialHairEntry, DBCStores/GetCharSectionEntry | WorldSession.CharacterHandler/HandleCharCreateOpcode | — |
| SelectRandomAppearance | method | DBCStores/GetAllValidCharSectionVariationAndColorPairs | PlayerBotAI/SpawnNewPlayer | — |
| Create | method | Log.Main/Out, MapManager/CreateMap, MapManager/GetContinentInstanceId, Object/SetGuidValue, ObjectGuid/ObjectGuid, ObjectMgr/GetPlayerInfo, Player.StatSystem/UpdateMaxHealth#2, Player.StatSystem/UpdateMaxPower#2, ReputationMgr/LoadFromDB, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPowerType, Unit.Main/InitPlayerDisplayIds, Unit.Main/SetHealth, Unit.Main/SetPower, World/getConfig#4, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/Relocate#2, WorldObject.Object/SetByteValue, WorldObject.Object/SetFloatValue, WorldObject.Object/SetInt32Value, WorldObject.Object/SetLocationInstanceId, WorldObject.Object/SetLocationMapId, WorldObject.Object/SetMap, WorldObject.Object/SetUInt16Value, WorldObject.Object/SetUInt32Value, WorldObject.Object/_Create, WorldSession.Main/GetSecurity | PlayerBotAI/SpawnNewPlayer | — |
| AddStartingItems | method | Log.Main/Out, Object/GetEntry, ObjectMgr/GetPlayerInfo, Unit.Main/GetClass, Unit.Main/GetRace | CombatBotBaseAI/AutoEquipGear | — |
| StoreNewItemInBestSlots | method | game_Objects_Item/ClearEnchantment, game_Objects_Item/GenerateItemRandomPropertyId, game_Objects_Item/GetProto, game_Objects_Item/SetCount, game_Objects_Item/SetEnchantment, game_Objects_Item/SetItemRandomProperties, ItemPrototype/GetMaxStackSize, Log.Main/Out, Unit.Main/GetClass, Unit.Main/GetRace | CombatBotBaseAI/EquipRandomGearInEmptySlots, ObjectMgr/ApplyPremadeGearTemplateToPlayer, PartyBotAI/CloneFromPlayer | — |
| StoreNewItemInInventorySlot | method | game_Objects_Item/GenerateItemRandomPropertyId | ChatHandler.CharacterCommands/HandleGroupAddItemCommand, Map.ScriptCommands/ScriptCommand_CreateItem | — |
| SatisfyItemRequirements | method | game_Objects_Item/GetProficiencySpell, HonorMgr/CalculateRankInfo, HonorMgr/GetHighestRank, HonorMgr/GetRank, HonorMgr/SetHighestRank, HonorMgr/SetRank, HonorRankInfo/HonorRankInfo, ObjectMgr/GetFactionEntry, ReputationMgr/GetRank, ReputationMgr/GetRepPointsToRank, ReputationMgr/SetReputation, Unit.Main/GetLevel, World/getConfig, World/GetWowPatch, WorldObject.Object/SetUInt32Value | CombatBotBaseAI/EquipRandomGearInEmptySlots, ObjectMgr/ApplyPremadeGearTemplateToPlayer, PartyBotAI/CloneFromPlayer | — |
| EnvironmentalDamage | method | Log.Main/Out, SpellCaster/DealDamageMods, Unit.Main/CalculateDamageAbsorbAndResist, Unit.Main/DealDamage, Unit.Main/IsAlive, Unit.Main/IsImmuneToDamage, Unit.Main/SendEnvironmentalDamageLog, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Spell.Effects/EffectEnvironmentalDMG, WorldSession.MovementHandler/HandleMoverRelocation | — |
| GetAuctionAccessMode | method | — | AuctionHouseMgr/GetAuctionHouseEntry, WorldSession.AuctionHouseHandler/GetCheckedAuctionHouseForAuctioneer | — |
| HandleSobering | method | — | — | — |
| SetAuctionAccessMode | method | — | ChatHandler.MiscCommands/HandleAuctionAllianceCommand, ChatHandler.MiscCommands/HandleAuctionCommand, ChatHandler.MiscCommands/HandleAuctionGoblinCommand, ChatHandler.MiscCommands/HandleAuctionHordeCommand | — |
| GetDrunkenstateByValue | method | — | — | — |
| SetDrunkValue | method | Unit.Main/GetGender, WorldObject.Object/SetUInt16Value | ChatHandler.CharacterCommands/HandleModifyDrunkCommand, Spell.Effects/EffectInebriate | — |
| IsAcceptTickets | method | — | World/SendGMTicketText, World/SendGMTicketText#2 | — |
| SetAcceptTicket | method | — | ChatHandler.TicketCommands/HandleGMTicketNotifyCommand | — |
| IsGameMaster | method | — | BattleBotAI.Main/SelectFollowTarget, BattleGroundAB/GetClosestGraveYard, BattleGroundAV/GetClosestGraveYard, blackrock_depths/AreaTrigger_at_shadowforge_bridge, boss_cthun/AggroRadius, boss_cthun/SelectRandomAliveNotStomach, boss_fankriss/UpdateAI, boss_maexxna/DoCastWebWrap, boss_mandokir/MoveInLineOfSight, boss_nefarian/HandleClassCall, boss_ouro/MoveInLineOfSight#3, boss_sapphiron/OnUse, boss_vaelastrasz/GossipHello_boss_vael, boss_ysondre/DoSpecialAbility, ChatHandler.MiscCommands/HandleGMCommand, ChatHandler.MiscCommands/HandleGMListIngameCommand, CombatBotBaseAI/SelectBuffTarget, CombatBotBaseAI/SelectBuffTarget#2, CombatBotBaseAI/SelectDispelTarget, Creature.Main/IsVisibleInGridForPlayer, Creature.Main/MeetsSelectAttackingRequirement, darkshore/at_murloc_camp, dustwallow_marsh/AreaTrigger_at_sentry_point, eastern_plaguelands/Reset, GameObject/IsFriendlyTo, GameObject/IsHostileTo, GameObject/IsVisibleForInState, GameObject/PlayerCanUse, game_Battlegrounds_BattleGround/ReturnPlayersToHomeGY, game_Group_Group/_homebindIfInstance, instance_blackrock_spire/AreaTrigger_at_blackrock_spire, instance_blackwing_lair/ApplyAura, instance_blackwing_lair/AreaTrigger_at_enter_vael_room, instance_naxxramas.boss_kelthuzad/CheckForEnemyPlayers, instance_naxxramas.Main/AreaTrigger_at_naxxramas, instance_naxxramas.Main/onNaxxramasAreaTrigger, instance_stratholme/JoueurDansPiegeRat1, instance_stratholme/JoueurDansPiegeRat2, instance_stratholme/Update, instance_temple_of_ahnqiraj/AreaTrigger_at_temple_ahnqiraj, instance_temple_of_ahnqiraj/HandleStomachTriggers, loch_modan/AreaTrigger_at_huldar_miran, Map.Main/CanEnter#2, Map.Main/GetPlayersCountExceptGMs, MapManager/CanPlayerEnter, PartyBotAI/UpdateAI, quest_stormwind_rendezvous/AreaTrigger_at_stormwind_gates, ScriptedInstance/GetPlayerInMap, Spell.Effects/EffectSpiritHeal, Spell.Main/CheckCast, Spell.Main/CheckTarget, ThreatListCopier.boss_ragnaros/CheckForMelee, ThreatListCopier.boss_ragnaros/UpdateAI, ThreatManager/addThreat#4, ThreatManager/addThreatDirectly, ThreatManager/updateOnlineStatus, ungoro_crater/AreaTrigger_at_scent_larkorwi, Unit.Main/Attack, Unit.Main/IsTargetableBy, Unit.Main/IsVisibleForOrDetect, Unit.Main/TauntApply, Unit.Main/TauntFadeOut, WorldObject.Object/BuildValuesUpdate, WorldObject.Object/CanSeeInWorld, WorldObject.Object/CanSeeInWorld#2, WorldObject.Object/GetReactionTo, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.ItemHandler/SendListInventory, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.MovementHandler/HandleMoverRelocation, world_event_wareffort/AggroAllPlayerNear, world_event_wareffort/MoreThanOnePlayerNear, world_event_wareffort/MoveInLineOfSight#3 | — |
| IsGMChat | method | — | ChatHandler.MiscCommands/HandleGMChatCommand | — |
| IsTaxiCheater | method | — | WorldSession.TaxiHandler/SendTaxiMenu | — |
| SetTaxiCheater | method | — | ChatHandler.CharacterCommands/HandleTaxiCheatCommand, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| SetPvPDeath | method | — | Unit.Main/Kill | — |
| IsGMVisible | method | — | ChatHandler.MiscCommands/HandleGMVisibleCommand, instance_stratholme/JoueurDansPiegeRat1, instance_stratholme/JoueurDansPiegeRat2, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetWaterBreathingInterval | method | World/getConfig#4 | — | — |
| SetWaterBreathingIntervalMultiplier | method | MirrorTimer/SetDuration, MirrorTimer/SetScale | Unit.SpellAuras/HandleModWaterBreathing, Unit.SpellAuras/HandleWaterBreathing | — |
| GetCheatOptions | method | — | — | — |
| HasCheatOption | method | — | ChatHandler.CharacterCommands/HandleCheatStatusCommand, MapManager/CanPlayerEnter, MovementAnticheat/CheckMoveStart, MovementAnticheat/HandleFlagTests, PartyBotAI/UpdateAI, Spell.Main/CheckCast, Spell.Main/finish, Spell.Main/HandleAddTargetTriggerAuras, Spell.Main/prepare#2, Spell.Main/SendSpellCooldown, Spell.Main/TakePower, Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.Main/IsSpellCrit, Unit.Main/RollMeleeOutcomeAgainst#2, WorldObject.Object/BuildValuesUpdate, WorldObject.Object/GetUpdateFieldFlagsForTarget, WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| EnableCheatOption | method | — | — | — |
| RemoveCheatOption | method | — | — | — |
| SetCheatOption | method | — | PartyBotAI/UpdateAI | — |
| SetEnvironmentFlags | method | HostileRefManager/updateThreatTables, MirrorTimer/SetScale, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/GetHostileRefManager, Unit.Main/RemoveAurasWithInterruptFlags | — | — |
| GetGMInvisibilityLevel | method | — | ChatHandler.MiscCommands/HandleGMVisibleCommand, MasterPlayer.Main/LoadPlayer, Unit.Main/IsVisibleForOrDetect, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| SetGMInvisibilityLevel | method | — | ChatHandler.MiscCommands/HandleGMVisibleCommand | — |
| GetGMTicketCounter | method | — | ChatHandler.TicketCommands/HandleGMTicketNextCommand, ChatHandler.TicketCommands/HandleGMTicketPreviousCommand | — |
| SetGMTicketCounter | method | — | ChatHandler.TicketCommands/HandleGMTicketCounterCommand, ChatHandler.TicketCommands/HandleGMTicketNextCommand, ChatHandler.TicketCommands/HandleGMTicketPreviousCommand | — |
| CanTakeMoreSimilarItems | method | — | — | — |
| CanTakeMoreSimilarItems#2 | method | — | — | — |
| SendMirrorTimerStart | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#4, ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendMirrorTimerStop | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendMirrorTimerPause | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| GetWeaponForAttack | method | — | Player.StatSystem/GetWeaponBasedAuraModifier#2, Spell.Main/CheckItems, Spell.Main/WriteAmmoToPacket, Unit.SpellAuras/TriggerSpell | — |
| FreezeMirrorTimers | method | MirrorTimer/GetSpellId, MirrorTimer/SetFrozen | — | — |
| GetItemUpdateQueue | method | — | ChatHandler.DebugCommands/HandleDebugGetItemStateCommand | — |
| IsMainHandPos | method | — | — | — |
| IsInventoryPos#2 | method | — | Spell.Effects/EffectSummonChangeItem, WorldSession.ItemHandler/HandleAutoEquipItemOpcode | — |
| IsEquipmentPos#2 | method | — | Spell.Effects/EffectSummonChangeItem, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBagItemOpcode, WorldSession.ItemHandler/HandleDestroyItemOpcode | — |
| IsBankPos#2 | method | — | Spell.Effects/EffectSummonChangeItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.MailHandler/HandleSendMailCallback | — |
| SendMirrorTimers | method | MirrorTimer/FetchStatus, MirrorTimer/GetDuration, MirrorTimer/GetRemaining, MirrorTimer/GetScale, MirrorTimer/GetSpellId, MirrorTimer/GetType, MirrorTimer/IsActive, MirrorTimer/IsFrozen | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| IsValidPos#2 | method | — | — | — |
| GetBankBagSlotCount | method | — | WorldSession.ItemHandler/HandleBuyBankSlotOpcode | — |
| SetBankBagSlotCount | method | — | WorldSession.ItemHandler/HandleBuyBankSlotOpcode | — |
| CanStoreNewItem | method | — | ChatHandler.CharacterCommands/HandleAddItemCommand, ChatHandler.CharacterCommands/HandleAddItemSetCommand, CombatBotBaseAI/AddItemToInventory, darkshore/QuestAcceptGO_beached_sea, game_Battlegrounds_BattleGround/RewardItem, game_Group_Group/CountSingleLooterRoll, game_Group_Group/CountTheRoll, Pet.Main/Unsummon, Spell.Effects/DoCreateItem, Spell.Main/CheckItems, spell_warlock/OnCheckCast#2, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, Unit.SpellAuras/HandleChannelDeathItem, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode | — |
| CanStoreItem | method | — | AiBotAI.Loot/TryAutoEquip, AiBotAI.Loot/TryAutoEquipBags, Spell.Effects/EffectSummonChangeItem, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBagItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBankItemOpcode, WorldSession.ItemHandler/HandleBuybackItem, WorldSession.MailHandler/HandleMailCreateTextItem, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.TradeHandler/MoveItems | — |
| UpdateMirrorTimers | method | MirrorTimer/GetSpellId, MirrorTimer/GetType, MirrorTimer/IsActive, MirrorTimer/SetRemaining, MirrorTimer/Start, MirrorTimer/Start#2, MirrorTimer/Stop, MirrorTimer/Update, SpellAuraHolder/GetAuraDuration, SpellAuraHolder/GetAuraMaxDuration, Unit.SpellAuras/GetId | — | — |
| BankItem | method | — | Spell.Effects/EffectSummonChangeItem, WorldSession.ItemHandler/HandleAutoBankItemOpcode, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBankItemOpcode | — |
| GetBuyBackItemPrice | method | — | WorldSession.ItemHandler/HandleBuybackItem | — |
| CheckMirrorTimerActivation | method | Unit.Main/IsFeigningDeath, Unit.Main/IsTaxiFlying, WorldObject.Object/GetTransport | — | — |
| GetMaxKeyringSize | method | — | — | — |
| AddWeaponProficiency | method | — | Spell.Effects/EffectProficiency | — |
| AddArmorProficiency | method | — | Spell.Effects/EffectProficiency | — |
| CheckMirrorTimerDeactivation | method | Object/HasFlag, Unit.Main/GetShapeshiftForm, Unit.Main/IsAlive, Unit.Main/IsFeigningDeath | — | — |
| GetWeaponProficiency | method | — | Spell.Effects/EffectProficiency | — |
| GetArmorProficiency | method | — | Spell.Effects/EffectProficiency | — |
| IsTwoHandUsed | method | — | — | — |
| GetTradeData | method | — | Spell.Main/CheckCast, SpellCastTargetsInfo/Update, SpellEntry/GetCastTime, TradeData/GetTraderData, WorldSession.TradeHandler/HandleClearTradeItemOpcode, WorldSession.TradeHandler/HandleSetTradeGoldOpcode, WorldSession.TradeHandler/HandleSetTradeItemOpcode, WorldSession.TradeHandler/SendUpdateTrade | — |
| GetMoney | method | — | AiBotAI.Bridge/BridgeHandleRepairItems, AiBotAI.Bridge/BridgeHandleSellItems, AiBotAI.Bridge/BridgeHandleTakeFlight, AiBotAI.Bridge/BridgeHandleTrain, AiBotAI.Bridge/BridgeSendState, AiBotAI.Loot/DoAutoLoot, AsyncCommandHandlers/HandlePInfoCommand, ChatHandler.CharacterCommands/HandleGoldRemoval, ChatHandler.CharacterCommands/HandleModifyMoneyCommand, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.GuildHandler/HandleSaveGuildEmblemOpcode, WorldSession.ItemHandler/HandleBuybackItem, WorldSession.ItemHandler/HandleBuyBankSlotOpcode, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleMailTakeMoney, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.NPCHandler/HandleBuyStableSlot, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.PetHandler/HandlePetUnlearnOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/HandleSetTradeGoldOpcode | — |
| ModifyMoney | method | — | AiBotAI.Bridge/BridgeHandleSellItems, AiBotAI.Bridge/BridgeHandleTrain, AiBotAI.Bridge/BridgeHandleUseGameObject, AiBotAI.Loot/DoAutoLoot, ChatHandler.CharacterCommands/HandleGoldRemoval, WaypointMovementGenerator/Update, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.GuildHandler/HandleSaveGuildEmblemOpcode, WorldSession.ItemHandler/HandleBuybackItem, WorldSession.ItemHandler/HandleBuyBankSlotOpcode, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.NPCHandler/HandleBuyStableSlot, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.PetHandler/HandlePetUnlearnOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| OnMirrorTimerExpirationPulse | method | Object/HasFlag, shared_Util/urand, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/IsAlive, World/getConfig#4 | — | — |
| SetMoney | method | — | ChatHandler.CharacterCommands/HandleModifyMoneyCommand | — |
| GetLootGuid | method | — | game_Objects_Item/CanBeTraded, Unit.Main/ModConfuseSpell, Unit.SpellAuras/HandleAuraModStun, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/ModPossess, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.LootHandler/HandleLootMoneyOpcode, WorldSession.LootHandler/HandleLootReleaseOpcode, WorldSession.Main/LogoutPlayer, WorldSession.MiscHandler/HandleLogoutRequestOpcode, WorldSession.MovementHandler/HandleMoverRelocation, WorldSession.SpellHandler/HandleCastSpellOpcode | — |
| SetLootGuid | method | — | WorldSession.LootHandler/DoLootRelease | — |
| GetMirrorTimerMaxDuration | method | MirrorTimer/GetDuration, World/getConfig#4 | — | — |
| GetMirrorTimerBuff | method | Aura/GetHolder, SpellAuraHolder/GetAuraMaxDuration, Unit.Main/GetAurasByType, Unit.Main/IsFeigningDeath | — | — |
| IsCityProtector | method | — | — | — |
| GetQuestSlotQuestId | method | — | — | — |
| SetCityTitle | method | Unit.Main/GetRace, WorldObject.Object/SetByteValue | ChatHandler.CharacterCommands/HandleCharacterCityTitleCommand | — |
| SetQuestSlot | method | — | — | — |
| RemoveCityTitle | method | WorldObject.Object/SetByteValue | ChatHandler.CharacterCommands/HandleCharacterCityTitleCommand | — |
| SetQuestSlotCounter | method | — | — | — |
| CanAutoAttackTarget | method | Unit.Main/CanAutoAttackTarget, WorldObject.Object/IsValidAttackTarget | — | — |
| SetQuestSlotState | method | — | — | — |
| RemoveQuestSlotState | method | — | — | — |
| SetQuestSlotTimer | method | — | — | — |
| GetQuestLevelForPlayer | method | — | WorldSession.QuestHandler/GetDialogStatus | — |
| Update | method | Log.Main/Out, Map.Main/GetGridActivationDistance, Map.Main/GetId, Map.Main/Instanceable, Map.Main/IsContinent, Map.Main/IsDungeon, MapManager/GetContinentInstanceId, MapManager/ScheduleInstanceSwitch, MovementAnticheat/Update, Object/GetGUIDLow, Object/HasFlag, Object/IsInWorld, Object/SetGuidValue, ObjectGuid/Clear, PlayerAI/UpdateAI, Unit.Main/GetDeathState, Unit.Main/HasPendingMovementChange#2, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/IsTaxiFlying, Unit.Main/Update, Unit.Main/UpdateMeleeAttackingState, World/getConfig#4, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetTransport, WorldObject.Object/GetZoneAndAreaId, WorldObject.Object/LoadMapCellsAround, WorldSession.Main/IsConnected, WorldSession.Main/ProcessAnticheatAction | — | — |
| SwapQuestSlot | method | — | WorldSession.QuestHandler/HandleQuestLogSwapQuest | — |
| GetQuestStatusMap | method | — | AiBotAI.Bridge/BridgeHandleQueryQuestStatus, AiBotAI.Bridge/BridgeHandleQuestInteract, AiBotAI.Bridge/BridgeHandleSellItems, AiBotAI.Bridge/BridgeSendHello, AiBotAI.Bridge/BridgeSendState, AiBotAI.Grind/ScanApproachTarget, ChatHandler.CharacterCommands/HandleQuestRemoveCommand, WorldSession.QuestHandler/GetDialogStatus | — |
| GetQuestShareInfo | method | — | WorldSession.QuestHandler/HandlePushQuestToParty, WorldSession.QuestHandler/HandleQuestConfirmAccept, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, WorldSession.QuestHandler/HandleQuestPushResult | — |
| SetQuestShareInfo | method | — | WorldSession.QuestHandler/HandlePushQuestToParty, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode | — |
| ClearQuestShareInfo | method | — | WorldSession.QuestHandler/HandleQuestConfirmAccept, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, WorldSession.QuestHandler/HandleQuestPushResult | — |
| GetInGameTime | method | — | — | — |
| SetInGameTime | method | — | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| AddTimedQuest | method | — | — | — |
| RemoveTimedQuest | method | — | — | — |
| HasCharacterFlag | method | — | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| SetCharacterFlag | method | — | ChatHandler.CharacterCommands/HandleCharacterRenameCommand, ChatHandler.CharacterCommands/HandleResetAllCommand | — |
| GetSaveTimer | method | — | ChatHandler.CharacterCommands/HandleSaveCommand | — |
| SetSaveTimer | method | — | — | — |
| IsSavingDisabled | method | — | game_Group_Group/_addMember#2, Pet.Main/SavePetToDB, Spell.Main/CheckTamingSpell, WorldSession.TradeHandler/MoveItems | — |
| SetSaveDisabled | method | — | AiBotAI.Main/UpdateAI | — |
| _SetMiniPet | method | — | Pet.Main/Unsummon, Spell.Effects/EffectSummonCritter | — |
| GetTemporaryUnsummonedPetNumber | method | — | Pet.Main/SavePetToDB, Pet.Main/Unsummon, WorldSession.MovementHandler/ExecuteTeleportNear | — |
| SetTemporaryUnsummonedPetNumber | method | — | Pet.Main/LoadPetFromDB | — |
| GetSpellMap#2 | method | — | GameObject/GetSpellForLock, PartyBotAI/CloneFromPlayer | — |
| GetSpellMap | method | — | ChatHandler.CharacterCommands/HandleListTalentsCommand, CombatBotBaseAI/PopulateSpellData, PlayerAI/PlayerControlledAI, Unit.Main/ModifyAuraState, Unit.SpellAuras/HandleShapeshiftBoosts | — |
| OnDisconnected | method | Map.Main/GetHeight, MovementAnticheat/Finalize, Object/IsInWorld, Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/SendHeartBeat, Unit.Main/SetRootedReal, Unit.Main/SetSplineDonePending, Unit.Main/SetStandState, Unit.Main/ShouldBeRooted, World/getConfig#4, WorldObject.Object/FindMap, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/RemoveUnitMovementFlag, WorldSession.LootHandler/DoLootRelease, WorldSession.Main/ProcessAnticheatAction | WorldSession.Main/ForcePlayerLogoutDelay | — |
| RelocateToLastClientPosition | method | Map.Main/DoPlayerGridRelocation, WorldObject.Object/GetMap | Unit.SpellAuras/ModPossess | — |
| GetFreeTalentPoints | method | — | CombatBotBaseAI/LearnRandomTalents | — |
| SetFreeTalentPoints | method | — | ChatHandler.CharacterCommands/HandleModifyTalentCommand | — |
| GetSafePosition | method | WorldObject.Object/GetPosition#2 | — | — |
| SetWorldMask | method | Object/GetGUID, ObjectMgr/SetPlayerWorldMask, WorldObject.Object/GetWorldMask, WorldObject.Object/SetWorldMask | — | — |
| UpdateCinematic | method | Camera/ResetView, Camera/SetView, ObjectMgr/GetCinematicInitialPosition, ObjectMgr/GetCinematicPosition, WorldObject.Object/SummonCreature#2 | — | — |
| InitStatBuffMods | method | — | — | — |
| SetPersonalXpRate | method | — | ChatHandler.CharacterCommands/HandleModifyXpRateCommand | — |
| GetPersonalXpRate | method | — | Pet.Main/GivePetXP | — |
| GetComboPoints | method | — | AiBotAI.Combat/UpdateInCombatAI_Druid, AiBotAI.Combat/UpdateInCombatAI_Rogue, BattleBotAI.Main/UpdateInCombatAI_Druid, BattleBotAI.Main/UpdateInCombatAI_Rogue, PartyBotAI/UpdateInCombatAI_Druid, PartyBotAI/UpdateInCombatAI_Rogue, SpellCaster/CalculateSpellEffectValue, SpellEntry/CalculateDuration, spell_druid/OnEffectExecute#2, spell_rogue/OnEffectExecute, Unit.SpellAuras/CalculateDotDamage | — |
| GetComboTargetGuid | method | — | Spell.Main/CheckPower, SpellCaster/CalculateSpellEffectValue, Unit.Main/ClearComboPointHolders, Unit.SpellAuras/HandleAuraRetainComboPoints, WorldSession.MiscHandler/HandleSetSelectionOpcode | — |
| CinematicStart | method | WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| CinematicEnd | method | Camera/ResetView | WorldSession.MiscHandler/HandleCompleteCinematic | — |
| CanParry | method | — | Player.StatSystem/UpdateParryPercentage, SpellCaster/MeleeSpellHitResult, Unit.Main/GetUnitParryChance | — |
| CanBlock | method | — | Player.StatSystem/UpdateBlockPercentage, SpellCaster/MeleeSpellHitResult, Unit.Main/GetUnitBlockChance | — |
| CanDualWield | method | — | CombatBotBaseAI/EquipRandomGearInEmptySlots, Player.StatSystem/UpdateAttackPowerAndDamage#3 | — |
| SetCanDualWield | method | — | Spell.Effects/EffectDualWield | — |
| SetDeathState | method | Object/GetUInt32Value, Unit.Main/IsAlive, Unit.Main/RemoveAuraTypeOnDeath, Unit.Main/ResetExtraAttacks, Unit.Main/SetDeathState, Unit.Main/Uncharm, WorldObject.Object/SetUInt32Value, WorldSession.LootHandler/DoLootRelease, ZoneScript/OnPlayerDeath | — | — |
| ApplyStatBuffMod | method | — | Unit.SpellAuras/HandleAuraModStat | — |
| ApplyStatPercentBuffMod | method | — | Unit.SpellAuras/HandleModTotalPercentStat | — |
| GetPosStat | method | — | ChatHandler.UnitCommands/HandleUnitStatInfoCommand | — |
| GetNegStat | method | — | ChatHandler.UnitCommands/HandleUnitStatInfoCommand | — |
| GetResistanceBuffMods | method | — | ChatHandler.UnitCommands/HandleUnitStatInfoCommand | — |
| SetResistanceBuffMods | method | — | — | — |
| ApplyResistanceBuffModsMod | method | — | Unit.SpellAuras/HandleAuraModResistance, Unit.SpellAuras/HandleAuraModResistanceExclusive, Unit.SpellAuras/HandleModResistancePercent | — |
| ApplyResistanceBuffModsPercentMod | method | — | — | — |
| GetAmmoDPS | method | — | Player.StatSystem/CalculateMinMaxDamage | — |
| SetBaseModValue | method | — | Player.StatSystem/UpdateAllCritPercentages | — |
| GetTotalPercentageModValue | method | — | Player.StatSystem/UpdateCritPercentage | — |
| GetFreePrimaryProfessionPoints | method | — | custom_creatures/CompleteLearnProfession, WorldSession.NPCHandler/SendTrainerList | — |
| SetFreePrimaryProfessions | method | — | — | — |
| GetBaseDefenseSkillValue | method | — | — | — |
| GetSkillValue | method | — | ChatHandler.CharacterCommands/HandleSetSkillCommand, CombatBotBaseAI/EquipRandomGearInEmptySlots, GameObject/Use, game_Objects_Item/AddItemsSetItem, instance_naxxramas.Main/GossipHello_npc_MasterCraftsmanOmarion, instance_naxxramas.Main/GossipSelect_npc_MasterCraftsmanOmarion, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonWild, Spell.Main/CanOpenLock, Spell.Main/CheckCast, SpellCaster/GetDefenseSkillValue, SpellCaster/GetWeaponSkillValue, WorldSession.LootHandler/DoLootRelease | — |
| GetSkillValueBase | method | — | Conditions/Evaluate, go_scripts/GOHello_go_field_repair_bot_74A, instance_dire_maul/GossipHello_npc_knot_thimblejack | — |
| GetSkillValuePure | method | — | ChatHandler.LookupCommands/HandleLookupSkillCommand, Spell.Effects/EffectLearnSkill, Spell.Effects/EffectOpenLock, Spell.Effects/EffectSkinning | — |
| GetSkillMax | method | — | SpellCaster/GetDefenseSkillValue | — |
| GetSkillMaxPure | method | — | ChatHandler.CharacterCommands/HandleLearnAllRecipesCommand, ChatHandler.CharacterCommands/HandleSetSkillCommand, ChatHandler.LookupCommands/HandleLookupSkillCommand | — |
| GetSkillBonusPermanent | method | — | ChatHandler.LookupCommands/HandleLookupSkillCommand | — |
| GetSkillBonusTemporary | method | — | ChatHandler.LookupCommands/HandleLookupSkillCommand | — |
| AutoReSummonPet | method | Object/IsPlayer, Object/ToPlayer, Pet.Main/SetDeathState, Spell.Effects/EffectSummonPet#2, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/ClearUnitState, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetPet, Unit.Main/IsAlive, Unit.Main/SetHealth, WorldObject.Object/RemoveFlag, WorldObject.Object/SetUInt32Value | Spell.Effects/EffectSpiritHeal | — |
| SetJustBoarded | method | — | WorldSession.MovementHandler/HandleMoverRelocation, WorldSession.MovementHandler/HandleMoveTimeSkippedOpcode | — |
| HasJustBoarded | method | — | WorldSession.MovementHandler/HandleMoveTimeSkippedOpcode | — |
| SetCanDelayTeleport | method | — | WorldSession.Main/ExecuteOpcode | — |
| IsHasDelayedTeleport | method | — | WorldSession.Main/ExecuteOpcode | — |
| SetDelayedTeleportFlagIfCan | method | — | — | — |
| ScheduleDelayedOperation | method | — | — | — |
| BuildEnumData | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, Field/GetCppString, Field/GetFloat, Field/GetString, Field/GetUInt32, Field/GetUInt8, Log.Main/Out, ObjectGuid/ObjectGuid#2, ObjectGuid/operator<<, ObjectMgr/GetCreatureTemplate, ObjectMgr/GetItemPrototype, ObjectMgr/GetPlayerInfo, QueryResult/Fetch, shared_Util/GetUInt32ValueFromArray, shared_Util/StrSplit | WorldSession.CharacterHandler/HandleCharEnum | — |
| GetCachedZoneId | method | — | game_Guild_Guild/AddMember, game_Guild_Guild/Roster, game_Guild_Guild/SetMemberStats, Map.ScriptCommands/ScriptCommand_StartScriptOnZone, MasterPlayer.Main/LoadPlayer, MovementAnticheat/IsInTransportArea, ObjectMgr/InsertPlayerInCache, ObjectMgr/UpdatePlayerCache, Unit.Main/TeleportPositionRelocation, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.GroupHandler/BuildPartyMemberStatsPacket, WorldSession.MiscHandler/operator() | — |
| GetCachedAreaId | method | — | MasterPlayer.Main/LoadPlayer, MovementAnticheat/IsInTransportArea, Unit.Main/TeleportPositionRelocation, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetGridRef | method | — | — | — |
| GetMapRef | method | — | Map.Main/Add#3, Map.Main/CanEnter, Map.Main/CanEnter#2, Map.Main/Remove#3, Map.Main/TeleportAllPlayersTo | — |
| GetTeleportDest | method | — | MapManager/CreateNewInstancesForPlayers, MapManager/ScheduleNewWorldOnFarTeleport, WorldSession.MovementHandler/ExecuteTeleportNear, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| IsBeingTeleported | method | — | AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, boss_patchwerk/DoHatefulStrike, ChatHandler.TeleportCommands/HandleGroupgoCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, ChatHandler.TeleportCommands/HandleRecallCommand, ChatHandler.TeleportCommands/HandleTeleGroupCommand, ChatHandler.TeleportCommands/HandleTeleNameCommand, CombatBotBaseAI/OnPacketReceived, ConfusedMovementGenerator/Update, FearMovementGenerator/_setTargetLocation, FleeingMovementGenerator/_setTargetLocation, MovementAnticheat/CheckSpeedHack, MovementAnticheat/CheckTeleport, MovementAnticheat/HandleFlagTests, PartyBotAI/UpdateAI, Spell.Main/CheckCast, Unit.Main/CheckPendingMovementChanges, WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes, WorldSession.MovementHandler/HandleMoveKnockBackAck, WorldSession.MovementHandler/HandleMovementFlagChangeToggleAck, WorldSession.MovementHandler/HandleMovementOpcodes, WorldSession.MovementHandler/HandleMoveNotActiveMoverOpcode, WorldSession.MovementHandler/HandleMoveRootAck, WorldSession.MovementHandler/HandleMoveSplineDoneOpcode | — |
| IsBeingTeleportedNear | method | — | PlayerBotAI/UpdateAI#2, Unit.Main/ResolvePendingMovementChange, WorldSession.MovementHandler/ExecuteTeleportNear, WorldSession.MovementHandler/HandleMoveTeleportAckOpcode | — |
| IsBeingTeleportedFar | method | — | Map.Main/Add#3, MapManager/CreateNewInstancesForPlayers, PlayerBotAI/UpdateAI#2, Unit.Main/CheckPendingMovementChanges, WorldSession.Main/LogoutPlayer, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| SetPendingFarTeleport | method | — | MapManager/CancelDelayedPlayerTeleport, MapManager/ExecuteSingleDelayedTeleport, MapManager/ScheduleFarTeleport | — |
| SetFallInformation | method | — | Unit.Main/DisableSpline, WaypointMovementGenerator/Finalize, WorldSession.MovementHandler/HandleMoveKnockBackAck | — |
| IsFalling | method | — | instance_temple_of_ahnqiraj/UpdateStomachOfCthun | — |
| IsControlledByOwnClient | method | — | Unit.Main/IsMovedByPlayer | — |
| SetMover | method | — | Spell.Main/SendChannelUpdate, Unit.SpellAuras/ModPossess, Unit.SpellAuras/ModPossessPet | — |
| GetMover | method | — | WorldSession.MovementHandler/GetMoverFromGuid, WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes, WorldSession.MovementHandler/HandleMovementFlagChangeToggleAck, WorldSession.MovementHandler/HandleMoveNotActiveMoverOpcode, WorldSession.MovementHandler/HandleMoveRootAck, WorldSession.MovementHandler/HandleMoveSplineDoneOpcode, WorldSession.MovementHandler/HandleMoveTeleportAckOpcode, WorldSession.MovementHandler/HandleSetActiveMoverOpcode, WorldSession.SpellHandler/HandleCancelAutoRepeatSpellOpcode, WorldSession.SpellHandler/HandleCancelCastOpcode, WorldSession.SpellHandler/HandleCancelChanneling | — |
| ToggleAFK | method | Object/ToggleFlag | BattleGroundMgr/SendToBattleGround, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| IsSelfMover | method | — | WorldSession.LFGHandler/HandleMeetingStoneJoinOpcode, WorldSession.SpellHandler/HandleCancelAuraOpcode, WorldSession.SpellHandler/HandleGameObjectUseOpcode, WorldSession.SpellHandler/HandleOpenItemOpcode, WorldSession.SpellHandler/HandlePetCancelAuraOpcode, WorldSession.SpellHandler/HandleUseItemOpcode | — |
| GetFarSightGuid | method | — | WorldSession.MiscHandler/HandleFarSightOpcode | — |
| GetRecallPosition | method | — | ChatHandler.TeleportCommands/HandleRecallCommand | — |
| ToggleDND | method | Object/HasFlag, Object/ToggleFlag | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| RelocateToHomebind | method | — | — | — |
| GetChatTag | method | — | MasterPlayer.Main/LoadPlayer, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| IsInVisibleList_Unsafe | method | — | WorldObject.Object/DestroyForNearbyPlayers, WorldObject.Object/Visit, WorldObject.Object/Visit#2, WorldObject.Object/Visit#3 | — |
| GetCamera | method | — | ChatHandler.MiscCommands/HandleSetViewCommand, GridNotifiers/Notify, Map.Main/PlayerRelocation, Map.Main/UpdateActiveObjectVisibility#2, Map.Main/UpdateActiveObjectVisibility#3, Spell.Effects/EffectAddFarsight, Spell.Main/SendChannelUpdate, Unit.SpellAuras/HandleAuraDummy, Unit.SpellAuras/HandleBindSight, Unit.SpellAuras/HandleInvisibilityDetect, Unit.SpellAuras/ModPossess, Unit.SpellAuras/ModPossessPet, WorldSession.MiscHandler/HandleFarSightOpcode, WorldSession.NPCHandler/SendSpiritResurrect | — |
| GetLongSight | method | — | Map.Main/PlayerRelocation | — |
| SwitchInstance | method | Errors/PrintStacktraceAndThrow, HostileRefManager/deleteReferences, Map.Main/Add#3, Map.Main/GetGameObject, Map.Main/GetId, Map.Main/Remove#3, MapManager/CreateMap, MovementInfo/ClearTransportData, MovementInfo/RemoveMovementFlag, Object/GetGuidValue, Object/IsInWorld, ObjectGuid/ObjectGuid, ObjectGuid/operator<<, SpellCaster/InterruptNonMeleeSpells, SpellCaster/IsNonMeleeSpellCasted, SpellCaster/RemoveAllDynObjects, Transport/RemovePassenger, Unit.Main/CombatStop, Unit.Main/DisableSpline, Unit.Main/GetHostileRefManager, Unit.Main/IsTaxiFlying, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/RemoveCharmAuras, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMap, WorldObject.Object/SetLocationInstanceId, WorldObject.Object/SetMap, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.MiscCommands/HandleInstanceSwitchCommand, MapManager/SwitchPlayersInstances | — |
| CanWalk | method | — | — | — |
| CanSwim | method | — | — | — |
| CanFly | method | — | — | — |
| SaveNoUndermapPosition | method | — | WorldSession.MovementHandler/HandleMoverRelocation | — |
| UndermapRecall | method | — | WorldSession.MovementHandler/HandleMoverRelocation | — |
| GetHomeBindMap | method | — | Map.Main/TeleportAllPlayersTo | — |
| GetHomeBindAreaId | method | — | — | — |
| SetSummonPoint | method | — | — | — |
| IsLaunched | method | — | instance_temple_of_ahnqiraj/UpdateStomachOfCthun, MovementAnticheat/CheckTeleport, WorldSession.MovementHandler/HandleMovementOpcodes | — |
| SetLaunched | method | — | boss_maexxna/DoCastWebWrap, boss_maexxna/OnEffectExecute, Unit.Main/KnockBackFrom, WorldSession.MovementHandler/HandleMovementOpcodes | — |
| TeleportTo | method | Log.Main/Out, Map.Main/CanEnter#3, Map.Main/GetGameObject, Map.Main/GetGridActivationDistance, MapEntry/IsBattleGround, MapManager/CanPlayerEnter, MapManager/FindMap, MapManager/GetContinentInstanceId, MapManager/IsValidMapCoord#4, MapManager/ScheduleFarTeleport, MapPersistentStateMgr/GetInstanceId, MovementInfo/ClearTransportData, MovementInfo/RemoveMovementFlag, MovementPacketSender/SendTeleportToController, Object/GetGuidValue, ScheduledTeleportData/ScheduledTeleportData, SpellCaster/InterruptSpellsWithChannelFlags, Transport/RemovePassenger, Unit.Main/CombatStop, Unit.Main/DisableSpline, Unit.Main/GetDeathState, Unit.Main/GetPet, WorldLocation/WorldLocation#2, WorldObject.Object/FindMap, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/IsWithinDist3d, WorldSession.LootHandler/DoLootRelease, WorldSession.Main/PlayerLogout | BattleGroundMgr/SendToBattleGround, ChatHandler.MiscCommands/HandleCinematicGoTimeCommand, ChatHandler.TeleportCommands/HandleGoHelper, ChatHandler.TeleportCommands/HandleGonameCommand, ChatHandler.TeleportCommands/HandleGroupgoCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, ChatHandler.TeleportCommands/HandleUnstuckCommand, custom_creatures/SendDefaultMenu_TeleportNPC, GMTicketMgr/TeleportTo, instance_blackwing_lair/AreaTrigger_at_orb_of_command, Map.ScriptCommands/ScriptCommand_TeleportTo, PartyBotAI/UpdateAI, ScriptedAI/DoTeleportAll, ScriptedAI/DoTeleportPlayer, Spell.Effects/EffectDummy, Spell.Effects/EffectStuck, Transport/TeleportTransport, Unit.Main/NearTeleportTo, Unit.SpellAuras/HandleAuraDummy, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.NPCHandler/SendSpiritResurrect | — |
| IsUnderwater | method | — | — | — |
| IsInWater | method | — | — | — |
| IsInMagma | method | — | — | — |
| IsInHighSea | method | — | — | — |
| IsInHighLiquid | method | — | Spell.Main/CheckCast, spell_item/OnCheckCast#7 | — |
| UpdateInnerTime | method | — | — | — |
| GetRestBonus | method | — | — | — |
| GetRestType | method | — | WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| GetTimeInnEnter | method | — | — | — |
| IsRested | method | — | — | — |
| GetRestTime | method | — | — | — |
| SetRestTime | method | — | — | — |
| GetTaxi | method | — | AiBotAI.Bridge/BridgeHandleTakeFlight, AiBotAI.Main/UpdateAI, ChatHandler.CharacterCommands/HandleCharacterFillFlysCommand, ChatHandler.CharacterCommands/HandleLearnAllMyTaxisCommand, ChatHandler.TeleportCommands/HandleGoHelper, ChatHandler.TeleportCommands/HandleGonameCommand, ChatHandler.TeleportCommands/HandleGroupgoCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, ChatHandler.TeleportCommands/HandleTeleGroupCommand, Creature.MotionMaster/MoveTaxiFlight, WaypointMovementGenerator/Finalize, WaypointMovementGenerator/Update | — |
| GetTaxi#2 | method | — | — | — |
| InitTaxiNodes | method | — | ChatHandler.CharacterCommands/HandleResetLevelCommand | — |
| GetCurrentCinematicEntry | method | — | Unit.Main/IsTargetableBy | — |
| GetLastSwingErrorMsg | method | — | Unit.Main/UpdateMeleeAttackingState | — |
| SetSwingErrorMsg | method | — | Unit.Main/UpdateMeleeAttackingState | — |
| SetCannotBeDetectedTimer | method | — | Spell.Effects/EffectSanctuary | — |
| CanBeDetected | method | — | — | — |
| AI | method | — | ChatHandler.CharacterCommands/HandleCharacterAIInfoCommand, ChatHandler.PlayerBotMgr/HandleBattleBotRemoveCommand, ChatHandler.PlayerBotMgr/HandleBattleBotShowPathCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAoECommand, ChatHandler.PlayerBotMgr/HandlePartyBotAttackStartCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAttackStopCommand, ChatHandler.PlayerBotMgr/HandlePartyBotClearMarksCommand, ChatHandler.PlayerBotMgr/HandlePartyBotComeToMeHelper, ChatHandler.PlayerBotMgr/HandlePartyBotControlMarkCommand, ChatHandler.PlayerBotMgr/HandlePartyBotFocusMarkCommand, ChatHandler.PlayerBotMgr/HandlePartyBotPauseApplyHelper, ChatHandler.PlayerBotMgr/HandlePartyBotPullCommand, ChatHandler.PlayerBotMgr/HandlePartyBotRemoveCommand, ChatHandler.PlayerBotMgr/HandlePartyBotSetRoleCommand, ChatHandler.PlayerBotMgr/HandlePartyBotToggleCastingCommand, ChatHandler.PlayerBotMgr/HandlePartyBotUnequipCommand, ChatHandler.PlayerBotMgr/HandlePartyBotUseGObjectHelper, PlayerBotAI/Remove, PointMovementGenerator/MovementInform#4 | — |
| SetAI | method | — | ChatHandler.PlayerBotMgr/OnPlayerInWorld, PlayerAI/Remove, PlayerBotAI/Remove | — |
| GetSession | method | — | AiBotAI.Bridge/BridgeHandleSayText, AiBotAI.Main/OnPacketReceived, AiBotAI.Main/UpdateAI, AsyncCommandHandlers/HandlePInfoCommand, AuctionHouseMgr/BuildListAuctionItems, AuctionHouseMgr/BuildListOwnerItems, AuctionHouseMgr/IsAvailableFor, AuctionHouseMgr/SendAuctionExpiredMail, AuctionHouseMgr/SendAuctionSuccessfulMail, AuctionHouseMgr/SendAuctionWonMail, BattleBotAI.BattleBotWaypoints/WSG_AtAllianceFlag, BattleBotAI.BattleBotWaypoints/WSG_AtHordeFlag, BattleBotAI.Main/OnPacketReceived, BattleGroundMgr/AddGroup, BattleGroundMgr/Execute, BattleGroundMgr/Execute#2, BattleGroundMgr/InviteGroupToBG, BattleGroundMgr/PlayerLoggedIn, BattleGroundMgr/RemoveOfflinePlayer, BattleGroundWS/HandleAreaTrigger, ChatHandler.AccountCommands/HandleAccountSetGmLevelCommand, ChatHandler.AccountCommands/HandleBanInfoCharacterCommand, ChatHandler.AccountCommands/HandleKickPlayerCommand, ChatHandler.AccountCommands/HandleMuteCommand, ChatHandler.AccountCommands/HandleSniffCommand, ChatHandler.AccountCommands/HandleSpamerMute, ChatHandler.AccountCommands/HandleSpamerUnmute, ChatHandler.AccountCommands/HandleUnmuteCommand, ChatHandler.CharacterCommands/HandleCharacterEraseCommand, ChatHandler.Chat/ChatHandler#2, ChatHandler.Chat/ExtractAccountId, ChatHandler.Chat/HasLowerSecurity, ChatHandler.DebugCommands/HandleDebugChatFreezeCommand, ChatHandler.DebugCommands/HandleDebugGetPrevPlayTimeCommand, ChatHandler.DebugCommands/HandleDebugPlayMusicCommand, ChatHandler.DebugCommands/HandleDebugSetPrevPlayTimeCommand, ChatHandler.DebugCommands/HandleDebugSpellModsCommand, ChatHandler.LookupCommands/HandleListClickToMoveCommand, ChatHandler.MiscCommands/HandleGMListIngameCommand, ChatHandler.MiscCommands/HandleSendMessageCommand, ChatHandler.PlayerBotMgr/OnPlayerInWorld, ChatHandler.TeleportCommands/HandleGroupgoCommand, ChatHandler.TeleportCommands/HandleTeleGroupCommand, ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand, ChatHandler.TicketCommands/HandleGMTicketEscalateCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand, ChatHandler.TicketCommands/ViewTicket, CombatBotBaseAI/OnPacketReceived, CombatBotBaseAI/SendAreaTriggerPacket, CombatBotBaseAI/SendBattlefieldPortPacket, CombatBotBaseAI/SendBattlemasterJoinPacket, custom_creatures/CompleteLearnProfession, custom_creatures/Enchant, custom_creatures/GossipSelect_EnchantNPC, custom_creatures/LearnAllRecipesInProfession, GameObject/Update, GameObject/Use, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/PlayerAddedToBGCheckIfBGIsRunning, game_Battlegrounds_BattleGround/PlaySoundToTeam, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, game_Battlegrounds_BattleGround/SendPacketToAll, game_Battlegrounds_BattleGround/SendPacketToTeam, game_Battlegrounds_BattleGround/SendRewardMarkByMail, game_Battlegrounds_BattleGround/UpdateWorldStateForPlayer, game_Chat_Channel/List, game_Chat_Channel/SetOwner, game_Group_Group/BroadcastPacket, game_Group_Group/BroadcastReadyCheck, game_Group_Group/CountSingleLooterRoll, game_Group_Group/CountTheRoll, game_Group_Group/Disband, game_Group_Group/GetGroupMemberStatus, game_Group_Group/MasterLoot, game_Group_Group/RemoveMember, game_Group_Group/SendLootAllPassed, game_Group_Group/SendLootRoll, game_Group_Group/SendLootRollWon, game_Group_Group/SendLootStartRoll, game_Group_Group/SendLootStartRollsForPlayer, game_Group_Group/SendUpdate, game_Group_Group/UpdatePlayerOutOfRange, game_Guild_Guild/AddMember, game_Guild_Guild/BroadcastPacket, game_Guild_Guild/BroadcastPacketToRank, game_Guild_Guild/Create#2, game_Mail_Mail/prepareTemplateItems, game_Objects_Item/AddToClientUpdateList, game_Objects_Item/SendTimeUpdate, go_scripts/GOHello_go_silithyste, GridNotifiers/Notify, GridNotifiers/Visit, GridNotifiers/Visit#2, GridNotifiers/Visit#3, GridNotifiers/Visit#4, GridNotifiers/Visit#5, GuildMgr/GetSignatureForPlayer, GuildMgr/PetitionSignature, HonorMgr/Add, LFGQueue/AddPlayer, LFGQueue/RemovePlayerFromQueue, LFGQueue/RestoreOfflinePlayer, LFGQueue/Update, Map.Main/Add#3, Map.Main/BindPlayerOrGroupOnEnter, Map.Main/CrashUnload, Map.Main/PermBindAllPlayers, Map.Main/ProcessSessionPackets, Map.Main/RemoveCorpses, Map.Main/SendDefenseMessage, Map.Main/SendInitSelf, Map.Main/SendInitTransports, Map.Main/SendObjectUpdates, Map.Main/SendRemoveTransports, Map.Main/SendToPlayers, Map.Main/SendToPlayersInZone, Map.Main/Update#3, Map.Main/UpdateActiveObjectVisibility, Map.Main/UpdatePlayers, MovementAnticheat/MovementAnticheat, MovementPacketSender/SendKnockBackToController, MovementPacketSender/SendMovementFlagChangeToController, MovementPacketSender/SendSpeedChangeToController, MovementPacketSender/SendTeleportToController, ObjectAccessor/KickPlayer, ObjectMgr/GetPlayerAccountIdByGUID, ObjectMgr/InsertPlayerInCache, ObjectMgr/UpdatePlayerCache, OutdoorPvPSI/HandleAreaTrigger, OutdoorPvPSI/HandleDropFlag, PartyBotAI/OnPacketReceived, Pet.Main/ModifyLoyalty, Pet.Main/SetEnabled, Pet.Main/_LoadSpellCooldowns, PlayerBotAI/UpdateAI#2, PointMovementGenerator/ComputePath, ReputationMgr/SendVisible, Spell.Effects/EffectApplyAura, Spell.Effects/EffectDuel, Spell.Effects/EffectDummy, Spell.Effects/EffectEnchantItemPerm, Spell.Effects/EffectEnchantItemTmp, Spell.Main/SendCastResult, Spell.Main/SendCastResult#2, Spell.Main/SendResurrectRequest, Spell.Main/ValidateExplicitTargetMask, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, TradeData/SetAccepted, TradeData/Update, Transport/SendCreateUpdateToMap, Unit.Main/AddSpellAuraHolder, Unit.Main/CheckPendingMovementChanges, Unit.Main/IsVisibleForOrDetect, Unit.Main/Kill, Unit.Main/ModConfuseSpell, Unit.Main/SendPetActionFeedback, Unit.Main/SendPetAIReaction, Unit.Main/SendPetCastFail, Unit.Main/SendPetTalk, Unit.Main/SetStandState, Unit.SpellAuras/HandleAuraModStun, Unit.SpellAuras/HandleManaShield, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/HandlePeriodicDamage, Unit.SpellAuras/HandlePeriodicHeal, Unit.SpellAuras/HandlePeriodicHealthFunnel, Unit.SpellAuras/HandlePeriodicLeech, Unit.SpellAuras/HandleSchoolAbsorb, Unit.SpellAuras/ModPossess, Weather/SendWeatherUpdateToPlayer, World/SendServerMessage, WorldObject.Object/DestroyForPlayer, WorldObject.Object/MonsterWhisper, WorldObject.Object/MonsterWhisper#2, WorldObject.Object/SendCreateUpdateToPlayer, WorldObject.Object/SendForcedObjectUpdate, WorldObject.Object/Visit, WorldObject.Object/Visit#2, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.AuctionHouseHandler/SendAuctionCancelledToBidderMail, WorldSession.AuctionHouseHandler/SendAuctionOutbiddedMail, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.CharacterHandler/HandleCharDeleteOpcode, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/HandleChatIgnoredOpcode, WorldSession.GroupHandler/HandleGroupDeclineOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GroupHandler/HandleRaidReadyCheckOpcode, WorldSession.GuildHandler/HandleGuildDeclineOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.LFGHandler/HandleMeetingStoneInfoOpcode, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMailRequest, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.MiscHandler/HandleWhoisOpcode, WorldSession.MiscHandler/HandleZoneUpdateOpcode, WorldSession.MiscHandler/operator(), WorldSession.MovementHandler/ExecuteTeleportNear, WorldSession.MovementHandler/HandleMoverRelocation, WorldSession.PetHandler/SendPetNameQuery, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionDeclineOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode, WorldSession.QuestHandler/HandleQuestPushResult, WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/HandleBeginTradeOpcode, WorldSession.TradeHandler/HandleInitiateTradeOpcode, WorldSession.TradeHandler/MoveItems, ZoneScript/BroadcastPacket, ZoneScript/OnPlayerLeave#2 | — |
| IsBot | method | — | ChatHandler.PlayerBotMgr/Update, game_Battlegrounds_BattleGround/RewardMark, Map.Main/HaveRealPlayers, Unit.Main/IsMovedByPlayer, WorldSession.MiscHandler/operator() | — |
| ExecuteTeleportFar | method | BattleGround/GetMapId, ByteBuffer/operator<<#10, HostileRefManager/deleteReferences, Map.Main/CanEnter#3, Map.Main/Remove#3, MapManager/CanPlayerEnter, MapManager/FindMap, MapManager/GetContinentInstanceId, MapManager/ScheduleNewWorldOnFarTeleport, MapPersistentStateMgr/GetInstanceId, MovementAnticheat/LogMovementPacket, Object/GetEntry, Object/IsInWorld, ObjectGuid/ObjectGuid, SpellCaster/InterruptNonMeleeSpells, SpellCaster/IsNonMeleeSpellCasted, SpellCaster/RemoveAllDynObjects, Unit.Main/CombatStop, Unit.Main/DisableSpline, Unit.Main/GetHostileRefManager, Unit.Main/GetPet, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/RemoveCharmAuras, Unit.Main/ResetExtraAttacks, Unit.Main/ResolvePendingMovementChanges, Unit.Main/SetSplineDonePending, WorldLocation/WorldLocation#2, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldPacket/WorldPacket#4, WorldSession.Main/PlayerLogout, WorldSession.Main/SendPacket | MapManager/ExecuteSingleDelayedTeleport, MapManager/ScheduleFarTeleport | — |
| GetTotalPlayedTime | method | — | AsyncCommandHandlers/HandlePInfoCommand, WorldSession.MiscHandler/HandlePlayedTime | — |
| GetLevelPlayedTime | method | — | WorldSession.MiscHandler/HandlePlayedTime | — |
| AddSkippedUpdateTime | method | — | Map.Main/UpdatePlayers | — |
| GetSkippedUpdateTime | method | — | Map.Main/UpdatePlayers | — |
| ResetSkippedUpdateTime | method | — | Map.Main/UpdatePlayers | — |
| ScheduleStandUp | method | — | Unit.Main/DealDamage | — |
| IsStandUpScheduled | method | — | SpellCaster/ProcDamageAndSpell_real | — |
| ClearScheduledStandUp | method | — | Unit.Main/SetStandState | — |
| GetSelectedGobj | method | — | ChatHandler.ObjectCommands/getSelectedGameObject | — |
| SetSelectedGobj | method | — | ChatHandler.ObjectCommands/HandleGameObjectSelectCommand | — |
| GetSelectionGuid | method | — | ChatHandler.Chat/ExecuteCommand, ChatHandler.Chat/GetSelectedCreature, ChatHandler.Chat/GetSelectedPet, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/GetSelectedUnit, ChatHandler.CreatureCommands/HandleNpcWhisperCommand, ChatHandler.CreatureCommands/HandleRespawnCommand, ChatHandler.DebugCommands/HandleDebugPlaySoundCommand, ChatHandler.TeleportCommands/HandleGoTargetCommand, ChatHandler.UnitCommands/HandleDamageCommand, ChatHandler.UnitCommands/HandleGUIDCommand, Spell.Effects/EffectTransmitted, Spell.Main/CheckCast, spell_warlock/OnCheckCast#4, Unit.SpellAuras/SingleEnemyTargetAura | — |
| SetSelectionGuid | method | — | WorldSession.MiscHandler/HandleInspectOpcode, WorldSession.MiscHandler/HandleSetSelectionOpcode | — |
| SetResurrectRequestData | method | — | Spell.Effects/EffectResurrect, Spell.Effects/EffectResurrectNew | — |
| ClearResurrectRequestData | method | — | WorldSession.MiscHandler/HandleResurrectResponseOpcode | — |
| IsRessurectRequestedBy | method | — | WorldSession.MiscHandler/HandleResurrectResponseOpcode | — |
| IsRessurectRequested | method | — | boss_mandokir/UpdateAI, boss_mandokir/UpdateAI#2, Spell.Effects/EffectResurrect, Spell.Effects/EffectResurrectNew | — |
| GetResurrector | method | — | CombatBotBaseAI/OnPacketReceived | — |
| RemoveDelayedOperation | method | — | Unit.Main/TeleportPositionRelocation, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| HasScheduledEvent | method | — | Map.Main/UpdatePlayers | — |
| SetEscortingGuid | method | — | ScriptedEscortAI/JustDied, ScriptedEscortAI/Start, ScriptedEscortAI/Stop, ScriptedEscortAI/UpdateAI | — |
| GetEscortingGuid | method | — | Creature.Main/operator()#3 | — |
| GetDrunkValue | method | — | ChatHandler.CharacterCommands/HandleModifyGenderCommand, Spell.Effects/EffectInebriate, Unit.Main/CanDetectInvisibilityOf | — |
| GetDeathTimer | method | — | Creature.Main/IsVisibleInGridForPlayer, WorldSession.Main/LogoutPlayer | — |
| SendNewWorld | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#9, MovementInfo/GetTransportPos, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | MapManager/CreateNewInstancesForPlayers, MapManager/ScheduleNewWorldOnFarTeleport | — |
| IsEnabledWhisperRestriction | method | — | ChatHandler.CharacterCommands/HandleWhisperRestrictionCommand, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| SetWhisperRestriction | method | — | ChatHandler.CharacterCommands/HandleWhisperRestrictionCommand | — |
| IsAcceptWhispers | method | — | ChatHandler.MiscCommands/HandleGMListIngameCommand | — |
| SetAcceptWhispers | method | — | ChatHandler.CharacterCommands/HandleWhispersCommand | — |
| GetExtraFlags | method | — | MasterPlayer.Main/LoadPlayer | — |
| HandleReturnOnTeleportFail | method | Log.Main/Out, Object/GetGuidStr, WorldObject.Object/ResetMap | MapManager/CreateNewInstancesForPlayers, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| IsAFK | method | — | BattleGroundMgr/SendToBattleGround, game_Group_Group/GetGroupMemberStatus, game_Guild_Guild/GetGuildRosterFlagsForPlayer, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| IsDND | method | — | game_Group_Group/GetGroupMemberStatus, game_Guild_Guild/GetGuildRosterFlagsForPlayer, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| GetName | method | — | AiBotAI.Bridge/BridgeConnect, AiBotAI.Bridge/BridgeFlush, AiBotAI.Bridge/BridgeHandleAbandonQuest, AiBotAI.Bridge/BridgeHandleAttackTarget, AiBotAI.Bridge/BridgeHandleCombatDirective, AiBotAI.Bridge/BridgeHandleDisbandGroup, AiBotAI.Bridge/BridgeHandleFormGroup, AiBotAI.Bridge/BridgeHandleInteractNpc, AiBotAI.Bridge/BridgeHandleLearnSpell, AiBotAI.Bridge/BridgeHandleMoveTo, AiBotAI.Bridge/BridgeHandleQueryQuestStatus, AiBotAI.Bridge/BridgeHandleQuestInteract, AiBotAI.Bridge/BridgeHandleRepairItems, AiBotAI.Bridge/BridgeHandleResurrect, AiBotAI.Bridge/BridgeHandleSayText, AiBotAI.Bridge/BridgeHandleSellItems, AiBotAI.Bridge/BridgeHandleSetTask, AiBotAI.Bridge/BridgeHandleTakeFlight, AiBotAI.Bridge/BridgeHandleTeleport, AiBotAI.Bridge/BridgeHandleTrain, AiBotAI.Bridge/BridgeHandleUseGameObject, AiBotAI.Bridge/BridgeProcessLine, AiBotAI.Bridge/BridgeRecv, AiBotAI.Bridge/BridgeSend, AiBotAI.Bridge/BridgeSendHello, AiBotAI.Combat/HandleCombatStalemate, AiBotAI.Combat/HandleOverpullRetreat, AiBotAI.Grind/ConvertMoveToGrindInPlace, AiBotAI.Grind/SelectGrindTarget, AiBotAI.Loot/ChooseQuestReward, AiBotAI.Loot/DoAutoLoot, AiBotAI.Loot/TryAutoEquip, AiBotAI.Loot/TryAutoEquipBags, AiBotAI.Main/MovementInform, AiBotAI.Main/OnPacketReceived, AiBotAI.Main/OnPlayerLogin, AiBotAI.Main/OnSessionLoaded, AiBotAI.Main/RefreshDoctrine, AiBotAI.Main/UpdateAI, AiBotAI.Movement/FindNearestNavmeshPointNear, AiBotAI.Movement/IsPathSafe, AiBotAI.Movement/MoveToDestination, AiBotAI.Movement/RecordNavBoundary, AiBotAI.Movement/ReGroundZ, AiBotAI.Movement/SmoothPathCorners, AiBotAI.Movement/StartNextPathChunk, AiBotDoctrineTeam/AcquireTarget, AiBotDoctrineTeam/ResolveFocus, AsyncCommandHandlers/ShowAccountListHelper, AuctionHouseMgr/SendAuctionWonMail, BattleGroundAV/HandleQuestComplete, BattleGroundAV/UpgradeArmor, BattleGroundMgr/AddGroup, BattleGroundMgr/SendToBattleGround, ChatHandler.AccountCommands/HandleAnticheatCommand, ChatHandler.AccountCommands/HandleSniffCommand, ChatHandler.CharacterCommands/HandleCharacterFillFlysCommand, ChatHandler.CharacterCommands/HandleCharacterPremadeGearCommand, ChatHandler.CharacterCommands/HandleCharacterPremadeSpecCommand, ChatHandler.CharacterCommands/HandleCheatStatusCommand, ChatHandler.CharacterCommands/HandleGroupInfoCommand, ChatHandler.CharacterCommands/HandleHonorSetRPCommand, ChatHandler.CharacterCommands/HandleHonorShow, ChatHandler.CharacterCommands/HandleLearnCommand, ChatHandler.CharacterCommands/HandleListExploredAreasCommand, ChatHandler.CharacterCommands/HandleListVisibleGuidsCommand, ChatHandler.CharacterCommands/HandleModifyBlockCommand, ChatHandler.CharacterCommands/HandleModifyDodgeCommand, ChatHandler.CharacterCommands/HandleModifyGenderCommand, ChatHandler.CharacterCommands/HandleModifyHonorCommand, ChatHandler.CharacterCommands/HandleModifyMeleeCritCommand, ChatHandler.CharacterCommands/HandleModifyParryCommand, ChatHandler.CharacterCommands/HandleModifyRangedCritCommand, ChatHandler.CharacterCommands/HandleModifySpellCritCommand, ChatHandler.CharacterCommands/HandleModifyXpRateCommand, ChatHandler.CharacterCommands/HandleQuestAddCommand, ChatHandler.CharacterCommands/HandleQuestCompleteCommand, ChatHandler.CharacterCommands/HandleReviveCommand, ChatHandler.CharacterCommands/HandleUnLearnAllRecipesCommand, ChatHandler.Chat/ExecuteCommand, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.DebugCommands/HandleDebugLoSAllowCommand, ChatHandler.DebugCommands/HandleDebugMonsterChatCommand, ChatHandler.DebugCommands/HandleMmapPathCommand, ChatHandler.MiscCommands/HandleInstanceContinentsCommand, ChatHandler.PlayerBotMgr/HandlePartyBotClearMarksCommand, ChatHandler.PlayerBotMgr/HandlePartyBotComeToMeCommand, ChatHandler.PlayerBotMgr/HandlePartyBotControlMarkCommand, ChatHandler.PlayerBotMgr/HandlePartyBotFocusMarkCommand, ChatHandler.PlayerBotMgr/HandlePartyBotPauseHelper, ChatHandler.PlayerBotMgr/HandlePartyBotSetRoleCommand, ChatHandler.PlayerBotMgr/HandlePartyBotToggleCastingCommand, ChatHandler.PlayerBotMgr/HandlePartyBotUseGObjectCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, ChatHandler.TeleportCommands/HandleUnstuckCommand, ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketCommentCommand, ChatHandler.TicketCommands/HandleGMTicketCompleteCommand, ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand, ChatHandler.TicketCommands/HandleGMTicketUnAssignCommand, ChatHandler.UnitCommands/HandleModifyASpeedCommand, Corpse/Create#2, Creature.Main/LogDeath, duskwood/Handle_NightmareCorruption, duskwood/UpdateAI#3, game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup, game_Battlegrounds_BattleGround/AddPlayer, game_Battlegrounds_BattleGround/operator()#3, game_Battlegrounds_BattleGround/operator()#4, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, game_Group_Group/AddLeaderInvite, game_Group_Group/GetInvited#2, game_Guild_Guild/AddMember, game_Guild_Guild/SetMemberStats, GMTicketMgr/GmTicket#2, go_scripts/GOHello_go_silithyste, LFGMgr/AddToQueue, LFGQueue/FindRoleToGroup, LFGQueue/Update, Map.Main/Add#2, Map.Main/BindPlayerOrGroupOnEnter, Map.Main/CanEnter#2, Map.Main/DoPlayerGridRelocation, Map.Main/EnsureGridLoadedAtEnter, Map.Main/PlayerRelocation, Map.Main/Remove, Map.Main/Remove#2, Map.Main/Remove#3, MapManager/CanPlayerEnter, MasterPlayer.Main/LoadPlayer, ObjectAccessor/AddObject#3, ObjectAccessor/RemoveObject#3, ObjectMgr/InsertPlayerInCache, ObjectMgr/UpdatePlayerCache, OutdoorPvPSI/HandleAreaTrigger, OutdoorPvPSI/HandleDropFlag, PartyBotAI/AddToPlayerGroup, PartyBotAI/UpdateAI, ReputationMgr/GetReputation#2, ScriptedFollowerAI/StartFollow, Spell.Effects/EffectEnchantItemPerm, Spell.Effects/EffectEnchantItemTmp, Spell.Effects/EffectStuck, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/HandleChatIgnoredOpcode, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.ChatHandler/SanitizeChatMessage, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.GMTicketHandler/HandleGMTicketDeleteTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketUpdateTextOpcode, WorldSession.GroupHandler/HandleGroupAcceptOpcode, WorldSession.GroupHandler/HandleGroupDeclineOpcode, WorldSession.GroupHandler/HandleGroupDisbandOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GroupHandler/HandleGroupUninviteOpcode, WorldSession.GuildHandler/HandleGuildAcceptOpcode, WorldSession.GuildHandler/HandleGuildDeclineOpcode, WorldSession.GuildHandler/HandleGuildDemoteOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.GuildHandler/HandleGuildLeaderOpcode, WorldSession.GuildHandler/HandleGuildLeaveOpcode, WorldSession.GuildHandler/HandleGuildPromoteOpcode, WorldSession.GuildHandler/HandleGuildRemoveOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.Main/GetPlayerName, WorldSession.Main/LogoutPlayer, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.MiscHandler/HandleBugOpcode, WorldSession.MiscHandler/HandleMoveSetRawPosition, WorldSession.MiscHandler/HandleRepopRequestOpcode, WorldSession.MiscHandler/HandleWorldTeleportOpcode, WorldSession.MiscHandler/operator(), WorldSession.MovementHandler/HandleMoverRelocation, WorldSession.PetHandler/HandlePetSetAction, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode, WorldSession.QueryHandler/SendNameQueryOpcode, WorldSession.QuestHandler/HandleQuestgiverChooseRewardOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/MoveItems, ZoneScript/OnPlayerLeave#2 | — |
| SetName | method | — | AiBotAI.Main/OnSessionLoaded | — |
| LearnLanguage | method | — | Spell.Effects/EffectLanguage | — |
| RestorePendingTeleport | method | — | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| RemoveLanguage | method | — | — | — |
| KnowsLanguage | method | — | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| TeleportToBGEntryPoint | method | — | game_Battlegrounds_BattleGround/RemovePlayerAtLeave, Map.Main/TeleportAllPlayersTo | — |
| GetTeam | method | — | AiBotAI.Bridge/BridgeHandleResurrect, AiBotAI.Bridge/BridgeHandleSayText, AuctionHouseMgr/GetAuctionHouseEntry, AuraRemovalMgr/PlayerEnterMap, BattleBotAI.BattleBotWaypoints/StartNewPathToObjective, BattleBotAI.BattleBotWaypoints/WSG_AtAllianceFlag, BattleBotAI.BattleBotWaypoints/WSG_AtAllianceGraveyard, BattleBotAI.BattleBotWaypoints/WSG_AtHordeFlag, BattleBotAI.BattleBotWaypoints/WSG_AtHordeGraveyard, BattleBotAI.Main/DoGraveyardJump, BattleBotAI.Main/OnEnterBattleGround, BattleBotAI.Main/SelectAttackTarget, BattleBotAI.Main/SelectFollowTarget, BattleBotAI.Main/UpdateBattleGroundAI, BattleGroundAB/EventPlayerClickedOnFlag, BattleGroundAB/GetClosestGraveYard, BattleGroundAB/HandleAreaTrigger, BattleGroundAV/EventPlayerAssaultsPoint, BattleGroundAV/EventPlayerDefendsPoint, BattleGroundAV/GetClosestGraveYard, BattleGroundAV/HandleAreaTrigger, BattleGroundAV/HandleKillPlayer, BattleGroundAV/HandleKillUnit, BattleGroundAV/HandleQuestComplete, BattleGroundAV/UpgradeArmor, BattleGroundMgr/AddGroup, BattleGroundMgr/SendToBattleGround, BattleGroundWS/EventPlayerCapturedFlag, BattleGroundWS/EventPlayerClickedOnFlag, BattleGroundWS/EventPlayerDroppedFlag, BattleGroundWS/GetClosestGraveYard, BattleGroundWS/HandleAreaTrigger, ChatHandler.CharacterCommands/HandleCharacterFillFlysCommand, ChatHandler.CharacterCommands/HandleHonorShow, ChatHandler.CharacterCommands/HandleLearnAllMyTaxisCommand, ChatHandler.DebugCommands/HandleDebugLootTableCommand, ChatHandler.MiscCommands/HandleAuctionAllianceCommand, ChatHandler.MiscCommands/HandleAuctionHordeCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAddCommand, ChatHandler.PlayerBotMgr/PartyBotAddRequirementCheck, ChatHandler.TeleportCommands/HandleUnstuckCommand, Conditions/Evaluate, Creature.Main/GenerateLootForBody, Creature.Main/GeneratePlayerDependentLoot, Creature.Main/SendZoneUnderAttackMessage, custom_creatures/GossipHello_TeleportNPC, custom_creatures/SendDefaultMenu_TeleportNPC, eastern_plaguelands/Reset, game_Battlegrounds_BattleGround/CastSpellOnTeam, game_Battlegrounds_BattleGround/GetClosestGraveYard, game_Battlegrounds_BattleGround/HandleKillPlayer, game_Battlegrounds_BattleGround/PlaySoundToTeam, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, game_Battlegrounds_BattleGround/ReturnPlayersToHomeGY, game_Battlegrounds_BattleGround/RewardHonorToTeam, game_Battlegrounds_BattleGround/RewardReputationToTeam, game_Battlegrounds_BattleGround/SendPacketToTeam, game_Chat_Channel/SetOwner, game_Group_Group/AddMember, game_Group_Group/CanJoinBattleGroundQueue, game_Group_Group/RewardGroupAtKill_helper, GridNotifiers/Visit#3, GuardMgr/GetTeam, GuildMgr/CreatePetition, instance_blackrock_depths/ReplacePrincessIfPossible, LFGMgr/AddToQueue, Map.Main/SendToPlayers, MasterPlayer.Main/LoadPlayer, MovementAnticheat/CheckForbiddenArea, npcs_special/SpellHit#3, ObjectMgr/ApplyPremadeGearTemplateToPlayer, OutdoorPvPEP/OnPlayerEnter, OutdoorPvPEP/OnPlayerLeave, OutdoorPvPSI/HandleAreaTrigger, OutdoorPvPSI/HandleDropFlag, OutdoorPvPSI/OnPlayerEnter, PlayerBotAI/BeforeAddToMap#2, Spell.Effects/EffectSummonObjectWild, spell_special/OnAfterApply#4, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/QuestComplete_AV_npc_troops_chief, ThreatListCopier.battleground_alterac/QuestComplete_npc_AVBlood_collector, Unit.SpellAuras/HandleAuraDummy, World/SendGlobalMessage, World/SendZoneMessage, WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode, WorldSession.ChannelHandler/HandleJoinChannelOpcode, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GuildHandler/HandleGuildAcceptOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.MiscHandler/operator(), WorldSession.NPCHandler/SendSpiritResurrect, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode, WorldSession.TaxiHandler/SendLearnNewTaxiNode, WorldSession.TaxiHandler/SendTaxiMenu, WorldSession.TaxiHandler/SendTaxiStatus, WorldSession.TradeHandler/HandleInitiateTradeOpcode | — |
| ProcessDelayedOperations | method | SpellCaster/CastSpell#2, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/SetHealth, Unit.Main/SetPower | WorldSession.MovementHandler/ExecuteTeleportNear, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| GetTeamId | method | — | ChatHandler.TeleportCommands/HandleUnstuckCommand, game_Battlegrounds_BattleGround/RewardMark, Unit.SpellAuras/HandleAuraDummy, world_event_wareffort/GossipHello_npc_AQwar_collector, world_event_wareffort/QuestComplete_npc_AQwar_collector, ZoneScript/HandlePlayerEnter, ZoneScript/HandlePlayerLeave, ZoneScript/HasPlayer, ZoneScript/IsInsideObjective, ZoneScript/OnPlayerEnter#2, ZoneScript/OnPlayerLeave#2, ZoneScript/Update | — |
| GetReputationMgr | method | — | ChatHandler.CharacterCommands/HandleCharacterReputationCommand, ChatHandler.CharacterCommands/HandleModifyRepCommand, ChatHandler.LookupCommands/HandleLookupFactionCommand, ChatHandler.LookupCommands/ShowFactionListHelper, Creature.Main/OnEnterCombat, GameObject/IsFriendlyTo, GameObject/IsHostileTo, game_Battlegrounds_BattleGround/RewardReputationToTeam, game_Group_Group/RewardGroupAtKill_helper, instance_naxxramas.Main/SetData, instance_ruins_of_ahnqiraj/GiveRepAfterRajaxxDeath, Spell.Effects/EffectReputation, spell_item/OnAfterApply, Unit.SpellAuras/HandleForceReaction, WorldSession.CharacterHandler/HandleSetFactionAtWarOpcode, WorldSession.CharacterHandler/HandleSetFactionInactiveOpcode, WorldSession.MiscHandler/HandleSetSelectionOpcode | — |
| GetReputationMgr#2 | method | — | Conditions/Evaluate, GameObject/IsFriendlyTo, GameObject/IsHostileTo, WorldObject.Object/GetFactionReactionTo, WorldObject.Object/GetReactionTo, WorldObject.Object/IsValidAttackTarget | — |
| SetTemporaryAtWarWithFaction | method | — | Creature.Main/OnEnterCombat | — |
| IsPvPDesired | method | — | — | — |
| IsFFAPvP | method | — | GameObject/operator()#3, game_Group_Group/GetGroupMemberStatus, PartyBotAI/ShouldEnterStealth, Unit.Main/CanAttackWithoutEnablingPvP, Unit.Main/SetInCombatWithAggressor, Unit.Main/TogglePlayerPvPFlagOnAttackVictim, WorldObject.Object/GetReactionTo, WorldObject.Object/IsValidAttackTarget, WorldObject.Object/IsValidHelpfulTarget | — |
| AddToWorld | method | ChatHandler.PlayerBotMgr/OnPlayerInWorld, Object/AddToWorld, Unit.Main/AddToWorld | Map.Main/Add#3 | — |
| IsInDuelWith | method | — | GameObject/operator()#3, Unit.Main/CanAttackWithoutEnablingPvP, Unit.Main/SetInCombatWithAggressor, Unit.Main/TogglePlayerPvPFlagOnAttackVictim | — |
| RemoveFromWorld | method | Camera/ResetView, game_Objects_Item/RemoveFromWorld, Object/IsInWorld, ObjectGuid/ObjectGuid, Unit.Main/RemoveFromWorld, Unit.Main/UnsummonAllTotems, WorldSession.LootHandler/DoLootRelease, WorldSession.Main/PlayerLogout, ZoneScriptMgr/HandlePlayerLeaveZone | Map.Main/Remove#3 | — |
| GetHonorMgr | method | — | BattleGroundMgr/BuildPvpLogDataPacket, ChatHandler.CharacterCommands/HandleHonorAddCommand, ChatHandler.CharacterCommands/HandleHonorResetCommand, ChatHandler.CharacterCommands/HandleHonorSetRPCommand, ChatHandler.CharacterCommands/HandleHonorShow, ChatHandler.CharacterCommands/HandleResetHonorCommand, game_Battlegrounds_BattleGround/UpdatePlayerScore, game_Chat_Channel/Say, HonorMgr/HonorableKillPoints, PartyBotAI/CloneFromPlayer, Spell.Effects/EffectAddHonor, WorldSession.MiscHandler/HandleInspectHonorStatsOpcode | — |
| GetHonorMgr#2 | method | — | Conditions/Evaluate, HonorMgr/SendPVPCredit, PartyBotAI/CloneFromPlayer | — |
| InBattleGround | method | — | BattleBotAI.Main/UpdateAI, ChatHandler.MiscCommands/HandleInstanceGroupUnbindCommand, ChatHandler.MiscCommands/RegisterPlayerToBG, ChatHandler.PlayerBotMgr/PartyBotAddRequirementCheck, ChatHandler.TeleportCommands/HandleUnstuckCommand, CombatBotBaseAI/OnPacketReceived, GameObject/Update, game_Group_Group/CanJoinBattleGroundQueue, PartyBotAI/GetPartyLeader, PartyBotAI/ShouldEnterStealth, PartyBotAI/UpdateAI, Spell.Main/CheckCast, SpellMgr/GetSpellAllowedInLocationError, spell_warlock/OnCheckCast#4, Unit.Main/Kill, Unit.Main/UpdateSpeed, Unit.SpellAuras/HandleAuraDummy, World/SendWorldTextToBGAndQueue, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.GroupHandler/HandleGroupDisbandOpcode, WorldSession.GroupHandler/HandleGroupRaidConvertOpcode, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.MiscHandler/HandleReclaimCorpseOpcode, WorldSession.MovementHandler/HandleMoverRelocation, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| GetBattleGroundId | method | — | ChatHandler.TeleportCommands/HandleGonameCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, Map.Main/CanEnter, MapManager/CreateInstance, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| GetBattleGroundTypeId | method | — | ChatHandler.TeleportCommands/HandleGonameCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, World/SendWorldTextToBGAndQueue, WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode | — |
| RewardRage | method | Unit.Main/GetLevel, Unit.Main/HasAura, Unit.Main/ModifyPower, World/getConfig#2 | Unit.Main/DealDamage | — |
| InBattleGroundQueue | method | — | BattleBotAI.Main/UpdateAI, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode | — |
| GetQueuedBattleground | method | — | — | — |
| GetBattleGroundQueueTypeId | method | — | BattleGroundMgr/PlayerLoggedOut, WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode | — |
| GetBattleGroundQueueIndex | method | — | BattleGroundMgr/Execute, BattleGroundMgr/Execute#2, BattleGroundMgr/InviteGroupToBG, BattleGroundMgr/RemoveOfflinePlayer, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/PlayerAddedToBGCheckIfBGIsRunning, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| HandleFoodEmotes | method | Aura/GetSpellProto, SpellEntry/HasAura, SpellEntry/HasAuraInterruptFlag, Unit.Main/GetAurasByType, Unit.Main/SendPlaySpellVisualKit | — | — |
| IsInvitedForBattleGroundQueueType | method | — | CombatBotBaseAI/OnPacketReceived, CombatBotBaseAI/SendBattlefieldPortPacket, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode | — |
| InBattleGroundQueueForBattleGroundQueueType | method | — | game_Group_Group/CanJoinBattleGroundQueue, World/SendWorldTextToBGAndQueue | — |
| SetBattleGroundId | method | — | ChatHandler.TeleportCommands/HandleGonameCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| AddBattleGroundQueueId | method | — | BattleGroundMgr/PlayerLoggedIn, WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| RemoveBattleGroundQueueId | method | — | BattleGroundMgr/Execute, BattleGroundMgr/PlayerLoggedOut, BattleGroundMgr/RemoveOfflinePlayer, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode | — |
| RegenerateAll | method | Unit.Main/HasAuraType, Unit.Main/IsInCombat, Unit.Main/IsPolymorphed | — | — |
| SetInviteForBattleGroundQueueType | method | — | BattleGroundMgr/InviteGroupToBG, BattleGroundMgr/PlayerLoggedIn | — |
| IsInvitedForBattleGroundInstance | method | — | WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| Regenerate | method | Aura/GetModifier, Unit.Main/GetAurasByType, Unit.Main/GetMaxPower, Unit.Main/GetPower, Unit.Main/IsUnderLastManaUseEffect, Unit.Main/SetPower, World/getConfig#2 | — | — |
| GetBattleGroundEntryPoint | method | — | WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| SetBGTeam | method | — | game_Battlegrounds_BattleGround/RemovePlayerAtLeave, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| GetBGTeam | method | — | BattleGroundMgr/SendToBattleGround, game_Battlegrounds_BattleGround/AddPlayer | — |
| GetCheatData | method | — | ChatHandler.AccountCommands/HandleAnticheatCommand, Creature.Main/Update, MovementPacketSender/SendKnockBackToController, MovementPacketSender/SendMovementFlagChangeToController, MovementPacketSender/SendSpeedChangeToController, MovementPacketSender/SendTeleportToController, MoveSplineInit/Launch, TargetedMovementGenerator/Update, TargetedMovementGenerator/_setTargetLocation, Unit.Main/CheckPendingMovementChanges, Unit.Main/KnockBack, WorldObject.Object/SendMovementMessageToSet, WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes, WorldSession.MovementHandler/HandleMoveKnockBackAck, WorldSession.MovementHandler/HandleMovementFlagChangeToggleAck, WorldSession.MovementHandler/HandleMovementOpcodes, WorldSession.MovementHandler/HandleMoveNotActiveMoverOpcode, WorldSession.MovementHandler/HandleMoveRootAck, WorldSession.MovementHandler/HandleMoveSplineDoneOpcode | — |
| GetPacketBroadcaster | method | — | MovementBroadcaster/UpdateConfiguration | — |
| RegenerateHealth | method | Aura/GetModifier, Unit.Main/GetAurasByType, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/GetRegenHPPerSpirit, Unit.Main/GetTotalAuraModifier, Unit.Main/HasAuraType, Unit.Main/IsInCombat, Unit.Main/IsPolymorphed, Unit.Main/IsStandingUp, Unit.Main/ModifyHealth, World/getConfig#2 | — | — |
| GetBoundInstances | method | — | ChatHandler.MiscCommands/HandleInstanceListBindsCommand, ChatHandler.MiscCommands/HandleInstanceUnbindHelper | — |
| SetAutoInstanceSwitch | method | — | ChatHandler.MiscCommands/HandleInstanceSwitchCommand, PlayerBotAI/SpawnNewPlayer | — |
| GetSmartInstanceBindingMode | method | — | ChatHandler.TeleportCommands/HandleGonameCommand | — |
| SetSmartInstanceBindingMode | method | — | ChatHandler.MiscCommands/HandleInstanceBindingMode | — |
| GetGroupInvite | method | — | game_Group_Group/AddInvite, game_Group_Group/RemoveAllInvites, game_Group_Group/RemoveInvite, game_Group_Group/_addMember#2, WorldSession.GroupHandler/HandleGroupAcceptOpcode, WorldSession.GroupHandler/HandleGroupDeclineOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode | — |
| SetGroupInvite | method | — | game_Group_Group/AddInvite, game_Group_Group/RemoveAllInvites, game_Group_Group/RemoveInvite, game_Group_Group/_addMember#2 | — |
| GetGroup | method | — | AccountMgr/GetWhisperScore, AiBotAI.Bridge/BridgeHandleDisbandGroup, AiBotAI.Bridge/BridgeHandleFormGroup, AiBotAI.Combat/HandleOverpullRetreat, AiBotAI.Combat/OverpullGuard, AiBotAI.Combat/SelectAttackTarget, AiBotAI.Loot/DoAutoLoot, AiBotAI.Main/OnPacketReceived, AiBotAI.Main/UpdateAI, AiBotDoctrine/ResolveDoctrine, AiBotDoctrineTeam/ResolveFocus, BattleBotAI.Main/SelectAttackTarget, ChatHandler.CharacterCommands/HandleGroupAddItemCommand, ChatHandler.CharacterCommands/HandleGroupInfoCommand, ChatHandler.CharacterCommands/HandleGroupReplenishCommand, ChatHandler.CharacterCommands/HandleGroupReviveCommand, ChatHandler.CharacterCommands/HandleGroupSummonCommand, ChatHandler.Chat/ExecuteCommand, ChatHandler.MiscCommands/HandleInstanceGroupUnbindCommand, ChatHandler.MiscCommands/HandleInstanceListBindsCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAoECommand, ChatHandler.PlayerBotMgr/HandlePartyBotAttackStartCommand, ChatHandler.PlayerBotMgr/HandlePartyBotAttackStopCommand, ChatHandler.PlayerBotMgr/HandlePartyBotClearMarksCommand, ChatHandler.PlayerBotMgr/HandlePartyBotComeToMeCommand, ChatHandler.PlayerBotMgr/HandlePartyBotControlMarkCommand, ChatHandler.PlayerBotMgr/HandlePartyBotFocusMarkCommand, ChatHandler.PlayerBotMgr/HandlePartyBotPauseHelper, ChatHandler.PlayerBotMgr/HandlePartyBotPullCommand, ChatHandler.PlayerBotMgr/HandlePartyBotToggleCastingCommand, ChatHandler.PlayerBotMgr/HandlePartyBotUseGObjectCommand, ChatHandler.TeleportCommands/HandleGonameCommand, ChatHandler.TeleportCommands/HandleGroupgoCommand, ChatHandler.TeleportCommands/HandleTeleGroupCommand, CombatBotBaseAI/AreOthersOnSameTarget, CombatBotBaseAI/FindAndPreHealTarget, CombatBotBaseAI/SelectBuffTarget, CombatBotBaseAI/SelectBuffTarget#2, CombatBotBaseAI/SelectDispelTarget, CombatBotBaseAI/SelectHealTarget, CombatBotBaseAI/SelectPeriodicHealTarget, Creature.Main/GetLootRecipient, Creature.Main/SetLootRecipient, CreatureAI/ClearTargetIcon, GameObject/Use, game_Group_Group/AddInvite, game_Group_Group/BroadcastPacket, game_Group_Group/ChangeMembersGroup#2, game_Group_Group/Disband, game_Group_Group/RemoveMember, game_Group_Group/SendUpdate, game_Group_Group/SwapMembersGroup, game_Group_Group/SwapMembersGroup#2, game_Group_Group/_addMember#2, game_Group_Group/_chooseLeader, instance_ruins_of_ahnqiraj/GiveRepAfterRajaxxDeath, LFGMgr/AddToQueue, LootMgr/FillPlayerDependentLoot, Map.Main/BindPlayerOrGroupOnEnter, Map.Main/CanEnter#2, Map.Main/PermBindAllPlayers, Map.ScriptCommands/ScriptCommand_QuestExplored, Map.ScriptCommands/ScriptCommand_StartScriptOnGroup, MapManager/CanPlayerEnter, PartyBotAI/AddToPlayerGroup, PartyBotAI/GetDistancingTarget, PartyBotAI/GetMarkedTarget, PartyBotAI/GetPartyLeader, PartyBotAI/SelectAttackTarget, PartyBotAI/SelectPartyAttackTarget, PartyBotAI/SelectResurrectionTarget, PartyBotAI/SelectShieldTarget, PartyBotAI/ShouldAutoRevive, Pet.Main/LoadPetFromDB, Pet.Main/Unsummon, PetAI/UpdateAllies, quest_stormwind_rendezvous/CompleteQuest, ScriptedEscortAI/IsPlayerOrGroupInRange, ScriptedFollowerAI/GetLeaderForFollower, ScriptedFollowerAI/JustDied, ScriptedFollowerAI/UpdateAI, searing_gorge/QuestAccept_npc_dying_archaeologist, Spell.Effects/EffectScriptEffect, Spell.Effects/EffectTransmitted, Spell.Main/FillRaidOrPartyTargets, Spell.Main/SetTargetMap, Totem/UnSummon, Unit.Main/ApplyMaxPowerMod, Unit.Main/ApplyPowerMod, Unit.Main/DealDamage, Unit.Main/Kill, Unit.Main/SetDisplayId, Unit.Main/SetHealth, Unit.Main/SetLevel, Unit.Main/SetMaxHealth, Unit.Main/SetMaxPower, Unit.Main/SetPower, Unit.Main/SetPowerType, Unit.Main/SetPvP, Unit.Main/UpdateAuraForGroup, Unit.SpellAuras/HandleAuraGhost, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/Update, Unit.SpellAuras/Update#2, WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.GroupHandler/HandleGroupAssistantLeaderOpcode, WorldSession.GroupHandler/HandleGroupChangeSubGroupOpcode, WorldSession.GroupHandler/HandleGroupDisbandOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GroupHandler/HandleGroupRaidConvertOpcode, WorldSession.GroupHandler/HandleGroupSetLeaderOpcode, WorldSession.GroupHandler/HandleGroupSwapSubGroupOpcode, WorldSession.GroupHandler/HandleGroupUninviteGuidOpcode, WorldSession.GroupHandler/HandleGroupUninviteOpcode, WorldSession.GroupHandler/HandleLootMethodOpcode, WorldSession.GroupHandler/HandleLootRoll, WorldSession.GroupHandler/HandleMinimapPingOpcode, WorldSession.GroupHandler/HandleRaidReadyCheckOpcode, WorldSession.GroupHandler/HandleRaidTargetUpdateOpcode, WorldSession.GroupHandler/HandleRandomRollOpcode, WorldSession.LFGHandler/HandleMeetingStoneInfoOpcode, WorldSession.LFGHandler/HandleMeetingStoneJoinOpcode, WorldSession.LFGHandler/HandleMeetingStoneLeaveOpcode, WorldSession.LootHandler/DoLootRelease, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.LootHandler/HandleLootMoneyOpcode, WorldSession.Main/LogoutPlayer, WorldSession.MiscHandler/HandleResetInstancesOpcode, WorldSession.PetHandler/HandlePetRename, WorldSession.QuestHandler/HandlePushQuestToParty, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, ZoneScript/HandleKill | — |
| GetGroup#2 | method | — | ChatHandler.PlayerBotMgr/PartyBotAddRequirementCheck, Conditions/Evaluate, Corpse/GetReactionTo, Creature.Main/IsTappedBy, game_Group_Group/SameSubGroup | — |
| GetGroupRef | method | — | game_Group_Group/ChangeMembersGroup#2, game_Group_Group/SwapMembersGroup, game_Group_Group/SwapMembersGroup#2 | — |
| GetSubGroup | method | — | game_Group_Group/SameSubGroup, Spell.Main/FillRaidOrPartyTargets, Spell.Main/SetTargetMap, Unit.SpellAuras/Update | — |
| GetGroupUpdateFlag | method | — | game_Group_Group/UpdatePlayerOutOfRange, WorldSession.GroupHandler/BuildPartyMemberStatsChangedPacket | — |
| SetGroupUpdateFlag | method | — | game_Group_Group/AddMember, game_Group_Group/UpdatePlayerOnlineStatus, Pet.Main/LoadPetFromDB, Pet.Main/Unsummon, Unit.Main/ApplyMaxPowerMod, Unit.Main/ApplyPowerMod, Unit.Main/SetDisplayId, Unit.Main/SetHealth, Unit.Main/SetLevel, Unit.Main/SetMaxHealth, Unit.Main/SetMaxPower, Unit.Main/SetPower, Unit.Main/SetPowerType, Unit.Main/SetPvP, Unit.Main/UpdateAuraForGroup, Unit.SpellAuras/HandleAuraGhost, WorldSession.PetHandler/HandlePetRename | — |
| GetAuraUpdateMask | method | — | WorldSession.GroupHandler/BuildPartyMemberStatsPacket | — |
| SetAuraUpdateSlot | method | — | Unit.Main/UpdateAuraForGroup | — |
| SetAuraUpdateMask | method | — | game_Group_Group/AddMember | — |
| IsInSameRaidWith | method | — | GameObject/Use, Spell.Effects/EffectDummy, Spell.Main/ChainHealingHash, Spell.Main/CheckCast, spell_warlock/OnCheckCast#4, Unit.Main/IsInRaidWith, WorldObject.Object/BuildValuesUpdate, WorldObject.Object/GetUpdateFieldFlagsForTarget, WorldObject.Object/IsValidHelpfulTarget, WorldSession.GroupHandler/HandleRequestPartyMemberStatsOpcode, WorldSession.QuestHandler/HandleQuestConfirmAccept | — |
| RemoveFromGroup | method | — | ChatHandler.PlayerBotMgr/Update, PartyBotAI/AddToPlayerGroup, WorldSession.GroupHandler/HandleGroupDisbandOpcode, WorldSession.Main/ForcePlayerLogoutDelay | — |
| CanUseBank | method | Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectGuid/operator!, ObjectGuid/operator== | WorldSession.ItemHandler/HandleAutoBankItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBagItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBankItemOpcode, WorldSession.ItemHandler/HandleSwapInvItemOpcode, WorldSession.ItemHandler/HandleSwapItem | — |
| SetLFGAreaId | method | — | LFGMgr/AddToQueue, LFGQueue/RemovePlayerFromQueue | — |
| GetLFGAreaId | method | — | — | — |
| IsInLFG | method | — | WorldSession.Main/LogoutPlayer | — |
| GetOriginalGroup | method | — | game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup, game_Group_Group/AddInvite, game_Group_Group/Disband, game_Group_Group/_removeMember, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GroupHandler/HandleLootMethodOpcode, WorldSession.GroupHandler/HandleRaidTargetUpdateOpcode | — |
| GetOriginalGroupRef | method | — | game_Group_Group/ChangeMembersGroup#2, game_Group_Group/SwapMembersGroup, game_Group_Group/SwapMembersGroup#2 | — |
| GetOriginalSubGroup | method | — | game_Group_Group/ChangeMembersGroup#2 | — |
| CanInteractWithQuestGiver | method | Object/GetTypeId, Unit.Main/IsAlive | WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode | — |
| SetInGuild | method | — | game_Guild_Guild/AddMember, game_Guild_Guild/DelMember | — |
| SetRank | method | — | game_Guild_Guild/AddMember, game_Guild_Guild/ChangeRank, game_Guild_Guild/DelMember | — |
| SetGuildIdInvited | method | — | game_Guild_Guild/AddMember, WorldSession.GuildHandler/HandleGuildDeclineOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode | — |
| GetGuildId | method | — | AccountMgr/GetWhisperScore, ChatHandler.MiscCommands/HandleGuildCreateCommand, ChatHandler.MiscCommands/HandleGuildRankCommand, ChatHandler.MiscCommands/HandleGuildUninviteCommand, game_Guild_Guild/AddMember, MasterPlayer.Main/LoadPlayer, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.GuildHandler/HandleGuildAcceptOpcode, WorldSession.GuildHandler/HandleGuildAddRankOpcode, WorldSession.GuildHandler/HandleGuildChangeInfoTextOpcode, WorldSession.GuildHandler/HandleGuildCreateOpcode, WorldSession.GuildHandler/HandleGuildDeclineOpcode, WorldSession.GuildHandler/HandleGuildDelRankOpcode, WorldSession.GuildHandler/HandleGuildDemoteOpcode, WorldSession.GuildHandler/HandleGuildDisbandOpcode, WorldSession.GuildHandler/HandleGuildInfoOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.GuildHandler/HandleGuildLeaderOpcode, WorldSession.GuildHandler/HandleGuildLeaveOpcode, WorldSession.GuildHandler/HandleGuildMOTDOpcode, WorldSession.GuildHandler/HandleGuildPromoteOpcode, WorldSession.GuildHandler/HandleGuildRankOpcode, WorldSession.GuildHandler/HandleGuildRemoveOpcode, WorldSession.GuildHandler/HandleGuildRosterOpcode, WorldSession.GuildHandler/HandleGuildSetOfficerNoteOpcode, WorldSession.GuildHandler/HandleGuildSetPublicNoteOpcode, WorldSession.GuildHandler/HandleSaveGuildEmblemOpcode, WorldSession.Main/LogoutPlayer, WorldSession.MiscHandler/operator(), WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode, WorldSession.PetitionsHandler/HandlePetitionShowSignOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode | — |
| GetRank | method | — | game_Guild_Guild/Roster, MasterPlayer.Main/LoadPlayer, WorldSession.GuildHandler/HandleGuildChangeInfoTextOpcode, WorldSession.GuildHandler/HandleGuildDemoteOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.GuildHandler/HandleGuildMOTDOpcode, WorldSession.GuildHandler/HandleGuildPromoteOpcode, WorldSession.GuildHandler/HandleGuildRemoveOpcode, WorldSession.GuildHandler/HandleGuildSetOfficerNoteOpcode, WorldSession.GuildHandler/HandleGuildSetPublicNoteOpcode | — |
| GetGuildIdInvited | method | — | WorldSession.GuildHandler/HandleGuildAcceptOpcode, WorldSession.GuildHandler/HandleGuildDeclineOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode | — |
| FindNearestInteractableNpcWithFlag | method | NearestInteractableNpcWithFlag/NearestInteractableNpcWithFlag | — | — |
| ToPlayer | function | — | boss_jindo/UpdateAI, Creature.Main/AddCooldown, DynamicObject/GetAffectingPlayer, GameObject/FinishRitual, GameObject/Update, game_Group_Group/RewardGroupAtKill_helper, Map.ScriptCommands/ScriptCommand_CreateItem, Map.ScriptCommands/ScriptCommand_FailQuest, Map.ScriptCommands/ScriptCommand_KillCredit, Map.ScriptCommands/ScriptCommand_MeetingStone, Map.ScriptCommands/ScriptCommand_PlaySound, Map.ScriptCommands/ScriptCommand_QuestCredit, Map.ScriptCommands/ScriptCommand_QuestExplored, Map.ScriptCommands/ScriptCommand_RemoveItem, Map.ScriptCommands/ScriptCommand_SendTaxiPath, Map.ScriptCommands/ScriptCommand_SetPvP, Map.ScriptCommands/ScriptCommand_TerminateCondition, scourge_invasion/OnScriptEventHappened#3, scourge_invasion/UpdateAI#9, Spell.Effects/DoCreateItem, Spell.Effects/EffectBind, Spell.Effects/EffectDummy, Spell.Effects/EffectEnchantHeldItem, Spell.Effects/EffectInebriate, Spell.Effects/EffectLanguage, Spell.Effects/EffectProficiency, Spell.Effects/EffectReputation, Spell.Effects/EffectScriptEffect, Spell.Effects/EffectSummonPlayer, Spell.Main/CheckCast, Spell.Main/ValidateExplicitTargetMask, spell_item/OnCheckCast, Unit.Main/Kill, Unit.Main/SendPetCastFail, Unit.Main/SetInCombatWithAggressor, Unit.Main/SetInCombatWithVictim, Unit.SpellAuras/HandleAuraDummy, Unit.SpellAuras/HandleAuraEmpathy, Unit.SpellAuras/HandleBindSight, Unit.SpellAuras/HandleModPossessPet, Unit.SpellAuras/TriggerSpell | — |
| GetNPCIfCanInteractWith | method | Map.Main/GetAnyTypeCreature, Object/IsInWorld, ObjectGuid/operator!, WorldObject.Object/GetMap | WorldSession.AuctionHouseHandler/GetCheckedAuctionHouseForAuctioneer, WorldSession.AuctionHouseHandler/HandleAuctionHelloOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.GuildHandler/HandleSaveGuildEmblemOpcode, WorldSession.ItemHandler/CheckBanker, WorldSession.ItemHandler/HandleBuybackItem, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.ItemHandler/SendListInventory, WorldSession.NPCHandler/CheckStableMaster, WorldSession.NPCHandler/HandleBinderActivateOpcode, WorldSession.NPCHandler/HandleGossipHelloOpcode, WorldSession.NPCHandler/HandleGossipSelectOptionOpcode, WorldSession.NPCHandler/HandleListStabledPetsOpcode, WorldSession.NPCHandler/HandleRepairItemOpcode, WorldSession.NPCHandler/HandleSpiritHealerActivateOpcode, WorldSession.NPCHandler/HandleTabardVendorActivateOpcode, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.NPCHandler/SendTrainerList, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode, WorldSession.PetitionsHandler/SendPetitionShowList, WorldSession.QuestHandler/HandleQuestgiverHelloOpcode, WorldSession.SkillHandler/HandleTalentWipeConfirmOpcode, WorldSession.TaxiHandler/HandleActivateTaxiExpressOpcode, WorldSession.TaxiHandler/HandleActivateTaxiOpcode, WorldSession.TaxiHandler/HandleTaxiQueryAvailableNodes | — |
| ToPlayer#2 | function | — | Conditions/Evaluate | — |
| CanInteractWithNPC | method | Creature.Main/HasStaticFlag, Object/HasFlag, Object/IsInWorld, ObjectMgr/GetFactionEntry, ObjectMgr/GetFactionTemplateEntry, ReputationMgr/GetRank, Unit.Main/GetCharmerGuid, Unit.Main/GetClass, Unit.Main/GetFactionTemplateId, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsHostileTo, Unit.Main/IsInCombat, Unit.Main/IsInvisibleForAlive, Unit.Main/IsTaxiFlying, WorldObject.Object/IsWithinDistInMap | — | — |
| GetGameObjectIfCanInteractWith | method | Map.Main/GetGameObject, Object/IsInWorld, ObjectGuid/operator!, WorldObject.Object/GetMap | WorldSession.LFGHandler/HandleMeetingStoneJoinOpcode, WorldSession.MailHandler/CheckMailBox, WorldSession.NPCHandler/HandleGossipSelectOptionOpcode | — |
| CanInteractWithGameObject | method | GameObject/GetGoType, GameObject/IsAtInteractDistance#2, GameObject/isSpawned, Object/IsInWorld, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsTaxiFlying | — | — |
| CanSeeHealthOf | method | Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself | WorldObject.Object/BuildValuesUpdate | — |
| CanSeeSpecialInfoOf | method | Object/GetObjectGuid, Unit.Main/HasAuraTypeByCaster | WorldObject.Object/GetUpdateFieldFlagsForTarget | — |
| SetGameMasterOnHelper | ctor | — | — | — |
| operator()#3 | method | HostileRefManager/setOnlineOfflineState#2, Unit.Main/GetHostileRefManager, Unit.Main/SetFactionTemplateId | — | — |
| SetGameMasterOffHelper | ctor | — | — | — |
| operator()#2 | method | HostileRefManager/setOnlineOfflineState#2, Unit.Main/GetHostileRefManager, Unit.Main/SetFactionTemplateId | — | — |
| SetGMChat | method | WorldSession.Main/SendNotification#2 | ChatHandler.MiscCommands/HandleGMChatCommand, spell_special/OnEffectExecute#10, spell_special/OnEffectExecute#11 | — |
| SetGameMaster | method | Camera/UpdateVisibilityForOwner, HostileRefManager/setOnlineOfflineState#2, Unit.Main/CombatStopWithPets, Unit.Main/GetFactionTemplateId, Unit.Main/GetHostileRefManager, Unit.Main/GetRace, Unit.Main/SetFactionTemplateId, UpdateMask/SetBit, UpdateMask/SetCount, UpdateMask/UpdateMask, World/IsFFAPvPRealm, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldObject.Object/UpdateObjectVisibility, WorldSession.Main/SendNotification#2 | ChatHandler.MiscCommands/HandleGMCommand, PartyBotAI/UpdateAI, spell_special/OnEffectExecute#5, spell_special/OnEffectExecute#6 | — |
| SetGMVisible | method | Database/PExecute#2, MasterPlayer.Main/ClearAllowedWhisperers, Object/GetGUIDLow, Unit.Main/AddAura, Unit.Main/HasAuraType, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetVisibility, WorldSession.Main/GetMasterPlayer, WorldSession.Main/SendNotification#2 | ChatHandler.MiscCommands/HandleGMVisibleCommand, spell_special/OnEffectExecute#7, spell_special/OnEffectExecute#8 | characters |
| SetCheatFly | method | WorldSession.Main/SendNotification#2 | ChatHandler.CharacterCommands/HandleCheatFlyCommand | — |
| SetCheatFixedZ | method | MovementInfo/AddMovementFlag, MovementInfo/RemoveMovementFlag, Unit.Main/SendHeartBeat, WorldSession.Main/SendNotification#2, WorldSession.MovementHandler/RejectMovementPacketsFor | ChatHandler.CharacterCommands/HandleCheatFixedZCommand | — |
| SetCheatBeastmaster | method | WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldSession.Main/SendNotification#2 | ChatHandler.CharacterCommands/HandleCheatBeastmasterCommand, spell_special/OnEffectExecute, spell_special/OnEffectExecute#2 | — |
| SetCheatGod | method | Unit.Main/SetInvincibilityHpThreshold, WorldSession.Main/SendNotification#2 | ChatHandler.CharacterCommands/HandleCheatGodCommand, ChatHandler.UnitCommands/HandleDieHelper, PlayerBotAI/OnPlayerLogin#2 | — |
| SetCheatNoCooldown | method | WorldSession.Main/SendNotification#2 | ChatHandler.CharacterCommands/HandleCheatCooldownCommand | — |
| SetCheatInstantCast | method | WorldSession.Main/SendNotification#2 | ChatHandler.CharacterCommands/HandleCheatCastTimeCommand | — |
| SetCheatNoPowerCost | method | WorldSession.Main/SendNotification#2 | ChatHandler.CharacterCommands/HandleCheatPowerCommand | — |
| SetCheatDebuffImmunity | method | WorldSession.Main/SendNotification#2 | ChatHandler.CharacterCommands/HandleCheatDebuffImmunityCommand | — |
| SetCheatAlwaysCrit | method | WorldSession.Main/SendNotification#2 | ChatHandler.CharacterCommands/HandleCheatAlwaysCritCommand | — |
| SetCheatNoCastCheck | method | WorldSession.Main/SendNotification#2 | ChatHandler.CharacterCommands/HandleCheatNoCastCheckCommand | — |
| SetCheatAlwaysProc | method | WorldSession.Main/SendNotification#2 | ChatHandler.CharacterCommands/HandleCheatAlwaysProcCommand | — |
| SetCheatTriggerPass | method | WorldSession.Main/SendNotification#2 | ChatHandler.CharacterCommands/HandleCheatTriggerPassCommand | — |
| SetCheatIgnoreTriggers | method | WorldSession.Main/SendNotification#2 | ChatHandler.CharacterCommands/HandleCheatIgnoreTriggersCommand | — |
| SetCheatDebugTargetInfo | method | Map.Main/GetUnit, ObjectGuid/IsUnit, UpdateData/BuildPacket#3, UpdateData/HasData, UpdateData/UpdateData, WorldObject.Object/BuildValuesUpdateBlockForPlayerWithFlags, WorldObject.Object/GetMap, WorldPacket/WorldPacket, WorldSession.Main/SendNotification#2 | ChatHandler.CharacterCommands/HandleCheatDebugTargetInfoCommand | — |
| IsAllowedWhisperFrom | method | Group/IsMember, Guild/GetMemberSlot, GuildMgr/GetGuildById, Object/GetObjectGuid, ObjectGuid/operator==, SocialMgr/HasFriend | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| IsGroupVisibleFor | method | World/getConfig#4 | Unit.Main/IsVisibleForOrDetect | — |
| IsInSameGroupWith | method | game_Group_Group/SameSubGroup | Unit.Main/IsInPartyWith, WorldSession.QuestHandler/HandleQuestConfirmAccept | — |
| UninviteFromGroup | method | game_Group_Group/Disband, game_Group_Group/RemoveAllInvites, game_Group_Group/RemoveInvite, Group/GetMembersCount, Group/IsCreated, ObjectMgr/RemoveGroup | Map.Main/CrashUnload, WorldSession.GroupHandler/HandleGroupDeclineOpcode, WorldSession.GroupHandler/HandleGroupUninviteGuidOpcode, WorldSession.GroupHandler/HandleGroupUninviteOpcode, WorldSession.Main/LogoutPlayer | — |
| RemoveFromGroup#2 | method | game_Group_Group/RemoveMember, ObjectMgr/RemoveGroup | WorldSession.GroupHandler/HandleGroupUninviteGuidOpcode, WorldSession.GroupHandler/HandleGroupUninviteOpcode | — |
| SendLogXPGain | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| GiveXP | method | Object/GetUInt32Value, Object/HasFlag, Unit.Main/GetLevel, Unit.Main/IsAlive, World/getConfig#4, WorldObject.Object/SetUInt32Value, WorldSession.Main/HasTrialRestrictions | game_Group_Group/RewardGroupAtKill_helper | — |
| GiveLevel | method | BattleGroundMgr/BgTemplateId, BattleGroundMgr/BuildBattleGroundStatusPacket, BattleGroundMgr/GetBattleGround, BattleGroundMgr/GetBattleGroundTemplate, BattleGroundMgr/GetPlayerGroupInfoData, BattleGroundMgr/RemovePlayer, BattleGroundMgr/ScheduleQueueUpdate, ByteBuffer/operator<<#10, Group/GetFirstMember, Group/GetMembersCount, GroupReference/next, Map.Main/GetPlayers, Map.Main/IsDungeon, Map.Main/IsRaid, MapReference/next#2, MapRefManager/getFirst#2, Object/GetGUIDLow, Object/GetGuidStr, Object/GetObjectGuid, ObjectMgr/GetPlayerClassLevelInfo, ObjectMgr/GetPlayerLevelInfo, ObjectMgr/GetXPForLevel, Pet.Main/SynchronizeLevelWithOwner, Player.StatSystem/GetHealthBonusFromStamina, Player.StatSystem/GetManaBonusFromIntellect, Player.StatSystem/UpdateAllStats#3, Unit.Main/GetClass, Unit.Main/GetCreateHealth, Unit.Main/GetCreateMana, Unit.Main/GetCreateStat, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPet, Unit.Main/GetPower, Unit.Main/GetPowerType, Unit.Main/GetRace, Unit.Main/IsAlive, Unit.Main/SetCreateHealth, Unit.Main/SetCreateMana, Unit.Main/SetCreateStat, Unit.Main/SetHealth, Unit.Main/SetLevel, Unit.Main/SetPower, World/SendGMText, WorldObject.Object/FindMap, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetZoneId, WorldObject.Object/SetUInt32Value, WorldPacket/WorldPacket, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | AiBotAI.Main/OnSessionLoaded, BattleBotAI.Main/UpdateAI, ChatHandler.CharacterCommands/HandleCharacterLevel, ObjectMgr/ApplyPremadeGearTemplateToPlayer, ObjectMgr/ApplyPremadeSpecTemplateToPlayer, PartyBotAI/CloneFromPlayer, PartyBotAI/UpdateAI, PlayerBotAI/UpdateAI | — |
| UpdateFreeTalentPoints | method | Unit.Main/GetLevel, WorldSession.Main/GetSecurity | — | — |
| InitTalentForLevel | method | — | BattleBotAI.Main/UpdateAI, ChatHandler.CharacterCommands/HandleCharacterLevel, ChatHandler.CharacterCommands/HandleResetLevelCommand, ChatHandler.CharacterCommands/HandleResetStatsCommand, ObjectMgr/ApplyPremadeGearTemplateToPlayer, ObjectMgr/ApplyPremadeSpecTemplateToPlayer, PartyBotAI/CloneFromPlayer, PartyBotAI/UpdateAI | — |
| InitStatsForLevel | method | Object/SetInt16Value, ObjectMgr/GetPlayerClassLevelInfo, ObjectMgr/GetPlayerLevelInfo, ObjectMgr/GetXPForLevel, Pet.Main/SynchronizeLevelWithOwner, Player.StatSystem/_ApplyAllStatBonuses, Player.StatSystem/_RemoveAllStatBonuses, Unit.Main/GetClass, Unit.Main/GetCreatePowers, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPet, Unit.Main/GetPower, Unit.Main/GetRace, Unit.Main/SetArmor, Unit.Main/SetCreateHealth, Unit.Main/SetCreateMana, Unit.Main/SetCreateStat, Unit.Main/SetHealth, Unit.Main/SetMaxHealth, Unit.Main/SetMaxPower, Unit.Main/SetPower, Unit.Main/SetResistance, Unit.Main/SetStat, WorldObject.Object/RemoveByteFlag, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldObject.Object/SetFloatValue, WorldObject.Object/SetInt32Value, WorldObject.Object/SetUInt32Value | ChatHandler.CharacterCommands/HandleResetLevelCommand, ChatHandler.CharacterCommands/HandleResetStatsCommand | — |
| SendInitialSpells | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#13, ByteBuffer/operator<<#7, ByteBuffer/wpos, CooldownContainer/size, CooldownData/GetCatCDExpireTime, CooldownData/GetCategory, CooldownData/GetItemId, CooldownData/GetSpellCDExpireTime, CooldownData/GetSpellId, CooldownData/IsPermanent, Log.Main/Out, World/GetCurrentClockTime, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| AddSpell | method | ByteBuffer/operator<<#13, Database/PExecute#2, DBCStores/GetTalentSpellCost, DBCStores/GetTalentSpellPos, Log.Main/Out, Object/IsInWorld, SpellCaster/CastSpell#2, SpellEntry/HasEffect, SpellMgr/GetPrevSpellInChain, SpellMgr/GetSpellBookSuccessorSpellId, SpellMgr/GetSpellEntry, SpellMgr/GetSpellLearnSpellMapBounds, SpellMgr/Instance, SpellMgr/IsPrimaryProfessionFirstRankSpell, SpellMgr/IsSpellValid, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | character_spell |
| IsNeedCastPassiveLikeSpellAtLearn | method | SpellEntry/GetErrorAtShapeshiftedCast, SpellEntry/HasAttribute#3, SpellEntry/HasAttribute#4, SpellEntry/IsNeedCastSpellAtFormApply, Unit.Main/GetShapeshiftForm, Unit.Main/HasAuraState | — | — |
| LearnSpell | method | ByteBuffer/operator<<#10, Object/IsInWorld, SpellMgr/GetSpellChainNext, SpellMgr/Instance, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | AiBotAI.Bridge/BridgeHandleLearnSpell, AiBotAI.Bridge/BridgeHandleTrain, ChatHandler.CharacterCommands/HandleLearnAllCommand, ChatHandler.CharacterCommands/HandleLearnAllGMCommand, ChatHandler.CharacterCommands/HandleLearnAllItemsCommand, ChatHandler.CharacterCommands/HandleLearnAllLangCommand, ChatHandler.CharacterCommands/HandleLearnAllMySpellsCommand, ChatHandler.CharacterCommands/HandleLearnCommand, ChatHandler.CharacterCommands/HandleLearnSkillRecipesHelper, ChatHandler.CharacterCommands/HandleLearnTrainerHelper, CombatBotBaseAI/EquipOrUseNewItem, CombatBotBaseAI/LearnArmorProficiencies, custom_creatures/LearnSkillRecipesHelper, ObjectMgr/ApplyPremadeGearTemplateToPlayer, ObjectMgr/ApplyPremadeSpecTemplateToPlayer, PartyBotAI/CloneFromPlayer, Pet.Main/CheckLearning, Pet.Main/InitPetCreateSpells, Spell.Effects/EffectLearnSpell | — |
| RemoveSpell | method | ByteBuffer/operator<<#13, DBCStores/GetTalentSpellCost, DBCStores/GetTalentSpellPos, SpellEntry/IsPassiveSpell, SpellMgr/GetPetAura, SpellMgr/GetPrevSpellInChain, SpellMgr/GetSpellBookSuccessorSpellId, SpellMgr/GetSpellChainNext, SpellMgr/GetSpellLearnSpellMapBounds, SpellMgr/Instance, SpellMgr/IsPrimaryProfessionFirstRankSpell, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RemovePetAura, World/getConfig#4, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.CharacterCommands/HandleRemoveRidingCommand, ChatHandler.CharacterCommands/HandleUnLearnAllGMCommand, ChatHandler.CharacterCommands/HandleUnLearnCommand, ChatHandler.CharacterCommands/HandleUnLearnSkillRecipesHelper | — |
| _LoadSpellCooldowns | method | CooldownContainer/AddCooldown, Field/GetUInt32, Field/GetUInt64, Log.Main/Out, Object/GetGuidStr, ObjectMgr/GetItemPrototype, QueryResult/Fetch, QueryResult/NextRow, SpellMgr/GetSpellEntry, SpellMgr/Instance, World/GetCurrentClockTime | — | — |
| _SaveSpellCooldowns | method | CooldownData/GetCatCDExpireTime, CooldownData/GetCategory, CooldownData/GetItemId, CooldownData/GetSpellCDExpireTime, CooldownData/GetSpellId, CooldownData/IsPermanent, Database/CreateStatement, Object/GetGUIDLow, SqlPreparedStatement/Execute#2, SqlPreparedStatement/operator=, SqlStatement/addUInt32, SqlStatement/addUInt64, SqlStatementID/SqlStatementID | — | character_spell_cooldown |
| UpdateResetTalentsMultiplier | method | World/getConfig#4, World/GetGameTime | — | — |
| GetResetTalentsCost | method | World/getConfig, World/getConfig#4, World/GetWowPatch | — | — |
| ResetTalents | method | SpellEntry/IsPassiveSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetClassMask, Unit.Main/RemoveAurasDueToSpell, World/getConfig#4 | ChatHandler.CharacterCommands/HandleResetTalentsCommand, ObjectMgr/ApplyPremadeSpecTemplateToPlayer, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.SkillHandler/HandleTalentWipeConfirmOpcode | — |
| BuildCreateUpdateBlockForPlayer | method | WorldObject.Object/BuildCreateUpdateBlockForPlayer | Map.Main/SendInitSelf | — |
| DestroyForPlayer | method | WorldObject.Object/DestroyForPlayer | — | — |
| HasSpell | method | — | AiBotAI.Bridge/BridgeHandleLearnSpell, AiBotAI.Combat/UpdateInCombatAI_Hunter, AiBotAI.Combat/UpdateInCombatAI_Mage, AiBotAI.Combat/UpdateInCombatAI_Priest, AiBotAI.Combat/UpdateInCombatAI_Warlock, AuctionHouseMgr/BuildListAuctionItems, BattleBotAI.Main/UpdateInCombatAI_Hunter, BattleBotAI.Main/UpdateInCombatAI_Mage, BattleBotAI.Main/UpdateInCombatAI_Priest, BattleBotAI.Main/UpdateInCombatAI_Warlock, ChatHandler.CharacterCommands/HandleLearnCommand, ChatHandler.CharacterCommands/HandleUnLearnCommand, ChatHandler.LookupCommands/ShowSpellListHelper, CombatBotBaseAI/AutoAssignRole, CombatBotBaseAI/EquipOrUseNewItem, CombatBotBaseAI/LearnArmorProficiencies, CombatBotBaseAI/SummonPetIfNeeded, Conditions/Evaluate, Creature.Main/IsTrainerOf, go_scripts/GOHello_go_field_repair_bot_74A, instance_dire_maul/GossipHello_npc_knot_thimblejack, instance_naxxramas.Main/LearnCraftIfCan, LFGMgr/GetTalentTrees, ObjectMgr/ApplyPremadeGearTemplateToPlayer, ObjectMgr/ApplyPremadeSpecTemplateToPlayer, PartyBotAI/CloneFromPlayer, PartyBotAI/UpdateInCombatAI_Hunter, PartyBotAI/UpdateInCombatAI_Mage, PartyBotAI/UpdateInCombatAI_Priest, PartyBotAI/UpdateInCombatAI_Warlock, Pet.Main/InitPetCreateSpells, Unit.SpellAuras/HandleShapeshiftBoosts | — |
| HasActiveSpell | method | — | WorldSession.SpellHandler/HandleCastSpellOpcode | — |
| GetTrainerSpellState | method | SpellMgr/GetSpellChainNode, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsPrimaryProfessionFirstRankSpell, SpellMgr/IsPrimaryProfessionSkill, Unit.Main/GetLevel | AiBotAI.Bridge/BridgeHandleTrain, ChatHandler.CharacterCommands/HandleLearnTrainerHelper, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.NPCHandler/SendTrainerList | — |
| DeleteFromDB | method | Bag/NewItemOrBag, Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Database/PQuery, Field/GetBool, Field/GetCppString, Field/GetUInt16, Field/GetUInt32, game_Guild_Guild/DelMember, game_Guild_Guild/Disband, game_Mail_Mail/AddItem, game_Mail_Mail/SendReturnToSender, game_Mail_Mail/SetSubjectAndBodyId, game_Objects_Item/FSetState, game_Objects_Item/LoadFromDB, game_Objects_Item/SaveToDB, GuildMgr/GetPlayerGuild, Log.Main/Out, MailDraft/MailDraft, MailDraft/SetMailTemplate, MailDraft/SetMoney, ObjectAccessor/ConvertCorpseForPlayer, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid#2, ObjectMgr/DeletePlayerFromCache, ObjectMgr/GetGroupByMember, ObjectMgr/GetItemPrototype, ObjectMgr/GetPlayerAccountIdByGUID, ObjectMgr/GetPlayerDataByGUID, Pet.Main/DeleteFromDB#2, QueryResult/Fetch, QueryResult/NextRow, World/getConfig#4, World/UpdateRealmCharCount | AccountMgr/DeleteAccount, ChatHandler.CharacterCommands/HandleCharacterDeletedDeleteCommand, ChatHandler.CharacterCommands/HandleCharacterEraseCommand, ChatHandler.CharacterCommands/HandleCleanCharactersToDeleteCommand, ChatHandler.CharacterCommands/HandleServiceDeleteCharacters, WorldSession.CharacterHandler/HandleCharDeleteOpcode | characters, character_action, character_aura, character_battleground_data, character_deleted_items, character_forgotten_skills, character_gifts, character_homebind, character_instance, character_inventory, character_pet, character_queststatus, character_reputation, character_skills, character_social, character_spell, character_spell_cooldown, group_instance, guild_eventlog, item_instance, mail, mail_items |
| DeleteOldCharacters | method | World/getConfig#4 | World/CharactersDatabaseWorkerThread | — |
| DeleteOldCharacters#2 | method | Database/PQuery, Field/GetUInt32, ObjectGuid/ObjectGuid#2, QueryResult/Fetch, QueryResult/NextRow | ChatHandler.CharacterCommands/HandleCharacterDeletedOldCommand | characters |
| SetFly | method | Transport/RemovePassenger, Unit.Main/SendHeartBeat, Unit.Main/StopMoving, WorldObject.Object/GetTransport, WorldSession.MovementHandler/RejectMovementPacketsFor | — | — |
| ApplyGhostForm | method | SpellCaster/CastSpell#2, Unit.Main/SetWaterWalking | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| RemoveGhostForm | method | Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetWaterWalking | — | — |
| BuildPlayerRepop | method | Corpse/ResetGhostTime, Log.Main/Out, Object/GetGUIDLow, SpellCaster/CastSpell#2, Unit.Main/InterruptSpellsCastedOnMe, Unit.Main/SetHealth, Unit.Main/SetRooted, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/RemoveFlag, WorldSession.Main/IsLogingOut | AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, PartyBotAI/UpdateAI, WorldSession.Main/LogoutPlayer, WorldSession.MiscHandler/HandleRepopRequestOpcode, WorldSession.MovementHandler/HandleMoverRelocation | — |
| ResurrectPlayer | method | Camera/UpdateVisibilityForOwner, SpellAuraHolder/SetAuraDuration, SpellCaster/CastSpell#2, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetRace, Unit.Main/GetSpellAuraHolder#2, Unit.Main/InterruptSpellsCastedOnMe, Unit.Main/SetHealth, Unit.Main/SetPower, Unit.Main/SetRooted, Unit.SpellAuras/UpdateAuraDuration, World/getConfig#3, WorldObject.Object/GetZoneAndAreaId, WorldObject.Object/UpdateObjectVisibility | AiBotAI.Bridge/BridgeHandleResurrect, AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, ChatHandler.CharacterCommands/HandleGroupReviveCommand, ChatHandler.CharacterCommands/HandleReviveCommand, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, instance_blackwing_lair/AreaTrigger_at_orb_of_command, PartyBotAI/UpdateAI, Spell.Effects/EffectSelfResurrect, Spell.Effects/EffectSpiritHeal, Transport/TeleportTransport, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.MiscHandler/HandleReclaimCorpseOpcode, WorldSession.NPCHandler/SendSpiritResurrect | — |
| KillPlayer | method | MapEntry/Instanceable, MovementAnticheat/OnDeath, Object/ApplyModByteFlag, WorldObject.Object/GetMapId, WorldObject.Object/SetUInt32Value, WorldObject.Object/UpdateObjectVisibility | WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.Main/LogoutPlayer, WorldSession.MiscHandler/HandleRepopRequestOpcode, WorldSession.MovementHandler/HandleMoverRelocation | — |
| CreateCorpse | method | Corpse/Corpse, Corpse/Create#2, Corpse/SaveToDB, game_Objects_Item/GetProto, Map.Main/IsBattleGround, Object/GetByteValue, Object/HasFlag, ObjectAccessor/AddCorpse, ObjectMgr/GenerateCorpseLowGuid, Unit.Main/GetGender, Unit.Main/GetNativeDisplayId, Unit.Main/GetRace, WorldObject.Object/GetMap, WorldObject.Object/SetByteValue, WorldObject.Object/SetUInt32Value | — | — |
| SpawnCorpseBones | method | Object/GetObjectGuid, ObjectAccessor/ConvertCorpseForPlayer | AiBotAI.Bridge/BridgeHandleResurrect, AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, ChatHandler.CharacterCommands/HandleGroupReviveCommand, ChatHandler.CharacterCommands/HandleReviveCommand, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, instance_blackwing_lair/AreaTrigger_at_orb_of_command, PartyBotAI/UpdateAI, Spell.Effects/EffectSelfResurrect, Spell.Effects/EffectSpiritHeal, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.MiscHandler/HandleReclaimCorpseOpcode, WorldSession.NPCHandler/SendSpiritResurrect | — |
| GetCorpse | method | Object/GetObjectGuid, ObjectAccessor/GetCorpseForPlayerGUID | AiBotAI.Main/UpdateAI, Creature.Main/IsVisibleInGridForPlayer, instance_blackwing_lair/AreaTrigger_at_orb_of_command, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.MiscHandler/HandleReclaimCorpseOpcode, WorldSession.NPCHandler/SendSpiritResurrect, WorldSession.QueryHandler/HandleCorpseQueryOpcode | — |
| DurabilityLossAll | method | Bag/GetBagSize | Spell.Effects/EffectDurabilityDamagePCT, Unit.Main/Kill, WorldSession.NPCHandler/SendSpiritResurrect | — |
| DurabilityLoss | method | Object/GetUInt32Value | Spell.Effects/EffectDurabilityDamagePCT, Spell.Effects/EffectSummonChangeItem | — |
| DurabilityPointsLossAll | method | Bag/GetBagSize | Spell.Effects/EffectDurabilityDamage | — |
| DurabilityPointsLoss | method | game_Objects_Item/GetSlot, game_Objects_Item/IsEquipped, game_Objects_Item/SetState, Object/GetUInt32Value, World/getConfig, WorldObject.Object/SetUInt32Value | Spell.Effects/EffectDurabilityDamage | — |
| DurabilityPointLossForEquipSlot | method | — | Spell.Main/TakeAmmo, Unit.Main/DealDamage | — |
| DurabilityRepairAll | method | — | AiBotAI.Bridge/BridgeHandleRepairItems, ChatHandler.CharacterCommands/HandleRepairitemsCommand, npcs_special/GossipSelect_npc_res_fixer, WorldSession.NPCHandler/HandleRepairItemOpcode | — |
| DurabilityRepair | method | game_Objects_Item/GetProto, game_Objects_Item/SetState, ItemPrototype/ItemSubClassToDurabilityMultiplierId, Log.Main/Out, Object/GetUInt32Value, WorldObject.Object/SetUInt32Value | WorldSession.NPCHandler/HandleRepairItemOpcode | — |
| ScheduleRepopAtGraveyard | method | Object/IsInWorld, WorldSession.Main/IsConnected | WorldSession.MiscHandler/HandleRepopRequestOpcode | — |
| RepopAtGraveyard | method | game_Battlegrounds_BattleGround/GetClosestGraveYard, Object/IsInWorld, ObjectMgr/GetClosestGraveYard, ObjectMgr/GetWorldSafeLocFacing, Transport/RemovePassenger, Unit.Main/IsAlive, Unit.Main/UpdateVisibilityAndView, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTransport | BattleBotAI.Main/UpdateAI, game_Battlegrounds_BattleGround/ReturnPlayersToHomeGY, PartyBotAI/UpdateAI, scripts_battlegrounds_battleground/CorpseRemoved, Spell.Effects/EffectSpiritHeal, WorldSession.Main/LogoutPlayer, WorldSession.MovementHandler/HandleMoverRelocation | — |
| JoinedChannel | method | — | game_Chat_Channel/Join | — |
| LeftChannel | method | — | game_Chat_Channel/KickOrBan | — |
| CleanupChannels | method | ChannelMgr/channelMgr, ChannelMgr/LeftChannel, game_Chat_Channel/GetName, game_Chat_Channel/Leave, Log.Main/Out, Object/GetObjectGuid | WorldSession.Main/LogoutPlayer | — |
| UpdateLocalChannels | method | — | — | — |
| LeaveLFGChannel | method | game_Chat_Channel/IsLFG, game_Chat_Channel/Leave, Object/GetObjectGuid | — | — |
| HandleBaseModValue | method | Log.Main/Out, Player.StatSystem/UpdateCritPercentage, shared_Util/ApplyPercentModFloatVar, Unit.Main/CanModifyStats | Unit.SpellAuras/HandleAuraModCritPercent, Unit.SpellAuras/HandleShieldBlockValue | — |
| GetBaseModValue | method | Log.Main/Out | — | — |
| GetTotalBaseModValue | method | Log.Main/Out | — | — |
| GetShieldBlockValue | method | Unit.Main/GetStat | — | — |
| GetMeleeCritFromAgility | method | Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetStat | Player.StatSystem/UpdateAllCritPercentages | — |
| GetDodgeFromAgility | method | Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetStat | Player.StatSystem/UpdateDodgePercentage | — |
| SetRegularAttackTime | method | game_Objects_Item/GetProto, Unit.Main/SetAttackTime | Unit.SpellAuras/HandleAuraModDisarm | — |
| UpdateSkill | method | Object/GetUInt32Value, WorldObject.Object/SetUInt32Value | — | — |
| SkillGainChance | function | World/getConfig#4 | — | — |
| UpdateCraftSkill | method | Log.Main/Out, SpellMgr/GetSkillLineAbilityMapBoundsBySpellId, SpellMgr/Instance, World/getConfig#4, WorldSession.Main/HasTrialRestrictions | Spell.Effects/DoCreateItem, Spell.Effects/EffectDisEnchant, Spell.Effects/EffectEnchantItemPerm | — |
| UpdateGatherSkill | method | Log.Main/Out, World/getConfig#4, WorldSession.Main/HasTrialRestrictions | Spell.Effects/EffectOpenLock, Spell.Effects/EffectSkinning | — |
| UpdateFishingSkill | method | Log.Main/Out, World/getConfig#4, WorldSession.Main/HasTrialRestrictions | GameObject/Use | — |
| UpdateSkillPro | method | Log.Main/Out, Object/GetUInt32Value, shared_Util/irand, WorldObject.Object/SetUInt32Value | — | — |
| UpdateCombatSkills | method | Formulas/GetGrayLevel, game_Objects_Item/GetProficiencySkill, game_Objects_Item/GetProto, Log.Main/Out, Player.StatSystem/UpdateAllCritPercentages, Player.StatSystem/UpdateDefenseBonusesMod, shared_Util/roll_chance_f, SpellCaster/GetLevelForTarget, Unit.Main/GetLevel, Unit.Main/GetStat, Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/IsShapeShifted, World/getConfig#4 | Unit.Main/ProcSkillsAndReactives | — |
| UpdateSkillsForLevel | method | DBCStores/GetSkillRaceClassInfo, Object/GetUInt32Value, ObjectMgr/GetSkillRangeType, SpellCaster/GetSkillMaxForLevel, Unit.Main/GetClass, Unit.Main/GetRace, World/getConfig, World/GetConfigMaxSkillValue, WorldObject.Object/SetUInt32Value | — | — |
| UpdateSkillsToMaxSkillsForLevel | method | Object/GetUInt32Value, Player.StatSystem/UpdateDefenseBonusesMod, SpellMgr/IsProfessionOrRidingSkill, WorldObject.Object/SetUInt32Value | AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, ChatHandler.CharacterCommands/HandleMaxSkillCommand, PartyBotAI/UpdateAI | — |
| SetSkill | method | Aura/GetModifier, Log.Main/Out, Object/GetUInt32Value, ObjectMgr/GetQuestTemplate, QuestDef/GetRequiredSkill, SkillStatusData/SkillStatusData, Unit.Main/GetAurasByType, Unit.SpellAuras/ApplyModifier, WorldObject.Object/SetUInt32Value | ChatHandler.CharacterCommands/HandleLearnAllLangCommand, ChatHandler.CharacterCommands/HandleLearnAllRecipesCommand, ChatHandler.CharacterCommands/HandleSetSkillCommand, custom_creatures/LearnAllRecipesInProfession, Spell.Effects/EffectLearnSkill, WorldSession.SkillHandler/HandleUnlearnSkillOpcode | — |
| HasSkill | method | — | ChatHandler.LookupCommands/HandleLookupSkillCommand, Conditions/Evaluate, custom_creatures/GossipSelect_ProfessionNPC, go_scripts/GOHello_go_field_repair_bot_74A, Unit.SpellAuras/HandleAuraModSkill | — |
| GetSkill | method | Object/GetUInt32Value | — | — |
| ModifySkillBonus | method | Object/GetUInt32Value, WorldObject.Object/SetUInt32Value | Unit.SpellAuras/HandleAuraModSkill | — |
| GetSkillBonus | method | Object/GetUInt32Value | — | — |
| UpdateSkillTrainedSpells | method | Object/IsInWorld, SpellMgr/GetSkillLineAbilityMapBoundsBySkillId, SpellMgr/Instance, Unit.Main/GetClassMask, Unit.Main/GetRaceMask | — | — |
| UpdateSpellTrainedSkills | method | DBCStores/GetSkillRaceClassInfo, ObjectMgr/GetSkillRangeType, SpellCaster/GetSkillMaxForLevel, SpellMgr/GetFirstSpellInChain, SpellMgr/GetPrevSpellInChain, SpellMgr/GetSkillLineAbilityMapBoundsBySpellId, SpellMgr/GetSpellLearnSkill, SpellMgr/Instance, SpellMgr/IsProfessionSkill, Unit.Main/GetClass, Unit.Main/GetRace, World/getConfig | — | — |
| IsActionButtonDataValid | method | ObjectMgr/GetItemPrototype, SpellEntry/IsPassiveSpell#2, SpellMgr/GetSpellEntry, SpellMgr/Instance | MasterPlayer.Main/addActionButton, ObjectMgr/LoadPlayerInfo, WorldSession.MiscHandler/HandleSetActionButtonOpcode | — |
| SetPosition | method | GridDefines/IsValidMapCoord#4, Log.Main/Out, Map.Main/GetGridActivationDistance, Map.Main/PlayerRelocation, MovementInfo/HasMovementFlag, Object/GetGUIDLow, Unit.Main/HandleInterruptsOnMovement, World/getConfig#4, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/LoadMapCellsAround | Unit.Main/TeleportPositionRelocation, Unit.Main/UpdateSplineMovement, WorldSession.MovementHandler/HandleMoverRelocation | — |
| SaveRecallPosition | method | WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | ChatHandler.TeleportCommands/HandleGoHelper, ChatHandler.TeleportCommands/HandleGonameCommand, ChatHandler.TeleportCommands/HandleGroupgoCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, ChatHandler.TeleportCommands/HandleTeleGroupCommand, PlayerBotAI/SpawnNewPlayer | — |
| SendMessageToSet | method | Map.Main/MessageBroadcast, Object/IsInWorld, WorldObject.Object/GetMap, WorldSession.Main/SendPacket | — | — |
| SendMessageToSetInRange | method | Map.Main/MessageDistBroadcast, Object/IsInWorld, WorldObject.Object/GetMap, WorldSession.Main/SendPacket | — | — |
| SendMessageToSetInRange#2 | method | Map.Main/MessageDistBroadcast, Object/IsInWorld, WorldObject.Object/GetMap, WorldSession.Main/SendPacket | — | — |
| SendDirectMessage | method | WorldSession.Main/SendPacket | Camera/ReceivePacket, ChatHandler.DebugCommands/HandleDebugPlayMusicCommand, ChatHandler.DebugCommands/HandleDebugSendNextChannelSpellVisualCommand, ChatHandler.DebugCommands/HandleSendSpellChannelVisualCommand, Creature.Main/OnEnterCombat, Creature.Main/SendAreaSpiritHealerQueryOpcode, game_Group_Group/AddMember, HonorMgr/SendPVPCredit, Map.Main/PlayDirectSoundToMap, ReputationMgr/SendForceReactions, ReputationMgr/SendInitialReputations, ReputationMgr/SendState, ReputationMgr/SendVisible, Spell.Effects/EffectBind, Spell.Main/Delayed, Spell.Main/SendChannelStart, Transport/SendOutOfRangeUpdateToMap, Unit.Main/Kill, Unit.SpellAuras/HandleAuraEmpathy, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/HandleModPossess, Unit.SpellAuras/UpdateAuraDuration, WorldObject.Object/PlayDirectMusic, WorldObject.Object/PlayDirectSound, WorldObject.Object/PlayDistanceSound, WorldObject.Object/SendOutOfRangeUpdateToPlayer | — |
| SendCinematicStart | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4 | ChatHandler.DebugCommands/HandleDebugPlayCinematicCommand, GameObject/Use, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| IsOutdoorOnTransport | method | GameObject/GetDisplayId, GameObject/IsMoTransport, Geometry/GetDistance3D, WorldObject.Object/GetTransport | Spell.Main/CheckCast | — |
| CheckAreaExploreAndOutdoor | method | AreaEntry/GetByAreaFlagAndMap, GridMap/GetAreaFlag, Log.Main/Out, MovementAnticheat/OnExplore, Object/GetGUIDLow, Object/GetUInt32Value, Object/HasFlag, ObjectMgr/GetAreaTrigger, ObjectMgr/GetBaseXP, ObjectMgr/IsPointInAreaTriggerZone, SpellCaster/CastSpell, SpellEntry/IsNeedCastSpellAtFormApply, SpellEntry/IsNeedCastSpellAtOutdoor, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetLevel, Unit.Main/GetShapeshiftForm, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/IsTaxiFlying, Unit.Main/RemoveAurasWithAttribute, World/getConfig, World/getConfig#2, World/getConfig#4, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTerrain, WorldObject.Object/GetTransport, WorldObject.Object/SetUInt32Value | — | — |
| TeamForRace | method | Log.Main/Out | game_Group_Group/LoadMemberFromDB, ObjectMgr/GetPlayerTeamByGUID, Unit.SpellAuras/GetShapeshiftDisplayInfo, WorldSession.CharacterHandler/HandleCharCreateOpcode, WorldSession.MiscHandler/HandleAddFriendOpcode | — |
| GetFactionForRace | method | Log.Main/Out | — | — |
| SetFactionForRace | method | Unit.Main/SetFactionTemplateId | ChatHandler.CharacterCommands/HandleResetStatsOrLevelHelper, Unit.Main/RestoreFaction, Unit.SpellAuras/HandleModCharm | — |
| GetReputationRank | method | ObjectMgr/GetFactionEntry, ReputationMgr/GetRank | CombatBotBaseAI/EquipRandomGearInEmptySlots, Creature.Main/IsTrainerOf, instance_naxxramas.Main/GossipSelect_npc_MasterCraftsmanOmarion, instance_naxxramas.Main/LearnCraftIfCan, ReputationMgr/SetReputation#2, spell_item/OnAfterApply, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, Unit.SpellAuras/HandleForceReaction, WorldSession.ItemHandler/SendListInventory | — |
| CalculateReputationGain | method | Formulas/GetGrayLevel, ObjectMgr/GetRepRewardRate, shared_Util/round_float, Unit.Main/GetLevel, Unit.Main/GetTotalAuraModifier, Unit.Main/GetTotalAuraModifierByMiscValue, World/getConfig#2 | game_Battlegrounds_BattleGround/RewardReputationToTeam, Spell.Effects/EffectReputation | — |
| RewardReputation#2 | method | Object/GetEntry, Object/IsPet, Object/IsPlayer, ObjectMgr/GetFactionEntry, ObjectMgr/GetReputationOnKillEntry, ReputationMgr/GetRank, ReputationMgr/ModifyReputation, Unit.Main/GetLevel, World/GetWowPatch | game_Group_Group/RewardGroupAtKill_helper | — |
| RewardReputation | method | ObjectMgr/GetFactionEntry, QuestDef/GetRewRepSpilloverMask, ReputationMgr/ModifyReputation | — | — |
| GetGuildIdFromDB | method | Database/PQuery, Field/GetUInt32, ObjectGuid/GetCounter, QueryResult/Fetch | ChatHandler.MiscCommands/HandleGuildRankCommand, ChatHandler.MiscCommands/HandleGuildUninviteCommand | guild_member |
| GetRankFromDB | method | Database/PQuery, Field/GetUInt32, ObjectGuid/GetCounter, QueryResult/Fetch | — | guild_member |
| GetZoneIdFromDB | method | Database/PExecute#2, Database/PQuery, Field/GetFloat, Field/GetUInt32, ObjectGuid/GetCounter, ObjectMgr/GetPlayerDataByGUID, QueryResult/Fetch, TerrainManager/GetZoneId | game_Guild_Guild/LoadMembersFromDB | characters |
| GetLevelFromDB | method | Database/PQuery, Field/GetUInt32, ObjectGuid/GetCounter, ObjectMgr/GetPlayerDataByGUID, QueryResult/Fetch | ChatHandler.CharacterCommands/HandleCharacterLevelCommand, ChatHandler.CharacterCommands/HandleLevelUpCommand | characters |
| DismountCheck | method | Aura/GetSpellProto, Spell.Main/CheckCast, Spell.Main/Spell#2, Unit.Main/GetAurasByType, Unit.Main/IsMounted, Unit.Main/RemoveSpellsCausingAura | — | — |
| SetTransport | method | WorldObject.Object/SetTransport | — | — |
| UpdateArea | method | AreaEntry/GetById, World/IsFFAPvPRealm | Unit.Main/TeleportPositionRelocation | — |
| UpdateZone | method | AreaEntry/GetById, Map.Main/GetWeatherSystem, Object/HasFlag, Unit.Main/IsAlive, Unit.Main/IsPvP, Unit.Main/IsTaxiFlying, Weather/FindOrCreateWeather, Weather/SendWeatherUpdateToPlayer, World/getConfig, World/IsFFAPvPRealm, World/IsPvPRealm, WorldObject.Object/GetMap, WorldObject.Object/SetZoneScript, ZoneScriptMgr/HandlePlayerEnterZone, ZoneScriptMgr/HandlePlayerLeaveZone | AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, PartyBotAI/UpdateAI, Unit.Main/TeleportPositionRelocation, WorldSession.MiscHandler/HandleZoneUpdateOpcode | — |
| CheckDuelDistance | method | Map.Main/GetGameObject, Object/GetGUIDLow, Object/GetGuidValue, Unit.Main/CombatStopWithPets, WorldObject.Object/GetMap, WorldObject.Object/GetTransport, WorldObject.Object/IsWithinDistInMap, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| IsOutdoorPvPActive | method | Unit.Main/HasInvisibilityAura, Unit.Main/HasStealthAura, Unit.Main/IsAlive, Unit.Main/IsTaxiFlying, World/IsPvPRealm | Spell.Effects/EffectScriptEffect, ZoneScript/HandleCustomSpell, ZoneScript/HandleKill, ZoneScript/Update | — |
| DuelComplete | method | ByteBuffer/operator<<#3, ByteBuffer/operator<<#7, Map.Main/GetGameObject, Object/GetGuidValue, Object/GetObjectGuid, Object/SetGuidValue, ObjectGuid/ObjectGuid, ObjectGuid/operator==, SpellAuraHolder/GetAuraApplyTime, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/IsReflected, Unit.Main/GetPetGuid, Unit.Main/GetSpellAuraHolderMap, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RemoveGameObject, Unit.Main/ResetExtraAttacks, Unit.SpellAuras/GetId, Unit.SpellAuras/IsPositive, WorldObject.Object/GetMap, WorldObject.Object/SendObjectMessageToSet, WorldObject.Object/SetUInt32Value, WorldPacket/Initialize, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Spell.Effects/EffectDuel, Unit.Main/DealDamage, WorldSession.DuelHandler/HandleDuelCancelledOpcode | — |
| _ApplyItemMods | method | game_Objects_Item/GetProto, game_Objects_Item/IsBroken, Log.Main/Out, Object/GetGUIDLow, Player.StatSystem/UpdateParryPercentage | — | — |
| _ApplyItemBonuses | method | ItemPrototype/IsRangedWeapon, ItemPrototype/IsWeapon, Player.StatSystem/UpdateDamagePhysical#3, Unit.Main/CanModifyStats, Unit.Main/CanUseEquippedWeapon, Unit.Main/HandleStatModifier, Unit.Main/IsInCombat, Unit.Main/SetAttackTime, Unit.Main/SetBaseWeaponDamage, Unit.Main/SetWeaponDamageSchool | — | — |
| _ApplyWeaponDependentAuraMods | method | Unit.Main/CanUseEquippedWeapon, Unit.Main/GetAurasByType | Unit.SpellAuras/HandleAuraModDisarm | — |
| _ApplyWeaponDependentAuraCritMod | method | Aura/GetCastItemGuid, Aura/GetModifier, Aura/GetSpellProto, Aura/IsApplied, game_Objects_Item/IsBroken, game_Objects_Item/IsFitToSpellRequirements, Object/GetObjectGuid, ObjectGuid/operator!, ObjectGuid/operator!=, Unit.Main/CanUseEquippedWeapon | Unit.SpellAuras/HandleAuraModCritPercent, Unit.SpellAuras/HandleShapeshiftBoosts | — |
| _ApplyWeaponDependentAuraDamageMod | method | Aura/GetModifier, Aura/GetSpellProto, game_Objects_Item/IsFitToSpellRequirements, Unit.Main/GetClassMask, Unit.Main/HandleStatModifier | Unit.SpellAuras/HandleModDamageDone, Unit.SpellAuras/HandleModDamagePercentDone | — |
| UpdateDamageDonePercent | method | Aura/GetModifier, Aura/GetSpellProto, Aura/IsApplied, Unit.Main/GetAurasByType, WorldObject.Object/SetFloatValue | Unit.SpellAuras/HandleModDamagePercentDone | — |
| ApplyItemEquipSpell | method | game_Objects_Item/GetProto, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| ApplyEquipSpell | method | Log.Main/Out, Object/GetObjectGuid, ObjectGuid/operator==, SpellAuraHolder/GetCastItemGuid, SpellCaster/CastSpell, SpellEntry/GetErrorAtShapeshiftedCast, Unit.Main/GetShapeshiftForm, Unit.Main/GetSpellAuraHolderBounds, Unit.Main/RemoveAurasDueToItemSpell, Unit.Main/RemoveSingleAuraDueToItemSet | game_Objects_Item/AddItemsSetItem, game_Objects_Item/RemoveItemsSetItem | — |
| UpdateEquipSpellsAtFormChange | method | game_Objects_Item/IsBroken | — | — |
| CastItemCombatSpell | method | game_Objects_Item/ClearEnchantment, game_Objects_Item/GetEnchantmentCharges, game_Objects_Item/GetEnchantmentId, game_Objects_Item/GetProto, game_Objects_Item/SetEnchantmentCharges, Log.Main/Out, shared_Util/roll_chance_f, SpellCaster/CastSpell#2, SpellCaster/HasGCD, SpellEntry/HasEffect, SpellEntry/IsPositiveSpell#4, SpellMgr/GetItemEnchantProcChance, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetExtraAttacks, Unit.Main/GetPPMProcChance | Spell.Main/DoAllEffectOnTarget#3, Unit.AuraProcHandler/HandleDummyAuraProc, Unit.Main/DealMeleeDamage | — |
| CastItemUseSpell | method | game_Objects_Item/GetProto, Log.Main/Out, Spell.Main/prepare, Spell.Main/SetCastItem, Spell.Main/SetClientStarted, Spell.Main/Spell#2, SpellEntry/HasAttribute#3, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/RemoveAurasWithInterruptFlags | CombatBotBaseAI/EquipOrUseNewItem, WorldSession.SpellHandler/HandleUseItemOpcode | — |
| GetItemSetEffect | method | — | game_Objects_Item/AddItemsSetItem, game_Objects_Item/RemoveItemsSetItem | — |
| AddItemSetEffect | method | — | game_Objects_Item/AddItemsSetItem | — |
| RemoveItemSetEffect | method | — | game_Objects_Item/RemoveItemsSetItem | — |
| _RemoveAllItemMods | method | game_Objects_Item/GetProto, game_Objects_Item/IsBroken, game_Objects_Item/RemoveItemsSetItem, Log.Main/Out | Player.StatSystem/_RemoveAllStatBonuses | — |
| _ApplyAllItemMods | method | game_Objects_Item/AddItemsSetItem, game_Objects_Item/GetProto, game_Objects_Item/IsBroken, Log.Main/Out | Player.StatSystem/_ApplyAllStatBonuses | — |
| _ApplyAmmoBonuses | method | Object/GetUInt32Value, ObjectMgr/GetItemPrototype, Player.StatSystem/UpdateDamagePhysical#3, Unit.Main/CanModifyStats | — | — |
| CheckAmmoCompatibility | method | game_Objects_Item/GetProto | — | — |
| RemovedInsignia | method | ByteBuffer/operator<<#7, Object/GetObjectGuid, ObjectAccessor/ConvertCorpseForPlayer, Unit.Main/GetDeathState, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Spell.Effects/EffectSkinPlayerCorpse | — |
| SendLootRelease | method | ByteBuffer/operator<<#7, ObjectGuid/operator<<, WorldPacket/WorldPacket#4 | WorldSession.LootHandler/DoLootRelease, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode | — |
| SendLootError | method | ByteBuffer/operator<<#11, ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4 | WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.LootHandler/HandleLootOpcode | — |
| SendLoot | method | BattleGround/GetPlayerSkinRefLootId, ByteBuffer/operator<<#7, Creature.Main/AI, Creature.Main/GetCreatureInfo, Creature.Main/GetGroupLootRecipient, Creature.Main/GetLootRecipient, Creature.Main/GetRespawnTimeEx, CreatureAI/CanBeLooted, GameObject/GetDBTableGUIDLow, GameObject/getFishLoot, GameObject/GetGOInfo, GameObject/GetGoType, GameObject/getLootState, GameObject/GetOwnerGuid, GameObject/GetRespawnTimeEx, GameObject/IsAtInteractDistance#2, GameObject/isSpawned, GameObject/SetGoState, GameObject/SetLootState, GameObjectInfo/GetLootId, game_Group_Group/GroupLoot, game_Group_Group/MasterLoot, game_Group_Group/NeedBeforeGreed, game_Group_Group/UpdateLooterGuid, game_Objects_Item/GetProto, game_Objects_Item/HasGeneratedLoot, game_Objects_Item/HasGeneratedLootSecondary, game_Objects_Item/SetGeneratedLoot, game_Objects_Item/SetLootState, Group/GetLootMethod, Group/GetTeam, Group/isBGGroup, Log.Main/Out, Loot/AddLooter, Loot/clear, Loot/empty, Loot/IsOriginalLooter, Loot/leaveOnlyQuestItems, Loot/SetTeam, LootMgr/FillLoot, LootMgr/FillNotNormalLootFor, LootMgr/GenerateMoneyLoot, LootMgr/operator<<#2, LootView/LootView, Map.Main/BindToInstanceOrRaid, Map.Main/GetCorpse, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/GetId, Object/GetEntry, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetHigh, ObjectGuid/GetString, ObjectGuid/IsItem, ObjectGuid/operator!=, ObjectGuid/operator<<, ObjectMgr/IsMapLootDisabled, shared_Util/urand, Unit.Main/GetLevel, Unit.Main/IsAlive, Unit.Main/IsFriendlyTo, Unit.Main/RemoveAurasWithInterruptFlags, World/getConfig#2, WorldObject.Object/FindMap, WorldObject.Object/ForceValuesUpdateAtIndex, WorldObject.Object/GetAreaId, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMap, WorldObject.Object/GetZoneId, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value, WorldPacket/WorldPacket#4, WorldSession.LootHandler/DoLootRelease, WorldSession.Main/ProcessAnticheatAction | GameObject/Use, Map.Main/RemoveCorpses, Spell.Effects/EffectDisEnchant, Spell.Effects/EffectPickPocket, Spell.Effects/EffectSkinning, Spell.Effects/EffectSkinPlayerCorpse, Spell.Effects/SendLoot, WorldSession.LootHandler/HandleLootOpcode, WorldSession.SpellHandler/HandleOpenItemOpcode | — |
| SendNotifyLootMoneyRemoved | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | LootMgr/NotifyMoneyRemoved | — |
| SendLootMoneyNotify | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.LootHandler/HandleLootMoneyOpcode | — |
| SendNotifyLootItemRemoved | method | ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | LootMgr/NotifyItemRemoved, LootMgr/NotifyQuestItemRemoved, WorldSession.LootHandler/HandleAutostoreLootItemOpcode | — |
| SendUpdateWorldState | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket, WorldStates/WriteUpdateWorldStatePair | ChatHandler.DebugCommands/HandleDebugSendWorldStateCommand, ChatHandler.HardcodedEvents/UpdateWorldState, npcs_special/GossipHello_npc_kwee_peddlefeet, OutdoorPvPEP/SendRemoveWorldStates, OutdoorPvPSI/SendRemoveWorldStates, scourge_invasion/GossipHello_npc_argent_emissary, ScriptedInstance/DoUpdateWorldState, world_event_wareffort/SendWorldStateUpdateToPlayer, ZoneScript/HandlePlayerEnter, ZoneScript/HandlePlayerLeave, ZoneScript/SendUpdateWorldState, ZoneScript/SendUpdateWorldState#2, ZoneScript/Update | — |
| SendInitWorldStates | method | BattleGround/FillInitialWorldStates, ByteBuffer/operator<<#10, ByteBuffer/operator<<#13, ByteBuffer/wpos, GameEventMgr.Main/IsActiveEvent, Log.Main/Out, ObjectMgr/GetSavedVariable, WorldObject.Object/GetMapId, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket, WorldStates/WriteInitialWorldStatePair, world_event_wareffort/BuildWarEffortWorldStates, ZoneScript/FillInitialWorldStates#2 | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetXPRestBonus | method | Log.Main/Out | — | — |
| ComputeRest | method | Object/GetUInt32Value, World/getConfig#2 | — | — |
| SetBindPoint | method | ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendTalentWipeConfirm | method | ByteBuffer/operator<<#10, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendPetSkillWipeConfirm | method | ByteBuffer/operator<<#10, Object/GetObjectGuid, ObjectGuid/operator<<, Pet.Main/GetResetTalentsCost, Unit.Main/GetPet, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| FindEquipSlot | method | game_Objects_Item/GetAllowedEquipSlots, Unit.Main/GetClass | CombatBotBaseAI/EquipOrUseNewItem | — |
| CanUnequipItems | method | Bag/GetBagSize, game_Objects_Item/GetCount, Object/GetEntry | — | — |
| GetItemCount | method | Bag/GetItemCount, game_Objects_Item/GetCount, Object/GetEntry | ChatHandler.CharacterCommands/HandleAddItemCommand, ChatHandler.CharacterCommands/HandleCharacterHasItemCommand, ChatHandler.CharacterCommands/HandleDeleteItemCommand, ChatHandler.PlayerBotMgr/HandlePartyBotUnequipCommand, WorldSession.ItemHandler/HandleSetAmmoOpcode | — |
| GetItemByGuid | method | Bag/GetBagSize, Bag/GetItemByPos, Object/GetObjectGuid, ObjectGuid/operator== | ChatHandler.DebugCommands/HandleDebugGetItemValueCommand, ChatHandler.DebugCommands/HandleDebugModItemValueCommand, ChatHandler.DebugCommands/HandleDebugSetItemValueCommand, SpellCastTargetsInfo/Update, TradeData/GetItem, TradeData/GetSpellCastItem, Unit.AuraProcHandler/HandleDummyAuraProc, Unit.AuraProcHandler/HandleOverrideClassScriptAuraProc, Unit.AuraProcHandler/HandleProcTriggerSpellAuraProc, Unit.SpellAuras/HandleAuraDummy, Unit.SpellAuras/IsWeaponBuffCoexistableWith, Unit.SpellAuras/ReapplyAffectedPassiveAuras#2, Unit.SpellAuras/TriggerSpell, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.ItemHandler/HandleAutoEquipItemSlotOpcode, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.LootHandler/DoLootRelease, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMoneyOpcode, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.NPCHandler/HandleRepairItemOpcode, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionRenameOpcode, WorldSession.PetitionsHandler/HandlePetitionShowSignOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode | — |
| GetItemByPos#2 | method | — | ChatHandler.CharacterCommands/HandleAddItemCommand, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleDestroyItemOpcode | — |
| GetItemByPos | method | Bag/GetItemByPos | AiBotAI.Bridge/BridgeHandleRepairItems, AiBotAI.Bridge/BridgeHandleSellItems, AiBotAI.Bridge/BridgeSendState, AiBotAI.Loot/ChooseQuestReward, AiBotAI.Loot/TryAutoEquip, AiBotAI.Loot/TryAutoEquipBags, AiBotAI.Main/OnPacketReceived, boss_viscidus/SpellHit, ChatHandler.CharacterCommands/HandleCharacterPremadeSaveGearCommand, ChatHandler.CharacterCommands/HandleResetItemsCommand, ChatHandler.DebugCommands/HandleDebugGetItemStateCommand, CombatBotBaseAI/AddHunterAmmo, CombatBotBaseAI/CastWeaponBuff, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/EquipOrUseNewItem, CombatBotBaseAI/EquipRandomGearInEmptySlots, CombatBotBaseAI/GetHighestHonorRankFromEquippedItems, CombatBotBaseAI/IsWearingShield, CombatBotBaseAI/UseTrinketEffects, custom_creatures/GossipSelect_EnchantNPC, PartyBotAI/CloneFromPlayer, Spell.Effects/EffectDurabilityDamage, Spell.Effects/EffectDurabilityDamagePCT, Spell.Effects/EffectEnchantHeldItem, SpellEntry/CalculateCustomCoefficient, Unit.AuraProcHandler/HandleDummyAuraProc, Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.Main/GetUnitBlockChance, Unit.SpellAuras/HandleRangedAmmoHaste, WorldSession.ItemHandler/HandleAutoBankItemOpcode, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBagItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBankItemOpcode, WorldSession.ItemHandler/HandleBuyItemInSlotOpcode, WorldSession.ItemHandler/HandleDestroyItemOpcode, WorldSession.ItemHandler/HandleReadItemOpcode, WorldSession.ItemHandler/HandleWrapItemOpcode, WorldSession.SpellHandler/HandleOpenItemOpcode, WorldSession.SpellHandler/HandleUseItemOpcode, WorldSession.TradeHandler/HandleSetTradeItemOpcode | — |
| GetWeaponForAttack#2 | method | game_Objects_Item/GetProto, game_Objects_Item/IsBroken, Unit.Main/CanUseEquippedWeapon | Spell.Effects/EffectTriggerSpell, Spell.Main/CheckItems, Spell.Main/TakeAmmo, SpellCaster/GetAPMultiplier, SpellCaster/GetWeaponSkillValue, SpellCaster/MeleeDamageBonusDone, SpellCaster/SpellDamageBonusDone, Unit.Main/HaveOffhandWeapon, Unit.SpellAuras/HandleAuraModCritPercent, Unit.SpellAuras/HandleAuraModDisarm, Unit.SpellAuras/HandleModDamageDone, Unit.SpellAuras/HandleModDamagePercentDone, Unit.SpellAuras/HandleShapeshiftBoosts | — |
| GetWeaponForParry | method | — | Unit.Main/GetUnitParryChance | — |
| CanBeDisarmed | method | — | — | — |
| GetAttackBySlot | method | — | — | — |
| GetHighestKnownArmorProficiency | method | — | CombatBotBaseAI/EquipRandomGearInEmptySlots | — |
| IsInventoryPos | method | — | ChatHandler.CharacterCommands/HandleListItemCommand | — |
| IsEquipmentPos | method | — | ChatHandler.CharacterCommands/HandleListItemCommand, WorldSession.ItemHandler/HandleAutoEquipItemSlotOpcode | — |
| IsBankPos | method | — | ChatHandler.CharacterCommands/HandleListItemCommand, WorldSession.ItemHandler/HandleAutoStoreBagItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBankItemOpcode, WorldSession.ItemHandler/HandleSwapInvItemOpcode, WorldSession.ItemHandler/HandleSwapItem, WorldSession.TradeHandler/HandleSetTradeItemOpcode | — |
| IsBagPos | method | — | game_Objects_Item/CanBeTraded, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBagItemOpcode, WorldSession.ItemHandler/HandleDestroyItemOpcode | — |
| IsValidPos | method | Bag/GetBagSize | ChatHandler.CharacterCommands/HandleItemMoveCommand, WorldSession.ItemHandler/HandleAutoStoreBagItemOpcode, WorldSession.ItemHandler/HandleSplitItemOpcode, WorldSession.ItemHandler/HandleSwapInvItemOpcode, WorldSession.ItemHandler/HandleSwapItem | — |
| HasItemCount | method | Bag/GetBagSize, game_Objects_Item/GetCount, game_Objects_Item/IsInTrade, Object/GetEntry | ChatHandler.CharacterCommands/HandleAddItemCommand, ChatHandler.CharacterCommands/HandleDeleteItemCommand, CombatBotBaseAI/AddAllSpellReagents, Conditions/Evaluate, GameObject/PlayerCanUse, instance_blackrock_spire/AreaTrigger_at_blackrock_spire, instance_dire_maul/OnPlayerEnter, instance_naxxramas.Main/GossipSelect_npc_MasterCraftsmanOmarion, Spell.Effects/EffectScriptEffect, Spell.Main/CheckItems, stranglethorn_vale/OnUse, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector | — |
| HasItemWithIdEquipped | method | game_Objects_Item/GetCount, Object/GetEntry | Conditions/Evaluate | — |
| _CanTakeMoreSimilarItems | method | ObjectMgr/GetItemPrototype | — | — |
| _CanStoreItem_InSpecificSlot | method | Bag/IsEmpty, game_Objects_Item/CanBeMergedPartlyWith, game_Objects_Item/GetCount, game_Objects_Item/GetProto, game_Objects_Item/IsBag, game_Objects_Item/ItemCanGoIntoBag, ItemPosCount/ItemPosCount, Log.Main/Out, WorldSession.Main/ProcessAnticheatAction | — | — |
| _CanStoreItem_InBag | method | Bag/GetBagSize, Bag/IsEmpty, game_Objects_Item/CanBeMergedPartlyWith, game_Objects_Item/GetCount, game_Objects_Item/GetProto, game_Objects_Item/IsBag, game_Objects_Item/ItemCanGoIntoBag, ItemPosCount/ItemPosCount, ItemPrototype/GetMaxStackSize, WorldSession.Main/ProcessAnticheatAction | — | — |
| _CanStoreItem_InInventorySlots | method | Bag/IsEmpty, game_Objects_Item/CanBeMergedPartlyWith, game_Objects_Item/GetCount, game_Objects_Item/IsBag, ItemPosCount/ItemPosCount, ItemPrototype/GetMaxStackSize, WorldSession.Main/ProcessAnticheatAction | — | — |
| _CanStoreItem | method | Bag/IsEmpty, game_Objects_Item/HasTemporaryLoot, game_Objects_Item/IsBag, game_Objects_Item/IsBindedNotWith, Log.Main/Out, ObjectMgr/GetItemPrototype | — | — |
| CanStoreItems | method | Bag/GetBagSize, Bag/IsEmpty, game_Objects_Item/CanBeMergedPartlyWith, game_Objects_Item/GetCount, game_Objects_Item/GetProto, game_Objects_Item/HasTemporaryLoot, game_Objects_Item/IsBag, game_Objects_Item/IsBindedNotWith, game_Objects_Item/IsInTrade, game_Objects_Item/ItemCanGoIntoBag, ItemPrototype/GetMaxStackSize, Log.Main/Out, Object/GetEntry, WorldSession.Main/ProcessAnticheatAction | WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| CanEquipNewItem | method | ObjectMgr/GetItemPrototype | — | — |
| CanEquipItem | method | game_Objects_Item/GetProto | AiBotAI.Loot/TryAutoEquip, Spell.Effects/EffectSummonChangeItem, WorldSession.ItemHandler/HandleAutoEquipItemOpcode | — |
| CanEquipItem#2 | method | Errors/PrintStacktraceAndThrow, game_Objects_Item/GetCount, game_Objects_Item/GetProto, game_Objects_Item/GetSlot, game_Objects_Item/HasTemporaryLoot, game_Objects_Item/IsBindedNotWith, ItemPrototype/CanChangeEquipStateInCombat, Log.Main/Out, Object/GetEntry, Unit.Main/HasUnitState, Unit.Main/IsInCombat, WorldSession.Main/IsLogingOut | — | — |
| CanUnequipItem | method | Bag/IsEmpty, game_Objects_Item/GetCount, game_Objects_Item/GetProto, game_Objects_Item/HasTemporaryLoot, game_Objects_Item/IsBag, ItemPrototype/CanChangeEquipStateInCombat, Log.Main/Out, Object/GetEntry, Object/HasFlag, Unit.Main/IsInCombat, WorldSession.Main/IsLogingOut | game_Objects_Item/CanBeTraded, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBagItemOpcode, WorldSession.ItemHandler/HandleDestroyItemOpcode | — |
| CanBankItem | method | Bag/IsEmpty, game_Objects_Item/GetCount, game_Objects_Item/GetProto, game_Objects_Item/HasTemporaryLoot, game_Objects_Item/IsBag, game_Objects_Item/IsBindedNotWith, Log.Main/Out, Object/GetEntry | Spell.Effects/EffectSummonChangeItem, WorldSession.ItemHandler/HandleAutoBankItemOpcode, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBankItemOpcode | — |
| CanUseItem | method | game_Objects_Item/GetProto, game_Objects_Item/IsBindedNotWith, Log.Main/Out, Object/GetEntry, Unit.Main/IsAlive | AuctionHouseMgr/BuildListAuctionItems, WorldSession.ItemHandler/HandleReadItemOpcode, WorldSession.SpellHandler/HandleUseItemOpcode | — |
| CanUseItem#2 | method | game_Objects_Item/GetProficiencySkill, HonorMgr/GetHighestRank, HonorMgr/GetRank, Unit.Main/GetClassMask, Unit.Main/GetLevel, Unit.Main/GetRaceMask, World/getConfig, World/GetWowPatch | AiBotAI.Loot/ChooseQuestReward, ChatHandler.CharacterCommands/HandleLearnAllItemsCommand, ChatHandler.LookupCommands/ShowItemListHelper, CombatBotBaseAI/EquipRandomGearInEmptySlots, game_Group_Group/StartLootRoll | — |
| CanUseAmmo | method | Log.Main/Out, ObjectMgr/GetItemPrototype, Unit.Main/IsAlive | CombatBotBaseAI/AddHunterAmmo | — |
| SetAmmo | method | Object/GetUInt32Value, WorldObject.Object/SetUInt32Value | CombatBotBaseAI/AddHunterAmmo, WorldSession.ItemHandler/HandleSetAmmoOpcode | — |
| RemoveAmmo | method | Player.StatSystem/UpdateDamagePhysical#3, Unit.Main/CanModifyStats, WorldObject.Object/SetUInt32Value | WorldSession.ItemHandler/HandleSetAmmoOpcode | — |
| StoreNewItem | method | game_Objects_Item/CreateItem, game_Objects_Item/SetItemRandomProperties, Object/GetObjectGuid | ChatHandler.CharacterCommands/HandleAddItemCommand, ChatHandler.CharacterCommands/HandleAddItemSetCommand, CombatBotBaseAI/AddItemToInventory, darkshore/QuestAcceptGO_beached_sea, game_Battlegrounds_BattleGround/RewardItem, game_Group_Group/CountSingleLooterRoll, game_Group_Group/CountTheRoll, Pet.Main/Unsummon, Spell.Effects/DoCreateItem, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, Unit.SpellAuras/HandleChannelDeathItem, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode | — |
| StoreItem | method | — | AiBotAI.Loot/TryAutoEquip, AiBotAI.Loot/TryAutoEquipBags, Spell.Effects/EffectSummonChangeItem, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBagItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBankItemOpcode, WorldSession.ItemHandler/HandleBuybackItem, WorldSession.MailHandler/HandleMailCreateTextItem | — |
| _StoreItem | method | Bag/StoreItem, game_Objects_Item/CloneItem, game_Objects_Item/GetCount, game_Objects_Item/GetProto, game_Objects_Item/RemoveFromWorld, game_Objects_Item/SetBinding, game_Objects_Item/SetContainer, game_Objects_Item/SetCount, game_Objects_Item/SetOwnerGuid, game_Objects_Item/SetSlot, game_Objects_Item/SetState, Log.Main/Out, Object/AddToWorld, Object/GetEntry, Object/GetObjectGuid, Object/IsInWorld, Object/SetGuidValue, WorldObject.Object/DestroyForPlayer, WorldObject.Object/SendCreateUpdateToPlayer | — | — |
| EquipNewItem | method | game_Objects_Item/CreateItem, Object/GetObjectGuid | — | — |
| EquipItem | method | game_Objects_Item/AddItemsSetItem, game_Objects_Item/GetCount, game_Objects_Item/GetProto, game_Objects_Item/RemoveFromWorld, game_Objects_Item/SetCount, game_Objects_Item/SetOwnerGuid, game_Objects_Item/SetState, Log.Main/Out, Object/AddToWorld, Object/GetObjectGuid, Object/IsInWorld, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetClass, Unit.Main/IsAlive, Unit.Main/IsInCombat, WorldObject.Object/DestroyForPlayer, WorldObject.Object/SendCreateUpdateToPlayer | AiBotAI.Loot/TryAutoEquip, AiBotAI.Loot/TryAutoEquipBags, CombatBotBaseAI/EquipOrUseNewItem, Spell.Effects/EffectSummonChangeItem, WorldSession.ItemHandler/HandleAutoEquipItemOpcode | — |
| QuickEquipItem | method | Object/AddToWorld, Object/IsInWorld, WorldObject.Object/SendCreateUpdateToPlayer | — | — |
| SetVisibleItemSlot | method | game_Objects_Item/GetEnchantmentId, game_Objects_Item/GetItemRandomPropertyId, game_Objects_Item/GetItemSuffixFactor, Object/GetEntry, Object/GetGuidValue, Object/SetGuidValue, Object/SetInt16Value, ObjectGuid/ObjectGuid, WorldObject.Object/SetUInt32Value | — | — |
| VisualizeItem | method | game_Objects_Item/GetProto, game_Objects_Item/SetBinding, game_Objects_Item/SetContainer, game_Objects_Item/SetSlot, game_Objects_Item/SetState, Log.Main/Out, Object/GetEntry, Object/GetObjectGuid, Object/SetGuidValue | — | — |
| RemoveItem | method | Bag/RemoveItem, game_Objects_Item/ClearEnchantment, game_Objects_Item/GetProto, game_Objects_Item/RemoveItemsSetItem, game_Objects_Item/SetSlot, Log.Main/Out, Object/GetEntry, Object/IsInWorld, Object/SetGuidValue, ObjectGuid/ObjectGuid, Unit.Main/ResetExtraAttacks, WorldObject.Object/SendCreateUpdateToPlayer | AiBotAI.Loot/TryAutoEquip, AiBotAI.Loot/TryAutoEquipBags, CombatBotBaseAI/EquipOrUseNewItem, WorldSession.ItemHandler/HandleAutoBankItemOpcode, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBagItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBankItemOpcode, WorldSession.ItemHandler/HandleSellItemOpcode | — |
| MoveItemFromInventory | method | game_Objects_Item/GetCount, game_Objects_Item/RemoveFromUpdateQueueOf, game_Objects_Item/RemoveFromWorld, Object/GetEntry, Object/IsInWorld, WorldObject.Object/DestroyForPlayer | WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| MoveItemToInventory | method | game_Objects_Item/GetCount, game_Objects_Item/GetOwnerGuid, game_Objects_Item/SetOwnerGuid, game_Objects_Item/SetState, Object/GetEntry, Object/GetObjectGuid, ObjectGuid/operator!= | WorldSession.MailHandler/HandleMailTakeItem, WorldSession.TradeHandler/MoveItems | — |
| DestroyItem | method | Bag/IsEmpty, Bag/RemoveItem, Database/CreateStatement, game_Objects_Item/GetCount, game_Objects_Item/GetEnchantmentId, game_Objects_Item/GetProto, game_Objects_Item/IsBag, game_Objects_Item/IsCharter, game_Objects_Item/IsEquipped, game_Objects_Item/RemoveFromWorld, game_Objects_Item/RemoveItemsSetItem, game_Objects_Item/SetSlot, game_Objects_Item/SetState, GuildMgr/DeletePetition, GuildMgr/GetPetitionById, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Object/HasFlag, Object/IsInWorld, Object/SetGuidValue, ObjectGuid/ObjectGuid, SqlStatementID/SqlStatementID, WorldObject.Object/DestroyForPlayer, WorldSession.Main/ProcessAnticheatAction | AiBotAI.Bridge/BridgeHandleSellItems, ChatHandler.CharacterCommands/HandleResetItemsCommand, CombatBotBaseAI/AddHunterAmmo, CombatBotBaseAI/DoCastSpell, CombatBotBaseAI/EquipOrUseNewItem, game_Objects_Item/UpdateDuration, Spell.Effects/EffectSummonChangeItem, WorldSession.ItemHandler/HandleDestroyItemOpcode, WorldSession.LootHandler/DoLootRelease, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode, WorldSession.SpellHandler/HandleOpenItemOpcode | character_gifts |
| DestroyItemCount#2 | method | Bag/GetBagSize, Bag/GetItemByPos, game_Objects_Item/GetCount, game_Objects_Item/IsInTrade, game_Objects_Item/SetCount, game_Objects_Item/SetState, Log.Main/Out, Object/GetEntry, Object/IsInWorld, WorldObject.Object/SendCreateUpdateToPlayer | ChatHandler.CharacterCommands/HandleAddItemCommand, ChatHandler.CharacterCommands/HandleCleanCharactersItemsCommand, ChatHandler.CharacterCommands/HandleDeleteItemCommand, ChatHandler.PlayerBotMgr/HandlePartyBotUnequipCommand, instance_dire_maul/OnPlayerEnter, Map.ScriptCommands/ScriptCommand_RemoveItem, scourge_invasion/OnScriptEventHappened#3, Spell.Main/TakeAmmo, Spell.Main/TakeReagents | — |
| DestroyEquippedItem | method | game_Objects_Item/IsInTrade, Object/GetEntry | — | — |
| DestroyZoneLimitedItem | method | Bag/GetBagSize, Bag/GetItemByPos, game_Objects_Item/IsLimitedToAnotherMapOrZone, Log.Main/Out, WorldObject.Object/GetMapId | — | — |
| DestroyConjuredItems | method | Bag/GetBagSize, Bag/GetItemByPos, game_Objects_Item/IsConjuredConsumable, Log.Main/Out | — | — |
| DestroyItemCount | method | game_Objects_Item/GetBagSlot, game_Objects_Item/GetCount, game_Objects_Item/GetSlot, game_Objects_Item/SetCount, game_Objects_Item/SetState, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Object/IsInWorld, WorldObject.Object/SendCreateUpdateToPlayer | Spell.Effects/EffectFeedPet, Spell.Main/TakeAmmo, Spell.Main/TakeCastItem, WorldSession.ItemHandler/HandleDestroyItemOpcode, WorldSession.ItemHandler/HandleWrapItemOpcode | — |
| SplitItem | method | game_Objects_Item/CloneItem, game_Objects_Item/GetCount, game_Objects_Item/HasGeneratedLoot, game_Objects_Item/SetCount, game_Objects_Item/SetState, Log.Main/Out, Object/GetEntry, Object/GetObjectGuid, Object/IsInWorld, TradeData/GetTradeSlotForItem, WorldObject.Object/SendCreateUpdateToPlayer, WorldSession.Main/ProcessAnticheatAction | WorldSession.ItemHandler/HandleSplitItemOpcode | — |
| SwapItem | method | Bag/GetBagSize, Bag/GetItemByPos, Bag/IsEmpty, Bag/RemoveItem, Bag/StoreItem, game_Objects_Item/GetCount, game_Objects_Item/GetProto, game_Objects_Item/HasGeneratedLoot, game_Objects_Item/IsBag, game_Objects_Item/ItemCanGoIntoBag, game_Objects_Item/SetCount, game_Objects_Item/SetState, ItemPrototype/GetMaxStackSize, Log.Main/Out, Object/GetEntry, Object/IsInWorld, Unit.Main/IsAlive, WorldObject.Object/SendCreateUpdateToPlayer, WorldSession.LootHandler/DoLootRelease, WorldSession.Main/ProcessAnticheatAction | ChatHandler.CharacterCommands/HandleItemMoveCommand, WorldSession.ItemHandler/HandleAutoEquipItemSlotOpcode, WorldSession.ItemHandler/HandleSwapInvItemOpcode, WorldSession.ItemHandler/HandleSwapItem | — |
| AddItemToBuyBackSlot | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, Object/GetEntry, Object/GetObjectGuid, Object/GetUInt32Value, Object/SetGuidValue, WorldObject.Object/SetUInt32Value | WorldSession.ItemHandler/HandleSellItemOpcode | — |
| GetItemFromBuyBackSlot | method | Log.Main/Out | WorldSession.ItemHandler/HandleBuybackItem | — |
| RemoveItemFromBuyBackSlot | method | game_Objects_Item/RemoveFromWorld, game_Objects_Item/SetState, Log.Main/Out, Object/SetGuidValue, ObjectGuid/ObjectGuid, WorldObject.Object/SetUInt32Value | WorldSession.ItemHandler/HandleBuybackItem | — |
| SendEquipError | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, game_Objects_Item/GetProto, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectGuid/operator<<, ObjectMgr/GetItemPrototype, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.CharacterCommands/HandleAddItemSetCommand, ChatHandler.DebugCommands/HandleDebugSendEquipErrorCommand, game_Group_Group/CountSingleLooterRoll, game_Group_Group/CountTheRoll, Spell.Effects/DoCreateItem, Spell.Main/CheckItems, spell_warlock/OnCheckCast#2, Unit.SpellAuras/HandleChannelDeathItem, WorldSession.ItemHandler/HandleAutoBankItemOpcode, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBagItemOpcode, WorldSession.ItemHandler/HandleAutoStoreBankItemOpcode, WorldSession.ItemHandler/HandleBuybackItem, WorldSession.ItemHandler/HandleDestroyItemOpcode, WorldSession.ItemHandler/HandleReadItemOpcode, WorldSession.ItemHandler/HandleSetAmmoOpcode, WorldSession.ItemHandler/HandleSplitItemOpcode, WorldSession.ItemHandler/HandleSwapInvItemOpcode, WorldSession.ItemHandler/HandleSwapItem, WorldSession.ItemHandler/HandleWrapItemOpcode, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode, WorldSession.SpellHandler/HandleOpenItemOpcode, WorldSession.SpellHandler/HandleUseItemOpcode | — |
| SendOpenContainer | method | ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.DebugCommands/HandleDebugSendOpenBagCommand, WorldSession.ItemHandler/HandleAutoEquipItemOpcode | — |
| SendBuyError | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.DebugCommands/HandleDebugSendBuyErrorCommand, WorldSession.ItemHandler/HandleBuybackItem, WorldSession.PetHandler/HandlePetUnlearnOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode | — |
| SendSellError | method | ByteBuffer/operator<<#7, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.DebugCommands/HandleDebugSendSellErrorCommand, WorldSession.ItemHandler/HandleBuybackItem, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.ItemHandler/SendListInventory | — |
| GetTrader | method | TradeData/GetTrader | Spell.Main/CheckCast, WorldSession.TradeHandler/MoveItems | — |
| TradeCancel | method | TradeData/GetTrader, WorldSession.TradeHandler/SendCancelTrade | WorldSession.TradeHandler/HandleBusyTradeOpcode, WorldSession.TradeHandler/HandleCancelTradeOpcode, WorldSession.TradeHandler/HandleIgnoreTradeOpcode | — |
| UpdateItemDuration | method | game_Objects_Item/GetProto, game_Objects_Item/UpdateDuration, Log.Main/Out | — | — |
| UpdateEnchantTime | method | Errors/PrintStacktraceAndThrow, game_Objects_Item/ClearEnchantment, game_Objects_Item/GetEnchantmentId | — | — |
| AddEnchantmentDurations | method | game_Objects_Item/GetEnchantmentDuration, game_Objects_Item/GetEnchantmentId | — | — |
| RemoveEnchantmentDurations | method | game_Objects_Item/SetEnchantmentDuration | — | — |
| RemoveAllEnchantments | method | Bag/GetBagSize, Bag/GetItemByPos, game_Objects_Item/ClearEnchantment, game_Objects_Item/GetEnchantmentId | — | — |
| AddEnchantmentDuration | method | EnchantDuration/EnchantDuration, game_Objects_Item/SetEnchantmentDuration, Object/GetObjectGuid, WorldSession.ItemHandler/SendItemEnchantTimeUpdate | — | — |
| ApplyEnchantment#2 | method | — | — | — |
| ApplyEnchantment | method | game_Objects_Item/GetEnchantmentDuration, game_Objects_Item/GetEnchantmentId, game_Objects_Item/GetProto, game_Objects_Item/GetSlot, game_Objects_Item/IsBroken, game_Objects_Item/IsEquipped, Log.Main/Out, SpellCaster/CastSpell#2, Unit.Main/GetClass, Unit.Main/HandleStatModifier, Unit.Main/RemoveAurasDueToItemSpell, WorldObject.Object/SetUInt32Value | Spell.Effects/EffectEnchantHeldItem, Spell.Effects/EffectEnchantItemPerm, Spell.Effects/EffectEnchantItemTmp, Unit.SpellAuras/TriggerSpell | — |
| BuildEnchantmentLog | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Object/GetObjectGuid, ObjectGuid/operator<< | — | — |
| SendEnchantmentLog | method | ObjectGuid/IsEmpty, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | game_Objects_Item/ClearEnchantment, game_Objects_Item/SetEnchantment | — |
| SendEnchantmentDurations | method | Object/GetObjectGuid, WorldSession.ItemHandler/SendItemEnchantTimeUpdate | — | — |
| SendItemDurations | method | game_Objects_Item/SendTimeUpdate | — | — |
| SendNewItem | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, game_Group_Group/BroadcastPacket, game_Objects_Item/GetBagSlot, game_Objects_Item/GetCount, game_Objects_Item/GetItemRandomPropertyId, game_Objects_Item/GetItemSuffixFactor, game_Objects_Item/GetSlot, Object/GetEntry, Object/GetObjectGuid, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.CharacterCommands/HandleAddItemCommand, ChatHandler.CharacterCommands/HandleAddItemSetCommand, ChatHandler.CharacterCommands/HandleGroupAddItemCommand, darkshore/QuestAcceptGO_beached_sea, game_Battlegrounds_BattleGround/RewardItem, Map.ScriptCommands/ScriptCommand_CreateItem, Pet.Main/Unsummon, Spell.Effects/DoCreateItem, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, Unit.SpellAuras/HandleChannelDeathItem, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode | — |
| PrepareGossipMenu | method | BroadcastText/GetText, Conditions/IsConditionSatisfied, Creature.Main/CanInteractWithBattleMaster, Creature.Main/CanTrainAndResetTalentsOf, Creature.Main/GetCreatureInfo, Creature.Main/GetVendorItems, Creature.Main/GetVendorTemplateItems, Creature.Main/IsTrainerOf, GameObject/GetGOInfo, GameObject/GetGoType, GossipDef/AddGossipMenuItemData, GossipDef/AddMenuItem#2, GossipDef/ClearMenus, GossipDef/SetDiscoveredNode, GossipDef/SetMenuId, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Object/GetObjectGuid, Object/GetTypeId, Object/GetUInt32Value, Object/HasFlag, Object/IsCreature, Object/IsGameObject, Object/ToGameObject, ObjectMgr/GetBroadcastTextLocale, ObjectMgr/GetGossipMenuItemsLocale, ObjectMgr/GetGossipMenuItemsMapBounds, Pet.Main/GetPetType, PlayerMenu/GetGossipMenu, Unit.Main/GetClass, Unit.Main/GetGender, Unit.Main/GetPet, Unit.Main/IsDead, VendorItemData/Empty, WorldObject.Object/GetDefaultGossipMenuId, WorldObject.Object/GetMap, WorldSession.Main/GetMangosString, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.TaxiHandler/SendLearnNewTaxiNode | boss_celebras_the_cursed/UpdateEscortAI, GameObject/Use, WorldSession.NPCHandler/HandleGossipHelloOpcode, WorldSession.QuestHandler/HandleQuestgiverHelloOpcode | — |
| SendPreparedGossip | method | GossipDef/Empty, GossipDef/GetMenuId, GossipDef/IsJustDiscoveredNode, GossipDef/SendGossipMenu, Object/GetObjectGuid, Object/GetTypeId, Object/HasFlag, PlayerMenu/GetGossipMenu, PlayerMenu/GetQuestMenu, QuestMenu/Empty | boss_celebras_the_cursed/UpdateEscortAI, GameObject/Use, WorldSession.NPCHandler/HandleGossipHelloOpcode, WorldSession.QuestHandler/HandleQuestgiverHelloOpcode | — |
| OnGossipSelect | method | BattleGroundMgr/GetBattleMasterBG, GossipDef/CloseGossip, GossipDef/GetItem, GossipDef/GetItemData, GossipDef/MenuItemCount, GossipDef/SendPointOfInterest#2, Log.Main/Out, Map.Main/ScriptsStart, Object/GetEntry, Object/GetGUIDLow, Object/GetObjectGuid, Object/GetTypeId, PlayerMenu/GetGossipMenu, SpellCaster/CastSpell#2, Unit.Main/IsDead, WorldObject.Object/GetMap, WorldSession.AuctionHouseHandler/SendAuctionHello, WorldSession.BattleGroundHandler/SendBattleGroundList, WorldSession.ItemHandler/SendListInventory, WorldSession.NPCHandler/SendShowBank, WorldSession.NPCHandler/SendStablePet, WorldSession.NPCHandler/SendTabardVendorActivate, WorldSession.NPCHandler/SendTrainerList, WorldSession.PetitionsHandler/SendPetitionShowList, WorldSession.TaxiHandler/SendTaxiMenu | WorldSession.NPCHandler/HandleGossipSelectOptionOpcode | — |
| GetGossipTextId | method | Object/GetGUIDLow, Object/IsCreature, Object/IsPet, ObjectMgr/GetNpcGossip | burning_steppes/GossipHello_npc_klinfran, custom_creatures/GossipHello_EnchantNPC, custom_creatures/GossipHello_PremadeGearNPC, custom_creatures/GossipHello_PremadeSpecNPC, custom_creatures/GossipHello_ProfessionNPC, darkshore/GossipHello_npc_threshwackonator, dustwallow_marsh/GossipHello_npc_cassa_crimsonwing, gnomeregan/GossipHello_npc_blastmaster_emi_shortfuse, silithus/GossipHello_npc_Krug_SkullSplit, silithus/GossipSelect_npc_Krug_SkullSplit, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, ungoro_crater/GossipHello_npc_simone_the_inconspicuous | — |
| GetGossipTextId#2 | method | Conditions/IsConditionSatisfied, Map.Main/ScriptsStart, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectMgr/GetGossipMenusMapBounds, WorldObject.Object/GetMap | — | — |
| PrepareQuestMenu | method | Errors/PrintStacktraceAndThrow, GossipDef/AddMenuItem#6, GossipDef/ClearMenu#2, Map.Main/GetAnyTypeCreature, Map.Main/GetGameObject, MapManager/FindMap, Object/GetEntry, Object/IsInWorld, ObjectMgr/GetCreatureQuestInvolvedRelationsMapBounds, ObjectMgr/GetCreatureQuestRelationsMapBounds, ObjectMgr/GetGOQuestInvolvedRelationsMapBounds, ObjectMgr/GetGOQuestRelationsMapBounds, ObjectMgr/GetQuestTemplate, PlayerMenu/GetQuestMenu, QuestDef/IsActive, QuestDef/IsAutoComplete, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMap, WorldObject.Object/GetMapId | blackrock_depths/GossipHello_npc_mistress_nagmara, boss_vaelastrasz/GossipHello_boss_vael, dustwallow_marsh/GossipHello_npc_lady_jaina_proudmoore, instance_dire_maul/GossipHello_boss_kromcrush, instance_dire_maul/GossipHello_npc_knot_thimblejack, quest_stormwind_rendezvous/GossipHello_npc_reginald_windsor, searing_gorge/GossipHello_npc_dying_archaeologist, silithus/GOHello_scarab_gong, silithus/GossipHello_npc_Krug_SkullSplit, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, wetlands/GossipHello_npc_mikhail, world_event_wareffort/GossipHello_npc_AQwar_collector | — |
| SendPreparedQuest | method | BroadcastText/GetText, Creature.Main/GetDefaultGossipMenuId, GossipDef/SendQuestGiverQuestDetails, GossipDef/SendQuestGiverQuestList, GossipDef/SendQuestGiverRequestItems, Map.Main/GetAnyTypeCreature, ObjectMgr/GetBroadcastTextLocale, ObjectMgr/GetNpcText, ObjectMgr/GetQuestTemplate, PlayerMenu/GetQuestMenu, QuestDef/IsRepeatable, QuestMenu/Empty, QuestMenu/GetItem, QuestMenu/MenuItemCount, Unit.Main/GetGender, WorldObject.Object/GetMap, WorldSession.Main/GetSessionDbLocaleIndex | dustwallow_marsh/GossipHello_npc_lady_jaina_proudmoore, silithus/GOHello_scarab_gong, wetlands/GossipHello_npc_mikhail | — |
| IsActiveQuest | method | — | SpellMgr/IsFitToRequirements, WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| IsCurrentQuest | method | — | Conditions/Evaluate, quest_stormwind_rendezvous/AreaTrigger_at_stormwind_gates, WorldSession.QuestHandler/HandleQuestConfirmAccept | — |
| GetNextQuest | method | Errors/PrintStacktraceAndThrow, Map.Main/GetAnyTypeCreature, Map.Main/GetGameObject, MapManager/FindMap, Object/GetEntry, Object/IsInWorld, ObjectMgr/GetCreatureQuestRelationsMapBounds, ObjectMgr/GetGOQuestRelationsMapBounds, ObjectMgr/GetQuestTemplate, QuestDef/GetNextQuestInChain, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMap, WorldObject.Object/GetMapId | WorldSession.QuestHandler/HandleQuestgiverChooseRewardOpcode | — |
| CanSeeStartQuest | method | QuestDef/GetMinLevel, QuestDef/IsActive, Unit.Main/GetLevel, World/getConfig#3 | WorldObject.Object/BuildValuesUpdate, WorldSession.QuestHandler/GetDialogStatus | — |
| CanTakeQuest | method | QuestDef/GetMaxLevel, QuestDef/IsActive, Unit.Main/GetLevel | AiBotAI.Bridge/BridgeHandleQuestInteract, Conditions/Evaluate, GameObject/ActivateToQuest, WorldSession.QuestHandler/GetDialogStatus, WorldSession.QuestHandler/HandlePushQuestToParty, WorldSession.QuestHandler/HandleQuestConfirmAccept, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode | — |
| CanAddQuest | method | — | AiBotAI.Bridge/BridgeHandleQuestInteract, ChatHandler.CharacterCommands/HandleQuestAddCommand, WorldSession.QuestHandler/HandleQuestConfirmAccept, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode | — |
| CanCompleteQuest | method | ObjectMgr/GetQuestTemplate, QuestDef/GetRepObjectiveFaction, QuestDef/GetRepObjectiveValue, QuestDef/GetRewOrReqMoney, QuestDef/HasQuestFlag, QuestDef/HasSpecialFlag, QuestDef/IsAutoComplete, ReputationMgr/GetReputation#2 | ChatHandler.CharacterCommands/HandleQuestAddCommand, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, WorldSession.QuestHandler/HandleQuestgiverRequestRewardOpcode | — |
| CanCompleteRepeatableQuest | method | QuestDef/HasSpecialFlag | WorldSession.QuestHandler/HandleQuestgiverCompleteQuest | — |
| CanRewardQuest | method | Object/HasFlag, QuestDef/GetQuestId, QuestDef/GetRewOrReqMoney, QuestDef/HasSpecialFlag, QuestDef/IsAutoComplete, WorldSession.Main/SendPlayTimeWarning | WorldObject.Object/BuildValuesUpdate, WorldSession.QuestHandler/HandleQuestgiverCompleteQuest | — |
| CanRewardQuest#2 | method | QuestDef/GetQuestId, QuestDef/GetRewChoiceItemsCount, QuestDef/GetRewItemsCount | AiBotAI.Bridge/BridgeHandleQuestInteract, WorldSession.QuestHandler/HandleQuestgiverChooseRewardOpcode | — |
| CountFreeInventorySlots | method | Bag/GetBagSize, game_Objects_Item/GetProto | — | — |
| SendPetTameFailure | method | ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Spell.Main/CheckCast | — |
| AddQuest | method | Errors/PrintStacktraceAndThrow, game_Objects_Item/GetBagSlot, game_Objects_Item/GetSlot, Map.Main/ScriptsStart, Object/GetEntry, Object/GetObjectGuid, Object/GetTypeId, Object/IsType, Object/ToWorldObject, ObjectMgr/GetFactionEntry, QuestDef/GetLimitTime, QuestDef/GetQuestId, QuestDef/GetQuestStartScript, QuestDef/GetRepObjectiveFaction, QuestDef/GetSrcItemId, QuestDef/GetType, QuestDef/HasSpecialFlag, ReputationMgr/SetVisible#2, ScriptMgr/OnQuestAccept, ScriptMgr/OnQuestAccept#2, SpellCaster/CastSpell#2, SpellMgr/GetSpellAreaForQuestMapBounds, SpellMgr/Instance, SpellMgr/IsFitToRequirements, Unit.Main/HasAura, WorldObject.Object/GetMap, WorldObject.Object/GetZoneAndAreaId | AiBotAI.Bridge/BridgeHandleQuestInteract, ChatHandler.CharacterCommands/HandleQuestAddCommand, WorldSession.QuestHandler/HandleQuestConfirmAccept, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode | — |
| FullQuestComplete | method | ObjectGuid/ObjectGuid, ObjectMgr/GetCreatureTemplate, ObjectMgr/GetFactionEntry, ObjectMgr/GetQuestTemplate, QuestDef/GetRepObjectiveFaction, QuestDef/GetRepObjectiveValue, QuestDef/GetRewOrReqMoney, ReputationMgr/GetReputation#2, ReputationMgr/SetReputation | BattleGroundAV/CompleteQuestForAll, ChatHandler.CharacterCommands/HandleQuestCompleteCommand | — |
| CompleteQuest | method | ObjectMgr/GetQuestTemplate, QuestDef/HasQuestFlag | AiBotAI.Bridge/BridgeHandleQuestInteract, ChatHandler.CharacterCommands/HandleQuestAddCommand, dustwallow_marsh/AreaTrigger_at_sentry_point, loch_modan/AreaTrigger_at_huldar_miran, quest_stormwind_rendezvous/CompleteQuest, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, WorldSession.QuestHandler/HandleQuestgiverRequestRewardOpcode | — |
| IncompleteQuest | method | — | — | — |
| RemoveQuest | method | ObjectMgr/GetQuestTemplate | ChatHandler.CharacterCommands/HandleQuestRemoveCommand | — |
| RemoveQuestAtSlot | method | ObjectMgr/GetItemPrototype, ObjectMgr/GetQuestTemplate, QuestDef/HasSpecialFlag | WorldSession.QuestHandler/HandleQuestLogRemoveQuest | — |
| RewardQuest | method | BattleGround/GetTypeID, BattleGroundAV/HandleQuestComplete, game_Mail_Mail/MailDraft#2, game_Mail_Mail/MailReceiver, game_Mail_Mail/MailSender#2, game_Mail_Mail/MailSender#4, game_Mail_Mail/SendMailTo, game_Objects_Item/GenerateItemRandomPropertyId, MailDraft/SetMoney, Map.Main/ScriptsStart, Object/GetObjectGuid, Object/GetTypeId, Object/ToUnit, ObjectMgr/GetCreatureQuestRelationsMap, QuestDef/GetQuestCompleteScript, QuestDef/GetQuestId, QuestDef/GetRewChoiceItemsCount, QuestDef/GetRewItemsCount, QuestDef/GetRewMailDelaySecs, QuestDef/GetRewMailMoney, QuestDef/GetRewMailTemplateId, QuestDef/GetRewMoneyMaxLevelAtComplete, QuestDef/GetRewOrReqMoney, QuestDef/GetRewSpell, QuestDef/GetRewSpellCast, QuestDef/IsRepeatable, QuestDef/XPValue, ScriptMgr/OnQuestRewarded, ScriptMgr/OnQuestRewarded#2, SpellCaster/CastSpell#2, SpellMgr/GetSpellAreaForQuestEndMapBounds, SpellMgr/GetSpellAreaForQuestMapBounds, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsFitToRequirements, Unit.Main/GetLevel, Unit.Main/HasAura, Unit.Main/RemoveAurasDueToSpell, World/getConfig#2, World/getConfig#4, WorldObject.Object/GetMap, WorldObject.Object/GetZoneAndAreaId, WorldSession.Main/GetSessionDbcLocale | AiBotAI.Bridge/BridgeHandleQuestInteract, WorldSession.QuestHandler/HandleQuestgiverChooseRewardOpcode | — |
| FailQuest | method | ObjectMgr/GetQuestTemplate, QuestDef/HasSpecialFlag | boss_vaelastrasz/QuestAccept_vaelastrasz, eastern_plaguelands/FailEvent, feralas/SpriteDied, Map.ScriptCommands/ScriptCommand_QuestExplored, moonglade/JustDied, npcs_special/EndEvent, ScriptedFollowerAI/JustDied, stonetalon_mountains/JustDied | — |
| SatisfyQuestSkill | method | QuestDef/GetRequiredSkill, QuestDef/GetRequiredSkillValue | AiBotAI.Bridge/BridgeHandleQuestInteract | — |
| SatisfyQuestCondition | method | Conditions/IsConditionSatisfied, QuestDef/GetRequiredCondition, WorldObject.Object/GetMap | AiBotAI.Bridge/BridgeHandleQuestInteract | — |
| SatisfyQuestLevel | method | QuestDef/GetMinLevel, Unit.Main/GetLevel | AiBotAI.Bridge/BridgeHandleQuestInteract, WorldSession.QuestHandler/GetDialogStatus | — |
| SatisfyQuestLog | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.QuestHandler/HandlePushQuestToParty | — |
| SatisfyQuestPreviousQuest | method | Errors/PrintStacktraceAndThrow, ObjectMgr/GetExclusiveQuestGroupsMapBounds, ObjectMgr/GetQuestTemplate, QuestDef/GetExclusiveGroup | AiBotAI.Bridge/BridgeHandleQuestInteract | — |
| SatisfyQuestBreadcrumbQuest | method | ObjectMgr/GetQuestTemplate, QuestDef/GetBreadcrumbForQuestId | AiBotAI.Bridge/BridgeHandleQuestInteract | — |
| SatisfyQuestDependentBreadcrumbQuests | method | — | AiBotAI.Bridge/BridgeHandleQuestInteract | — |
| SatisfyQuestClass | method | QuestDef/GetRequiredClasses, Unit.Main/GetClassMask | AiBotAI.Bridge/BridgeHandleQuestInteract | — |
| SatisfyQuestRace | method | QuestDef/GetRequiredRaces, Unit.Main/GetRaceMask | AiBotAI.Bridge/BridgeHandleQuestInteract | — |
| SatisfyQuestReputation | method | QuestDef/GetRequiredMaxRepFaction, QuestDef/GetRequiredMaxRepValue, QuestDef/GetRequiredMinRepFaction, QuestDef/GetRequiredMinRepValue, ReputationMgr/GetReputation#2 | AiBotAI.Bridge/BridgeHandleQuestInteract | — |
| SatisfyQuestStatus | method | QuestDef/GetQuestId | AiBotAI.Bridge/BridgeHandleQuestInteract, WorldSession.QuestHandler/HandlePushQuestToParty | — |
| SatisfyQuestTimed | method | QuestDef/HasSpecialFlag | AiBotAI.Bridge/BridgeHandleQuestInteract | — |
| SatisfyQuestExclusiveGroup | method | Errors/PrintStacktraceAndThrow, ObjectMgr/GetExclusiveQuestGroupsMapBounds, QuestDef/GetExclusiveGroup, QuestDef/GetQuestId | AiBotAI.Bridge/BridgeHandleQuestInteract | — |
| SatisfyQuestNextChain | method | QuestDef/GetNextQuestInChain | AiBotAI.Bridge/BridgeHandleQuestInteract | — |
| SatisfyQuestPrevChain | method | — | AiBotAI.Bridge/BridgeHandleQuestInteract | — |
| CanGiveQuestSourceItemIfNeed | method | QuestDef/GetQuestId, QuestDef/GetSrcItemCount, QuestDef/GetSrcItemId | — | — |
| GiveQuestSourceItemIfNeed | method | QuestDef/GetSrcItemId | — | — |
| TakeOrReplaceQuestStartItems | method | ObjectMgr/GetItemPrototype, ObjectMgr/GetQuestStartingItemID, ObjectMgr/GetQuestTemplate, QuestDef/GetSrcItemCount, QuestDef/GetSrcItemId | — | — |
| GetQuestRewardStatus | method | ObjectMgr/GetQuestTemplate, QuestDef/IsRepeatable | blackrock_depths/GossipHello_npc_mistress_nagmara, ChatHandler.LookupCommands/ShowQuestListHelper, Conditions/Evaluate, GameObject/ActivateToQuest, instance_blackrock_depths/ReplacePrincessIfPossible, instance_blackwing_lair/AreaTrigger_at_orb_of_command, instance_dire_maul/GossipHello_npc_knot_thimblejack, quest_stormwind_rendezvous/AreaTrigger_at_stormwind_gates, SpellMgr/IsFitToRequirements, sunken_temple/AreaTrigger_at_shade_of_eranikus, WorldSession.QuestHandler/GetDialogStatus | — |
| GetQuestStatusData | method | — | ChatHandler.CharacterCommands/HandleQuestStatusCommandHelper | — |
| GetQuestStatus | method | — | AiBotAI.Bridge/BridgeHandleAbandonQuest, AiBotAI.Bridge/BridgeHandleQuestInteract, AiBotAI.Bridge/BridgeSendState, areatrigger_scripts/AreaTrigger_at_ravenholdt, BattleGroundAV/CompleteQuestForAll, boss_celebras_the_cursed/GOHello_go_book_celebras, boss_celebras_the_cursed/UpdateEscortAI, boss_victor_nefarius/SummonedCreatureJustDied, burning_steppes/GossipHello_npc_klinfran, ChatHandler.CharacterCommands/HandleQuestCompleteCommand, ChatHandler.LookupCommands/ShowQuestListHelper, darkshore/at_murloc_camp, darkshore/GossipHello_npc_threshwackonator, darkshore/MoveInLineOfSight, darkshore/MovementInform, darkshore/UpdateAI#2, duskwood/Handle_NightmareCorruption, dustwallow_marsh/AreaTrigger_at_sentry_point, dustwallow_marsh/GossipHello_npc_cassa_crimsonwing, dustwallow_marsh/GossipHello_npc_lady_jaina_proudmoore, dustwallow_marsh/WaypointReached, eastern_plaguelands/CompleteEvent, eastern_plaguelands/FailEvent, eastern_plaguelands/GossipHello_npc_joseph_redpath, eastern_plaguelands/Reset, felwood/AreaTrigger_at_irontree_wood, feralas/SpriteDied, feralas/SpriteSaved, GameObject/ActivateToQuest, GameObject/Use, instance_deadmines/AreaTrigger_at_dmf_chest_dm, instance_dire_maul/GossipHello_npc_knot_thimblejack, instance_stratholme/SetData, instance_wailing_caverns/AreaTrigger_at_dmf_chest_wc, loch_modan/AreaTrigger_at_huldar_miran, LootMgr/AllowedForPlayer, moonglade/UpdateAI#2, npcs_special/EndEvent, npcs_special/ReceiveEmote, npcs_special/SpellHit, quest_stormwind_rendezvous/CompleteQuest, quest_stormwind_rendezvous/GossipHello_npc_squire_rowe, quest_stormwind_rendezvous/GossipSelect_npc_squire_rowe, ScriptedFollowerAI/JustDied, ScriptedFollowerAI/UpdateAI, silithus/GossipHello_npc_Krug_SkullSplit, stranglethorn_vale/SpellHit, stratholme/SpellHit, sunken_temple/AreaTrigger_at_shade_of_eranikus, tanaris/GOHello_go_inconspicuous_landmark, tanaris/MoveInLineOfSight, teldrassil/DoComplete, the_barrens/AreaTrigger_at_twiggy_flathead, thousand_needles/GossipHello_npc_plucky_johnson, thousand_needles/go_panther_cage, thousand_needles/ReceiveEmote, ungoro_crater/AreaTrigger_at_scent_larkorwi, ungoro_crater/GossipHello_npc_simone_the_inconspicuous, ungoro_crater/MoveInLineOfSight, western_plaguelands/MoveInLineOfSight#2, wetlands/GossipHello_npc_mikhail, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.QuestHandler/GetDialogStatus, WorldSession.QuestHandler/HandlePushQuestToParty, WorldSession.QuestHandler/HandleQuestgiverCompleteQuest, WorldSession.QuestHandler/HandleQuestgiverRequestRewardOpcode, zulfarrak/OnGossipHello_go_table_theka | — |
| CanShareQuest | method | ObjectMgr/GetQuestTemplate, QuestDef/HasQuestFlag | WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode | — |
| SetQuestStatus | method | ObjectMgr/GetQuestTemplate | AiBotAI.Bridge/BridgeHandleAbandonQuest, ChatHandler.CharacterCommands/HandleQuestRemoveCommand | — |
| AdjustQuestReqItemCount | method | QuestDef/HasSpecialFlag | — | — |
| FindQuestSlot | method | — | — | — |
| AreaExploredOrEventHappens | method | — | areatrigger_scripts/AreaTrigger_at_childrens_week_spot, boss_celebras_the_cursed/UpdateEscortAI, eastern_plaguelands/CompleteEvent, Map.ScriptCommands/ScriptCommand_QuestExplored, Spell.Effects/EffectQuestComplete, stormwind_city/DamageTaken, thousand_needles/GossipSelect_npc_plucky_johnson, WorldSession.MiscHandler/HandleAreaTriggerOpcode, zulfarrak/OnGossipHello_go_table_theka | — |
| GroupEventHappens | method | Group/GetFirstMember, GroupReference/next, Object/HasFlag | arathi_highlands/FinishEvent, arathi_highlands/WaypointReached, arathi_highlands/WaypointReached#2, ashenvale/WaypointReached, ashenvale/WaypointReached#2, ashenvale/WaypointReached#3, blackrock_depths/DoJailBreakQuestCredit, burning_steppes/JustDidDialogueStep, darkshore/MoveInLineOfSight, darkshore/MovementInform, darkshore/WaypointReached, darkshore/WaypointReached#2, darkshore/WaypointReached#3, desolace/Dialogue, desolace/WaypointReached, desolace/WaypointReached#2, dustwallow_marsh/WaypointReached, felwood/Dialogue, felwood/WaypointReached#2, feralas/MoveInLineOfSight, feralas/SpriteSaved, gnomeregan/UpdateFollowerAI, hinterlands/WaypointReached, loch_modan/WaypointReached, moonglade/DoHandleOutro, moonglade/UpdateAI#2, npcs_special/EndEvent, razorfen_downs/UpdateEscortAI, razorfen_kraul/WaypointReached, redridge_mountains/WaypointReached, silithus/UpdateAI#7, silverpine_forest/WaypointReached, stonetalon_mountains/UpdateAI, stormwind_city/UpdateAI, swamp_of_sorrows/WaypointReached, tanaris/MoveInLineOfSight, teldrassil/DoComplete, the_barrens/UpdateEscortAI, the_barrens/WaypointReached, thousand_needles/WaypointReached, thousand_needles/WaypointReached#2, ungoro_crater/MoveInLineOfSight, ungoro_crater/WaypointReached, westfall/WaypointReached, wetlands/UpdateEscortAI | — |
| GroupEventFailHappens | method | Group/GetFirstMember, GroupReference/next | blackrock_depths/JustDied#3, blackrock_depths/JustDied#4, desolace/FailEscort, Map.ScriptCommands/ScriptCommand_FailQuest, Map.ScriptCommands/ScriptCommand_TerminateCondition, ScriptedEscortAI/JustDied, stormwind_city/Reset#2, wetlands/WaypointReached | — |
| ItemAddedQuestCheck | method | ObjectMgr/GetQuestTemplate, QuestDef/GetSrcItemId, QuestDef/HasSpecialFlag | WorldSession.ItemHandler/HandleAutoStoreBankItemOpcode, WorldSession.ItemHandler/HandleBuybackItem | — |
| ItemRemovedQuestCheck | method | ObjectMgr/GetQuestTemplate, QuestDef/HasSpecialFlag | WorldSession.ItemHandler/HandleSellItemOpcode | — |
| KilledMonster | method | — | game_Group_Group/RewardGroupAtKill_helper, npcs_special/UpdateAI#10 | — |
| KilledMonsterCredit | method | Group/isRaidGroup, ObjectGuid/ObjectGuid, ObjectMgr/GetQuestTemplate, QuestDef/HasSpecialFlag, QuestDef/IsAllowedInRaid | areatrigger_scripts/AreaTrigger_at_ravenholdt, BattleGroundAB/EventPlayerClickedOnFlag, boss_order_of_silver_hand/JustDied, darkshore/EffectDummyCreature_npc_rabid_thistle_bear, durotar/peon_wake_up, eastern_plaguelands/EffectDummyGameObj_go_mark_of_detonation, eastern_plaguelands/GossipHello_npc_joseph_redpath, instance_blackrock_depths/SetData, instance_stratholme/SetData, Map.ScriptCommands/ScriptCommand_KillCredit, npc_j_eevee/UpdateAI#2, OutdoorPvPSI/HandleAreaTrigger, Spell.Effects/EffectDummy, stratholme/UpdateAI#2, ThreatListCopier.battleground_alterac/MoveInLineOfSight#7, western_plaguelands/MoveInLineOfSight | — |
| CastedCreatureOrGO | method | ObjectGuid/IsCreature, ObjectMgr/GetQuestTemplate, QuestDef/HasQuestFlag, QuestDef/HasSpecialFlag | — | — |
| TalkedToCreature | method | ObjectMgr/GetQuestTemplate, QuestDef/HasSpecialFlag | feralas/GossipHello_npc_screecher_spirit, Map.ScriptCommands/ScriptCommand_QuestCredit | — |
| LogModifyMoney | method | Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/ObjectGuid, World/getConfig#4, World/LogMoneyTrade, WorldSession.Main/GetSecurity | ChatHandler.CharacterCommands/HandleModifyMoneyCommand, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.MailHandler/HandleMailTakeMoney, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| GetMaxMoney | method | WorldSession.Main/HasTrialRestrictions | WorldSession.MailHandler/HandleMailTakeMoney, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| MoneyChanged | method | ObjectMgr/GetQuestTemplate, QuestDef/GetRewOrReqMoney | — | — |
| ReputationChanged | method | ObjectMgr/GetQuestTemplate, QuestDef/GetRepObjectiveFaction, QuestDef/GetRepObjectiveValue, ReputationMgr/GetReputation | ReputationMgr/SetOneFactionReputation | — |
| HasQuestForItem | method | Group/isRaidGroup, ObjectMgr/GetItemPrototype, ObjectMgr/GetQuestTemplate, QuestDef/IsAllowedInRaid | LootMgr/AllowedForPlayer, LootMgr/HasQuestDropForPlayer, LootMgr/HasQuestDropForPlayer#2 | — |
| SendQuestCompleteEvent | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | dustwallow_marsh/AreaTrigger_at_sentry_point, loch_modan/AreaTrigger_at_huldar_miran | — |
| SendQuestReward | method | ByteBuffer/operator<<#10, QuestDef/GetQuestId, QuestDef/GetRewItemsCount, QuestDef/GetRewMoneyMaxLevelAtComplete, QuestDef/GetRewOrReqMoney, Unit.Main/GetLevel, World/getConfig#4, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendQuestFailedAtTaker | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendQuestFailed | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendQuestTimerFailed | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendCanTakeQuestResponse | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.DebugCommands/HandleDebugSendQuestInvalidMsgCommand | — |
| SendQuestConfirmAccept | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, Object/GetObjectGuid, ObjectGuid/operator<<, ObjectMgr/GetQuestLocale, QuestDef/GetQuestId, QuestDef/GetTitle, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode | — |
| SendPushToPartyResponse | method | ByteBuffer/operator<<#7, Object/GetObjectGuid, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.DebugCommands/HandleDebugSendQuestPartyMsgCommand, WorldSession.QuestHandler/HandlePushQuestToParty, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode | — |
| SendQuestUpdateAddItem | method | ByteBuffer/operator<<#10, Errors/PrintStacktraceAndThrow, QuestDef/GetQuestId, QuestDef/GetReqCreatureOrGOcount, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendQuestUpdateAddCreatureOrGo | method | ByteBuffer/operator<<#10, Errors/PrintStacktraceAndThrow, ObjectGuid/operator<<, QuestDef/GetQuestId, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| Initialize | method | WorldObject.Object/_Create | — | — |
| _LoadBGData | method | Field/GetFloat, Field/GetUInt32, GridDefines/IsValidMapCoord#4, MapEntry/IsBattleGround, QueryResult/Fetch, WorldLocation/WorldLocation#2 | — | — |
| LoadPositionFromDB | method | Database/PQuery, Field/GetCppString, Field/GetFloat, Field/GetUInt32, ObjectGuid/GetCounter, ObjectMgr/GetPlayerDataByGUID, QueryResult/Fetch | ChatHandler.Chat/ExtractLocationFromLink, ChatHandler.TeleportCommands/HandleGonameCommand | characters |
| _LoadIntoDataField | method | shared_Util/StrSplit | — | — |
| LoadFromDB | method | BattleGroundMgr/PlayerLoggedIn, Database/PExecute#2, Errors/PrintStacktraceAndThrow, Field/GetCppString, Field/GetFloat, Field/GetInt32, Field/GetString, Field/GetUInt16, Field/GetUInt32, Field/GetUInt64, Field/GetUInt8, GenericTransport/CalculatePassengerPosition, GridDefines/IsValidMapCoord#4, HonorMgr/Load, HonorMgr/SetHighestRank#2, HonorMgr/SetLastWeekCP, HonorMgr/SetLastWeekHK, HonorMgr/SetRankPoints, HonorMgr/SetStanding, HonorMgr/SetStoredDK, HonorMgr/SetStoredHK, Log.Main/Out, Map.Main/GetTransport, MapEntry/IsBattleGround, MapEntry/IsDungeon, MapManager/CreateMap, MapManager/GetContinentInstanceId, MapPersistentStateMgr/GetInstanceId, MovementInfo/ClearTransportData, MovementInfo/GetTransportPos, MovementInfo/SetTransportData, Object/GetGUIDLow, Object/HasFlag, Object/SetGuidValue, ObjectGuid/GetCounter, ObjectGuid/GetString, ObjectGuid/ObjectGuid, ObjectMgr/CheckPlayerName, ObjectMgr/GetFullTransportGuidFromLowGuid, ObjectMgr/GetGoBackTrigger, ObjectMgr/GetQuestTemplate, ObjectMgr/GetTaxiNodeEntry, ObjectMgr/IsReservedName, Player.StatSystem/UpdateAllStats#3, PlayerBotAI/BeforeAddToMap, PlayerTaxi/ClearTaxiDestinations, PlayerTaxi/GetTaxiSource, PlayerTaxi/LoadTaxiDestinationsFromString, PlayerTaxi/LoadTaxiMask, QueryResult/Fetch, ReputationMgr/LoadFromDB, SqlOperations/TakeResult, Transport/AddPassenger, Unit.Main/ClearInCombat, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPowerType, Unit.Main/GetRace, Unit.Main/InitPlayerDisplayIds, Unit.Main/IsAlive, Unit.Main/RemoveAllAuras, Unit.Main/RemoveAllAurasOnDeath, Unit.Main/SetCanModifyStats, Unit.Main/SetChannelObjectGuid, Unit.Main/SetCharm, Unit.Main/SetCharmerGuid, Unit.Main/SetCreatorGuid, Unit.Main/SetHealth, Unit.Main/SetOwnerGuid, Unit.Main/SetPet, Unit.Main/SetPower, Unit.Main/SetTargetGuid, World/getConfig, World/getConfig#4, World/GetWowPatch, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/Relocate#2, WorldObject.Object/SetByteValue, WorldObject.Object/SetFlag, WorldObject.Object/SetInt32Value, WorldObject.Object/SetLocationInstanceId, WorldObject.Object/SetLocationMapId, WorldObject.Object/SetMap, WorldObject.Object/SetUInt16Value, WorldObject.Object/SetUInt32Value, WorldObject.Object/_Create, WorldSession.Main/GetAccountId, WorldSession.Main/GetBot, WorldSession.Main/GetConsecutivePlayTime, WorldSession.Main/GetSecurity | WorldSession.CharacterHandler/HandlePlayerLogin | characters |
| UpdateOldRidingSkillToNew | method | — | — | — |
| SendPacketsAtRelogin | method | — | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| IsAllowedToLoot | method | Creature.Main/AI#2, Creature.Main/GetGroupLootRecipient, Creature.Main/GetOriginalLootRecipient, CreatureAI/CanBeLooted, Group/GetLootMethod, Group/isBGGroup, Loot/isLooted, LootMgr/hasItemFor, LootMgr/hasOverThresholdItem, LootMgr/IsAllowedLooter, Object/GetGUID, Object/GetObjectGuid, Unit.Main/IsDead | WorldObject.Object/BuildValuesUpdate | — |
| GetMaxLootDistance | method | Unit.Main/GetCombatReachToTarget | WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMoneyOpcode | — |
| _LoadAuras | method | Field/GetFloat, Field/GetInt32, Field/GetUInt32, Field/GetUInt64, Field/GetUInt8, ObjectGuid/ObjectGuid#5, QueryResult/Fetch, QueryResult/NextRow, SpellCaster/CastSpell#2, Unit.Main/GetClass, Unit.Main/HasAuraType | — | — |
| LoadAura | method | Aura/GetModifier, Aura/SetLoadedState, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/ObjectGuid#2, ObjectGuid/operator!=, SpellAuraHolder/IsSingleTarget, SpellAuraHolder/SetIsSingleTarget, SpellAuraHolder/SetLoadedState, SpellEntry/HasRealTimeDuration, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/AddSpellAuraHolder, Unit.SpellAuras/AddAura, Unit.SpellAuras/CreateAura, Unit.SpellAuras/CreateSpellAuraHolder, Unit.SpellAuras/IsEmptyHolder | — | — |
| LoadCorpse | method | MapEntry/Instanceable, Object/ApplyModByteFlag, Object/GetObjectGuid, ObjectAccessor/ConvertCorpseForPlayer, Unit.Main/IsAlive, WorldObject.Object/GetMapId | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| _LoadInventory | method | AuctionHouseMgr/GetAItem, Bag/GetBagSize, Bag/NewItemOrBag, Database/PExecute#2, Field/GetBool, Field/GetUInt32, Field/GetUInt8, game_Mail_Mail/AddItem, game_Mail_Mail/MailReceiver, game_Mail_Mail/MailSender#4, game_Mail_Mail/SendMailTo, game_Objects_Item/FSetState, game_Objects_Item/GetContainer, game_Objects_Item/GetCount, game_Objects_Item/GetPos, game_Objects_Item/GetProto, game_Objects_Item/GetSlot, game_Objects_Item/IsBag, game_Objects_Item/IsLimitedToAnotherMapOrZone, game_Objects_Item/LoadFromDB, game_Objects_Item/SaveToDB, game_Objects_Item/SetContainer, game_Objects_Item/SetGeneratedLoot, game_Objects_Item/SetSlot, game_Objects_Item/SetState, Log.Main/Out, MailDraft/MailDraft#2, Object/GetEntry, Object/GetGUIDLow, Object/GetObjectGuid, ObjectMgr/GetItemPrototype, QueryResult/Fetch, QueryResult/NextRow, Unit.Main/IsAlive, WorldObject.Object/GetMapId, WorldObject.Object/GetZoneId, WorldSession.Main/GetMangosString, WorldSession.Main/ProcessAnticheatAction | — | character_deleted_items, character_inventory, item_instance |
| _LoadItemLoot | method | Database/PExecute#2, Field/GetUInt32, game_Objects_Item/LoadLootFromDB, Log.Main/Out, ObjectGuid/ObjectGuid#2, QueryResult/Fetch, QueryResult/NextRow | — | item_loot |
| LoadPet | method | Object/IsInWorld, Pet.Main/LoadPetFromDB, Pet.Main/Pet | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| _LoadQuestStatus | method | Field/GetUInt32, Field/GetUInt64, Field/GetUInt8, Log.Main/Out, Object/GetGUIDLow, ObjectMgr/GetQuestTemplate, QueryResult/Fetch, QueryResult/NextRow, QuestDef/HasSpecialFlag, QuestDef/IsRepeatable, World/GetGameTime | — | — |
| _LoadSpells | method | Field/GetBool, Field/GetUInt32, QueryResult/Fetch, QueryResult/NextRow | — | — |
| _LoadGroup | method | Field/GetUInt32, Group/GetMemberGroup, Object/GetObjectGuid, ObjectMgr/GetGroupById, QueryResult/operator[] | — | — |
| _LoadBoundInstances | method | Database/PExecute#2, Field/GetBool, Field/GetUInt32, Field/GetUInt64, Log.Main/Out, MapEntry/IsDungeon, MapPersistentStateMgr/AddPersistentState, Object/GetGUIDLow, QueryResult/Fetch, QueryResult/NextRow | — | character_instance |
| GetBoundInstance | method | — | ChatHandler.TeleportCommands/HandleGonameCommand, game_Group_Group/AddMember, game_Group_Group/Disband, game_Group_Group/_homebindIfInstance, game_Group_Group/_setLeader, Map.Main/BindPlayerOrGroupOnEnter, Map.Main/PermBindAllPlayers | — |
| UnbindInstance#2 | method | — | ChatHandler.TeleportCommands/HandleGonameCommand, Map.Main/BindPlayerOrGroupOnEnter, MapPersistentStateMgr/UnbindThisState | — |
| UnbindInstance | method | Database/PExecute#2, DungeonPersistentState/RemovePlayer, MapPersistentStateMgr/GetInstanceId, Object/GetGUIDLow | ChatHandler.MiscCommands/HandleInstanceUnbindHelper | character_instance |
| BindToInstance | method | Database/PExecute#2, DungeonPersistentState/AddPlayer, DungeonPersistentState/RemovePlayer, DungeonPersistentState/SetCanReset, Errors/PrintStacktraceAndThrow, Log.Main/Out, MapPersistentStateMgr/GetInstanceId, MapPersistentStateMgr/GetMapId, Object/GetGUIDLow | ChatHandler.TeleportCommands/HandleGonameCommand, game_Group_Group/Disband, game_Group_Group/_setLeader, Map.Main/BindPlayerOrGroupOnEnter, Map.Main/PermBindAllPlayers, PlayerBotAI/SpawnNewPlayer | character_instance |
| GetBoundInstanceSaveForSelfOrGroup | method | game_Group_Group/GetBoundInstance | MapManager/CanPlayerEnter, MapManager/CreateInstance, MapManager/ScheduleNewWorldOnFarTeleport, WorldSession.Main/LogoutPlayer | — |
| SendRaidInfo | method | ByteBuffer/operator<<#10, ByteBuffer/wpos, DungeonResetScheduler/GetResetTimeFor, MapPersistentStateManager/GetScheduler, MapPersistentStateMgr/GetInstanceId, MapPersistentStateMgr/GetMapId, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.GroupHandler/HandleRequestRaidInfoOpcode | — |
| SendSavedInstances | method | ByteBuffer/operator<<#10, MapPersistentStateMgr/GetMapId, WorldPacket/Initialize, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | — | — |
| ConvertInstancesToGroup | method | Database/PExecute#2, Errors/PrintStacktraceAndThrow, game_Group_Group/BindToInstance, Object/GetObjectGuid, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid, ObjectGuid/operator! | game_Group_Group/Create, game_Group_Group/_setLeader | character_instance, group_instance |
| _LoadHomeBind | method | Database/PExecute#2, Field/GetFloat, Field/GetUInt16, Field/GetUInt32, Log.Main/Out, MapEntry/Instanceable, MapManager/IsValidMapCoord#3, Object/GetGUIDLow, ObjectMgr/GetPlayerInfo, QueryResult/Fetch, Unit.Main/GetClass, Unit.Main/GetRace | — | character_homebind |
| _LoadGuild | method | Field/GetUInt32, GuildMgr/GetGuildById, Log.Main/Out, Object/GetGuidStr, QueryResult/Fetch | — | — |
| SaveNewPlayer | method | Database/BeginTransaction, Database/CommitTransaction, Database/CreateStatement, game_Objects_Item/GetAllowedEquipSlots, GridMap/GetZoneAndAreaId, Log.Main/Out, Map.Main/GetTerrain, MapManager/FindMap, MasterPlayer.Main/Create, MasterPlayer.Main/MasterPlayer, MasterPlayer.Main/SaveToDB, ObjectGuid/ObjectGuid#2, ObjectMgr/GetItemPrototype, ObjectMgr/GetPlayerClassLevelInfo, ObjectMgr/GetPlayerInfo, ObjectMgr/GetPlayerLevelInfo, ObjectMgr/InsertPlayerInCache#2, ObjectMgr/SetPlayerWorldMask, ObjectMgr/UpdatePlayerCachedPosition#2, Player.StatSystem/GetHealthBonusFromStamina, Player.StatSystem/GetManaBonusFromIntellect, PlayerTaxi/InitTaxiNodes, PlayerTaxi/operator<<, PlayerTaxi/PlayerTaxi, PlayerTaxi/SaveTaxiDestinationsToString, SqlPreparedStatement/Execute#2, SqlStatement/addFloat, SqlStatement/addInt8, SqlStatement/addString, SqlStatement/addString#2, SqlStatement/addUInt32, SqlStatement/addUInt64, SqlStatement/addUInt8, SqlStatementID/SqlStatementID, World/getConfig#4, WorldSession.Main/GetAccountId, WorldSession.Main/GetSecurity, WorldSession.Main/SaveTutorialsData | WorldSession.CharacterHandler/HandleCharCreateOpcode | — |
| UpdateCharacterFlags | method | Object/GetByteValue, Object/HasFlag, Unit.Main/IsPvP | — | — |
| SaveToDB | method | Common/finiteAlways, Database/BeginTransaction, Database/CommitTransaction, Database/CreateStatement, Geometry/NormalizeOrientation, HonorMgr/GetHighestRank, HonorMgr/GetLastWeekCP, HonorMgr/GetLastWeekHK, HonorMgr/GetRankPoints, HonorMgr/GetStanding, HonorMgr/GetStoredDK, HonorMgr/GetStoredHK, HonorMgr/Save, HonorMgr/Update, MovementInfo/GetTransportPos, Object/GetByteValue, Object/GetEntry, Object/GetGUIDLow, Object/GetInt32Value, Object/GetUInt32Value, Object/IsInWorld, ObjectMgr/GetPlayerDataByGUID, ObjectMgr/SetPlayerWorldMask, Pet.Main/SavePetToDB, PlayerTaxi/operator<<, PlayerTaxi/SaveTaxiDestinationsToString, ReputationMgr/SaveToDB, SqlPreparedStatement/Execute#2, SqlStatement/addFloat, SqlStatement/addInt32, SqlStatement/addString, SqlStatement/addString#2, SqlStatement/addUInt16, SqlStatement/addUInt32, SqlStatement/addUInt64, SqlStatement/addUInt8, SqlStatementID/SqlStatementID, Unit.Main/GetClass, Unit.Main/GetGender, Unit.Main/GetHealth, Unit.Main/GetLevel, Unit.Main/GetPet, Unit.Main/GetPower, Unit.Main/GetRace, World/getConfig, World/getConfig#4, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetWorldMask, WorldSession.Main/GetAccountId, WorldSession.Main/IsLogingOut, WorldSession.Main/SaveTutorialsData | AiBotAI.Main/OnPlayerLogin, AiBotAI.Main/UpdateAI, ChatHandler.CharacterCommands/HandleRemoveRidingCommand, ChatHandler.CharacterCommands/HandleSaveCommand, Map.Main/RemoveCorpses, ObjectAccessor/SaveAllPlayers, WorldSession.Main/ForcePlayerLogoutDelay, WorldSession.Main/LogoutPlayer | — |
| SaveInventoryAndGoldToDB | method | Database/BeginTransaction, Database/CommitTransaction, Database/GetTransactionSerialId, Database/InTransaction, Log.Main/Out, Object/GetGUIDLow, Object/GetGuidStr | ChatHandler.CharacterCommands/HandleDeleteItemCommand, Map.Main/CrashUnload, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.ItemHandler/HandleWrapItemOpcode, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| SaveGoldToDB | method | Database/CreateStatement, Object/GetGUIDLow, SqlStatementID/SqlStatementID | WorldSession.MailHandler/HandleMailTakeMoney | characters |
| _SaveAuras | method | Database/CreateStatement, Object/GetGUIDLow, ObjectGuid/GetRawValue, SqlPreparedStatement/Execute#2, SqlPreparedStatement/operator=, SqlStatement/addFloat, SqlStatement/addInt32, SqlStatement/addInt8, SqlStatement/addUInt32, SqlStatement/addUInt64, SqlStatement/addUInt8, SqlStatementID/SqlStatementID, Unit.Main/GetSpellAuraHolderMap | — | character_aura |
| SaveAura | method | Aura/GetModifier, Aura/IsAreaAura, Object/GetObjectGuid, ObjectGuid/GetCounter, ObjectGuid/operator!=, ObjectGuid/operator==, SpellAuraHolder/GetAuraByEffectIndex, SpellAuraHolder/GetAuraCharges, SpellAuraHolder/GetAuraDuration, SpellAuraHolder/GetAuraMaxDuration, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetCastItemGuid, SpellAuraHolder/GetSpellProto, SpellAuraHolder/GetStackAmount, SpellAuraHolder/IsPassive, SpellAuraHolder/IsSingleTarget, SpellEntry/HasAuraInterruptFlag, SpellEntry/IsChanneledSpell, Unit.SpellAuras/GetId | — | — |
| _SaveInventory | method | Database/CreateStatement, game_Objects_Item/FSetState, game_Objects_Item/GetBagSlot, game_Objects_Item/GetContainer, game_Objects_Item/GetOwnerGuid, game_Objects_Item/GetSlot, game_Objects_Item/GetState, game_Objects_Item/SaveToDB, game_Objects_Item/SetEnchantmentDuration, Object/GetEntry, Object/GetGUIDLow, Object/GetObjectGuid, ObjectGuid/operator!=, SqlPreparedStatement/Execute#2, SqlPreparedStatement/operator=, SqlStatement/addUInt32, SqlStatement/addUInt8, SqlStatementID/SqlStatementID, WorldSession.Main/ProcessAnticheatAction | — | character_inventory, item_instance |
| _SaveQuestStatus | method | Database/CreateStatement, Object/GetGUIDLow, SqlPreparedStatement/Execute#2, SqlStatement/addUInt32, SqlStatement/addUInt64, SqlStatement/addUInt8, SqlStatementID/SqlStatementID, World/GetGameTime | — | character_queststatus |
| _SaveSkills | method | Database/CreateStatement, Errors/PrintStacktraceAndThrow, Object/GetGUIDLow, Object/GetUInt32Value, SqlStatementID/SqlStatementID | — | character_skills |
| _SaveSpells | method | Database/CreateStatement, Object/GetGUIDLow, SqlStatementID/SqlStatementID | — | character_spell |
| _SaveStats | method | Database/CreateStatement, Object/GetFloatValue, Object/GetGUIDLow, Object/GetUInt32Value, SqlPreparedStatement/Execute#2, SqlPreparedStatement/operator=, SqlStatement/addFloat, SqlStatement/addInt32, SqlStatement/addUInt32, SqlStatementID/SqlStatementID, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetResistance, Unit.Main/GetSpellCritPercent, Unit.Main/GetStat, Unit.Main/GetTotalAuraModifier, World/getConfig#4 | — | character_stats |
| CanSpeak | method | — | ChatHandler.AccountCommands/HandleUnmuteCommand, WorldSession.ChatHandler/HandleEmoteOpcode, WorldSession.ChatHandler/HandleTextEmoteOpcode | — |
| SavePositionInDB | method | Database/Execute#2, Log.Main/Out, ObjectGuid/GetCounter | ChatHandler.TeleportCommands/HandleNamegoCommand, ChatHandler.TeleportCommands/HandleTeleNameCommand | characters |
| SendAttackSwingNotInRange | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Unit.Main/UpdateMeleeAttackingState | — |
| SendAttackSwingNotStanding | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendAttackSwingDeadTarget | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Unit.Main/UpdateMeleeAttackingState | — |
| SendAttackSwingCantAttack | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Unit.Main/UpdateMeleeAttackingState | — |
| SendAttackSwingCancelAttack | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Unit.Main/CombatStop, Unit.Main/InterruptAttacksOnMe, Unit.Main/SetFeignDeath, Unit.Main/StopAttackFaction, Unit.SpellAuras/HandleModCharm | — |
| SendAttackSwingBadFacingAttack | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Unit.Main/UpdateMeleeAttackingState | — |
| SendAutoRepeatCancel | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | SpellCaster/InterruptSpell | — |
| SendFeignDeathResisted | method | WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Unit.Main/SetFeignDeath | — |
| SendExplorationExperience | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendFactionAtWar | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Creature.Main/OnEnterCombat | — |
| SendResetFailedNotify | method | — | Map.Main/Reset | — |
| ResetInstances | method | DungeonPersistentState/CanReset, Object/IsInWorld, WorldObject.Object/GetMapId | game_Group_Group/AddMember, WorldSession.MiscHandler/HandleResetInstancesOpcode | — |
| ResetInstance | method | DungeonPersistentState/RemovePlayer, Map.Main/IsDungeon, Map.Main/Reset, MapManager/FindMap, MapPersistentStateMgr/DeleteFromDB, MapPersistentStateMgr/GetInstanceId, MapPersistentStateMgr/GetMapId | — | — |
| ResetPersonalInstanceOnLeaveDungeon | method | Errors/PrintStacktraceAndThrow, game_Group_Group/GetBoundInstance, WorldObject.Object/GetMapId | WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| SendResetInstanceSuccess | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | game_Group_Group/ResetInstances | — |
| SendResetInstanceFailed | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | game_Group_Group/ResetInstances | — |
| CheckInstanceCount | method | AccountMgr/CheckInstanceCount, World/getConfig#4, WorldSession.Main/GetAccountId | MapManager/CanPlayerEnter | — |
| AddInstanceEnterTime | method | AccountMgr/AddInstanceEnterTime, WorldSession.Main/GetAccountId | Map.Main/Add#2 | — |
| UpdatePvPFlagTimer | method | — | — | — |
| UpdatePvPContestedFlagTimer | method | — | — | — |
| SetPvPDesired | method | WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| SetFFAPvP | method | WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| IsInInterFactionMode | method | Map.Main/IsDungeon, WorldObject.Object/FindMap | WorldObject.Object/BuildValuesUpdate | — |
| UpdateDuelFlag | method | WorldObject.Object/SetUInt32Value | — | — |
| RemovePet | method | Pet.Main/Unsummon, Unit.Main/GetPet | WorldSession.Main/LogoutPlayer, WorldSession.MovementHandler/HandleSetActiveMoverOpcode | — |
| RemoveMiniPet | method | Pet.Main/Unsummon | Spell.Effects/EffectScriptEffect, Spell.Effects/EffectSummonCritter | — |
| GetMiniPet | method | Map.Main/GetPet, ObjectGuid/IsEmpty, WorldObject.Object/GetMap | areatrigger_scripts/AreaTrigger_at_childrens_week_spot, Spell.Effects/EffectScriptEffect, Spell.Effects/EffectSummonCritter | — |
| Say | method | ChatHandler.Chat/BuildChatPacket, Object/GetObjectGuid, World/getConfig#2, WorldPacket/WorldPacket | AiBotAI.Bridge/BridgeHandleSayText, boss_celebras_the_cursed/GOHello_go_book_celebras, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| GetYellRange | method | Unit.Main/GetLevel, World/getConfig#2, World/getConfig#4 | — | — |
| Yell | method | ChatHandler.Chat/BuildChatPacket, Object/GetObjectGuid, WorldPacket/WorldPacket | AiBotAI.Bridge/BridgeHandleSayText, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| TextEmote | method | ChatHandler.Chat/BuildChatPacket, Object/GetObjectGuid, World/getConfig, World/getConfig#2, WorldPacket/WorldPacket | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| SendSysMessage#2 | method | WorldSession.Main/GetMangosString | ChatHandler.CharacterCommands/HandleResetSpellsCommand, ChatHandler.CharacterCommands/HandleResetTalentsCommand, ObjectMgr/IsVendorItemValid | — |
| SendSysMessage | method | ChatHandler.Chat/BuildChatPacket, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | ChatHandler.CharacterCommands/HandleCheatFlyCommand, World/SendGMTicketText, WorldSession.AuctionHouseHandler/HandleAuctionSellItem | — |
| PSendSysMessage#2 | method | WorldSession.Main/GetMangosString | BattleGroundMgr/AddGroup, ChatHandler.AccountCommands/HandleAccountSetGmLevelCommand, ChatHandler.AccountCommands/HandleMuteCommand, ChatHandler.AccountCommands/HandleUnmuteCommand, ChatHandler.AccountCommands/HandleWarnCharacterCommand, ChatHandler.CharacterCommands/HandleCharacterLevel, ChatHandler.CharacterCommands/HandleCheatAlwaysCritCommand, ChatHandler.CharacterCommands/HandleCheatAlwaysProcCommand, ChatHandler.CharacterCommands/HandleCheatBeastmasterCommand, ChatHandler.CharacterCommands/HandleCheatCastTimeCommand, ChatHandler.CharacterCommands/HandleCheatCooldownCommand, ChatHandler.CharacterCommands/HandleCheatDebuffImmunityCommand, ChatHandler.CharacterCommands/HandleCheatDebugTargetInfoCommand, ChatHandler.CharacterCommands/HandleCheatFixedZCommand, ChatHandler.CharacterCommands/HandleCheatFlyCommand, ChatHandler.CharacterCommands/HandleCheatGodCommand, ChatHandler.CharacterCommands/HandleCheatIgnoreTriggersCommand, ChatHandler.CharacterCommands/HandleCheatImmuneToCreaturesCommand, ChatHandler.CharacterCommands/HandleCheatImmuneToPlayersCommand, ChatHandler.CharacterCommands/HandleCheatNoCastCheckCommand, ChatHandler.CharacterCommands/HandleCheatPowerCommand, ChatHandler.CharacterCommands/HandleCheatTriggerPassCommand, ChatHandler.CharacterCommands/HandleCheatUntargetableCommand, ChatHandler.CharacterCommands/HandleCheatWallclimbCommand, ChatHandler.CharacterCommands/HandleCheatWaterwalkCommand, ChatHandler.CharacterCommands/HandleExploreCheatCommand, ChatHandler.CharacterCommands/HandleModifyBlockCommand, ChatHandler.CharacterCommands/HandleModifyBWalkCommand, ChatHandler.CharacterCommands/HandleModifyDodgeCommand, ChatHandler.CharacterCommands/HandleModifyEnergyCommand, ChatHandler.CharacterCommands/HandleModifyFlyCommand, ChatHandler.CharacterCommands/HandleModifyGenderCommand, ChatHandler.CharacterCommands/HandleModifyMeleeCritCommand, ChatHandler.CharacterCommands/HandleModifyMoneyCommand, ChatHandler.CharacterCommands/HandleModifyMountCommand, ChatHandler.CharacterCommands/HandleModifyParryCommand, ChatHandler.CharacterCommands/HandleModifyRageCommand, ChatHandler.CharacterCommands/HandleModifyRangedCritCommand, ChatHandler.CharacterCommands/HandleModifySpeedCommand, ChatHandler.CharacterCommands/HandleModifySpellCritCommand, ChatHandler.CharacterCommands/HandleModifySwimCommand, ChatHandler.CharacterCommands/HandleRepairitemsCommand, ChatHandler.CharacterCommands/HandleTaxiCheatCommand, ChatHandler.DebugCommands/HandleDebugSpellModsCommand, ChatHandler.TeleportCommands/HandleGonameCommand, ChatHandler.TeleportCommands/HandleGroupgoCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, ChatHandler.TeleportCommands/HandleTeleGroupCommand, ChatHandler.TeleportCommands/HandleTeleNameCommand, ChatHandler.UnitCommands/HandleModifyAgilityCommand, ChatHandler.UnitCommands/HandleModifyArcaneCommand, ChatHandler.UnitCommands/HandleModifyArmorCommand, ChatHandler.UnitCommands/HandleModifyCastSpeedCommand, ChatHandler.UnitCommands/HandleModifyFireCommand, ChatHandler.UnitCommands/HandleModifyFrostCommand, ChatHandler.UnitCommands/HandleModifyHolyCommand, ChatHandler.UnitCommands/HandleModifyHPCommand, ChatHandler.UnitCommands/HandleModifyIntellectCommand, ChatHandler.UnitCommands/HandleModifyMainSpeedCommand, ChatHandler.UnitCommands/HandleModifyManaCommand, ChatHandler.UnitCommands/HandleModifyMeleeApCommand, ChatHandler.UnitCommands/HandleModifyNatureCommand, ChatHandler.UnitCommands/HandleModifyOffSpeedCommand, ChatHandler.UnitCommands/HandleModifyRangedApCommand, ChatHandler.UnitCommands/HandleModifyRangedSpeedCommand, ChatHandler.UnitCommands/HandleModifyScaleCommand, ChatHandler.UnitCommands/HandleModifyShadowCommand, ChatHandler.UnitCommands/HandleModifySpellPowerCommand, ChatHandler.UnitCommands/HandleModifySpiritCommand, ChatHandler.UnitCommands/HandleModifyStaminaCommand, ChatHandler.UnitCommands/HandleModifyStrengthCommand, ObjectMgr/IsVendorItemValid, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| PSendSysMessage | method | — | BattleGroundMgr/AddGroup, ChatHandler.MiscCommands/HandleInstanceUnbindHelper, MovementAnticheat/AddCheats, ObjectMgr/IsVendorItemValid, SpellMgr/IsSpellValid, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| PetSpellInitialize | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/wpos, CharmInfo/GetCommandState, CharmInfo/GetReactState, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/operator<<, Pet.Main/IsEnabled, Pet.Main/IsPermanentPetFor, Unit.Main/BuildActionBar, Unit.Main/GetCharmInfo, Unit.Main/GetPet, Unit.Main/WritePetSpellsCooldown, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Pet.Main/LearnSpell, Pet.Main/LoadPetFromDB, Pet.Main/RemoveSpell, Spell.Effects/EffectLearnPetSpell, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonPet#2, Spell.Effects/EffectTameCreature, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.MiscHandler/HandleRequestPetInfoOpcode, WorldSession.PetHandler/HandlePetUnlearnOpcode | — |
| PossessSpellInitialize | method | Aura/GetAuraDuration, Aura/GetCasterGuid, ByteBuffer/operator<<#10, ByteBuffer/operator<<#4, ByteBuffer/operator<<#7, Log.Main/Out, Object/GetGUIDLow, Object/GetObjectGuid, Object/GetTypeId, ObjectGuid/operator<<, ObjectGuid/operator==, Unit.Main/BuildActionBar, Unit.Main/GetAurasByType, Unit.Main/GetCharm, Unit.Main/GetCharmInfo, Unit.Main/WritePetSpellsCooldown, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Unit.SpellAuras/ModPossess | — |
| CharmSpellInitialize | method | Aura/GetAuraDuration, Aura/GetCasterGuid, ByteBuffer/operator<<#10, ByteBuffer/operator<<#4, ByteBuffer/operator<<#7, CharmInfo/GetCharmSpell, CharmInfo/GetCommandState, CharmInfo/GetReactState, Creature.Main/GetCreatureInfo, Log.Main/Out, Object/GetGUIDLow, Object/GetObjectGuid, Object/GetTypeId, ObjectGuid/operator<<, ObjectGuid/operator==, Unit.Main/BuildActionBar, Unit.Main/GetAurasByType, Unit.Main/GetCharm, Unit.Main/GetCharmInfo, Unit.Main/GetClass, Unit.Main/WritePetSpellsCooldown, UnitActionBarEntry/GetAction, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Unit.SpellAuras/HandleModCharm, WorldSession.MiscHandler/HandleRequestPetInfoOpcode | — |
| RemovePetActionBar | method | ObjectGuid/ObjectGuid, ObjectGuid/operator<<, WorldPacket/WorldPacket#4 | Pet.Main/Unsummon, Spell.Main/SendChannelUpdate, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/ModPossess | — |
| SummonPossessedMinion | method | Camera/SetView, Creature.Main/Create, Creature.Main/SetFactionTemporary, Creature.Main/SetSummonPoint, CreatureCreatePos/CreatureCreatePos, CreatureCreatePos/CreatureCreatePos#2, CreatureInfo/GetHighGuid, Log.Main/Out, Map.Main/GenerateLocalLowGuid, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/IsEmpty, ObjectMgr/GetCreatureTemplate, TemporarySummon/Summon, TemporarySummon/TemporarySummon, Transport/AddPassenger, Unit.Main/AddUnitState, Unit.Main/GetCharmGuid, Unit.Main/GetFactionTemplateId, Unit.Main/GetLevel, Unit.Main/InitCharmInfo, Unit.Main/InitPossessCreateSpells, Unit.Main/SetCharmerGuid, Unit.Main/SetCharmGuid, Unit.Main/SetLevel, Unit.Main/SetPossessorGuid, Unit.Main/SetWalk, Unit.Main/UpdateControl, WorldObject.Object/GetCreatureSummonCount, WorldObject.Object/GetCreatureSummonLimit, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetTransport, WorldObject.Object/GetWorldMask, WorldObject.Object/IncrementSummonCounter, WorldObject.Object/IsWalking, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value, WorldObject.Object/SetWorldMask | Spell.Effects/EffectSummonPossessed | — |
| UnsummonPossessedMinion | method | Camera/ResetView, ObjectGuid/ObjectGuid, Unit.Main/ClearUnitState, Unit.Main/DoKillUnit, Unit.Main/GetCharm, Unit.Main/SetCharm, Unit.Main/SetCharmerGuid, Unit.Main/SetPossessorGuid, Unit.Main/UpdateControl, WorldObject.Object/RemoveFlag, WorldObject.Object/SendForcedObjectUpdate | Unit.SpellAuras/HandleAuraDummy | — |
| HasInstantCastingSpellMod | method | SpellModifier/IsAffectedOnSpell | — | — |
| IsAffectedBySpellmod | method | SpellModifier/IsAffectedOnSpell | — | — |
| AddSpellMod | method | Errors/PrintStacktraceAndThrow | Unit.SpellAuras/HandleAddModifier, Unit.SpellAuras/HandleAuraDummy | — |
| SendSpellMod | method | ByteBuffer/operator<<#4, ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4 | — | — |
| GetSpellMod | method | — | Unit.AuraProcHandler/HandleDummyAuraProc, Unit.SpellAuras/HandleAuraDummy | — |
| RestoreSpellMods | method | — | Spell.Main/cancel, Spell.Main/cast, Spell.Main/finish | — |
| RestoreAllSpellMods | method | SpellCaster/GetCurrentSpell | — | — |
| RemoveSpellMods | method | Unit.Main/RemoveAurasDueToSpell | Spell.Main/cancel, Spell.Main/finish, Spell.Main/handle_immediate | — |
| DropModCharge | method | Errors/PrintStacktraceAndThrow, Spell.Main/HasModifierApplied | — | — |
| SendProficiency | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Spell.Effects/EffectProficiency | — |
| RemovePetitionsAndSigns | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, GuildMgr/DeletePetitionSignaturesByPlayer, ObjectGuid/GetCounter | game_Guild_Guild/AddMember | petition, petition_sign |
| SetRestBonus | method | Object/GetUInt32Value, Unit.Main/GetLevel, World/getConfig#4, WorldObject.Object/SetByteValue, WorldObject.Object/SetUInt32Value | — | — |
| ActivateTaxiPathTo | method | ByteBuffer/operator<<#10, Log.Main/Out, Object/HasFlag, ObjectMgr/GetTaxiMountDisplayId, ObjectMgr/GetTaxiNodeEntry, ObjectMgr/GetTaxiPath, ObjectMgr/GetTaxiPathTransitionsMapBounds, PlayerTaxi/AddTaxiDestination, PlayerTaxi/AddTaxiPathNode, PlayerTaxi/ClearTaxiDestinations, PlayerTaxi/IsTaximaskNodeKnown, PlayerTaxi/SetDiscount, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/CombatStop, Unit.Main/GetPet, Unit.Main/IsInCombat, Unit.Main/IsInDisallowedMountForm, Unit.Main/IsMounted, Unit.Main/RemoveSpellsCausingAura, Unit.Main/ResetExtraAttacks, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldPacket/WorldPacket#4, WorldSession.Main/IsLogingOut, WorldSession.Main/ProcessAnticheatAction, WorldSession.Main/SendPacket, WorldSession.TaxiHandler/SendDoFlight | AiBotAI.Bridge/BridgeHandleTakeFlight, WorldSession.TaxiHandler/HandleActivateTaxiExpressOpcode, WorldSession.TaxiHandler/HandleActivateTaxiOpcode | — |
| ActivateTaxiPathTo#2 | method | — | Map.ScriptCommands/ScriptCommand_SendTaxiPath, Spell.Effects/EffectSendTaxi | — |
| ContinueTaxiFlight | method | Log.Main/Out, Object/GetGUIDLow, ObjectMgr/GetTaxiMountDisplayId, PlayerTaxi/GetCurrentTaxiPath, PlayerTaxi/GetTaxiSource, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.TaxiHandler/SendDoFlight | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| Mount | method | Object/HasFlag, Pet.Main/IsPermanentPetFor, Pet.Main/SetEnabled, Unit.Main/GetPet, Unit.Main/IsInDisallowedMountForm, Unit.Main/IsMounted, Unit.Main/Mount, Unit.Main/RemoveSpellsCausingAura, Unit.Main/ResetExtraAttacks, World/getConfig | ChatHandler.CharacterCommands/HandleModifyMountCommand, WorldSession.TaxiHandler/SendDoFlight | — |
| Unmount | method | Pet.Main/SetEnabled, Unit.Main/GetPet, Unit.Main/IsMounted, Unit.Main/Unmount | ChatHandler.CharacterCommands/HandleDismountCommand, ChatHandler.CharacterCommands/HandleMountCommand, WaypointMovementGenerator/Finalize | — |
| SendMountResult | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendDismountResult | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| InitDataForForm | method | Player.StatSystem/UpdateAttackPowerAndDamage#3, Unit.Main/GetClass, Unit.Main/GetPowerType, Unit.Main/GetShapeshiftForm, Unit.Main/SetAttackTime, Unit.Main/SetPowerType | Unit.SpellAuras/HandleAuraModShapeshift | — |
| BuyItemFromVendor | method | ByteBuffer/operator<<#10, Conditions/IsConditionSatisfied, Creature.Main/FindItemSlot, Creature.Main/GetVendorItemCurrentCount, Creature.Main/GetVendorItems, Creature.Main/GetVendorTemplateItems, Creature.Main/UpdateVendorItemCurrentCount, game_Objects_Item/GenerateItemRandomPropertyId, HonorMgr/GetHighestRank, HonorMgr/GetRank, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/operator<<, ObjectMgr/GetItemPrototype, Unit.Main/GetLevel, Unit.Main/IsAlive, VendorItemData/Empty, VendorItemData/GetItem, VendorItemData/GetItemCount, World/getConfig, World/GetWowPatch, WorldObject.Object/GetFactionId, WorldObject.Object/GetMap, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.ItemHandler/HandleBuyItemInSlotOpcode, WorldSession.ItemHandler/HandleBuyItemOpcode | — |
| SendRaidGroupOnlyError | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | MapManager/CanPlayerEnter | — |
| UpdateHomebindTime | method | Log.Main/Out, Object/GetGUIDLow | — | — |
| UpdatePvP | method | Unit.Main/IsPvP, Unit.Main/SetPvP | ChatHandler.UnitCommands/HandlePvPCommand, Map.ScriptCommands/ScriptCommand_SetPvP, Spell.Main/DoSpellHitOnUnit, Unit.Main/SetInCombatWithAggressor, Unit.Main/SetInCombatWithAssisted, Unit.Main/TogglePlayerPvPFlagOnAttackVictim, WaypointMovementGenerator/Finalize, WorldSession.MiscHandler/HandleTogglePvP | — |
| UpdatePvPContested | method | Object/HasFlag, Unit.Main/SetPvPContested, Unit.Main/UpdateVisibilityAndView | Unit.Main/SetInCombatWithAssisted, Unit.Main/TogglePlayerPvPFlagOnAttackVictim, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| SetBattleGroundEntryPoint#2 | method | WorldLocation/WorldLocation#2 | ChatHandler.MiscCommands/RegisterPlayerToBG, WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| SetBattleGroundEntryPoint | method | Log.Main/Out, Map.Main/IsBattleGround, Map.Main/IsDungeon, Object/IsInWorld, ObjectMgr/GetClosestGraveYard, ObjectMgr/GetWorldSafeLocFacing, Unit.Main/IsTaxiFlying, WorldLocation/WorldLocation, WorldLocation/WorldLocation#2, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | ChatHandler.TeleportCommands/HandleGonameCommand, ChatHandler.TeleportCommands/HandleNamegoCommand, WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| LeaveBattleground | method | BattleGround/GetInstanceID, BattleGround/GetMapId, BattleGround/GetStatus, BattleGround/GetTypeID, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, Log.Main/Out, Object/GetGUIDLow, Object/GetObjectGuid, Unit.Main/AddAura, Unit.Main/RemoveAurasDueToSpell, World/getConfig, World/IsStopped, WorldSession.Main/GetAccountId, WorldSession.Main/GetRemoteAddress | BattleGroundAB/HandleAreaTrigger, BattleGroundAV/HandleAreaTrigger, BattleGroundWS/HandleAreaTrigger, WorldSession.BattleGroundHandler/HandleLeaveBattlefieldOpcode, WorldSession.Main/LogoutPlayer | — |
| CanJoinToBattleground | method | Unit.Main/HasAura#2 | game_Group_Group/CanJoinBattleGroundQueue, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| IsVisibleInGridForPlayer | method | Unit.Main/GetDeathState, Unit.Main/IsAlive, Unit.Main/IsFriendlyTo, WorldObject.Object/IsWithinDistInMap, WorldSession.Main/GetSecurity | — | — |
| IsVisibleGloballyFor | method | Unit.Main/GetVisibility, WorldObject.Object/CanSeeInWorld, WorldSession.Main/GetSecurity | ChatHandler.Chat/needReportToTarget, ChatHandler.MiscCommands/HandleGMListIngameCommand, game_Chat_Channel/List, WorldSession.MiscHandler/operator() | — |
| UpdateVisibilityOf | method | GameObject/IsMoTransport, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGUIDLow, Object/GetObjectGuid, Object/GetTypeId, Object/ToPlayer, ObjectGuid/GetString, PlayerBroadcaster/AddListener, PlayerBroadcaster/RemoveListener, WorldObject.Object/DestroyForPlayer, WorldObject.Object/FindMap, WorldObject.Object/GetDistance#3, WorldObject.Object/IsVisibleForInState, WorldObject.Object/IsWithinVisibilityDistanceOf, WorldObject.Object/SendCreateUpdateToPlayer | Camera/SetView, Camera/UpdateVisibilityOf, GridNotifiers/Notify, Map.Main/UpdateActiveObjectVisibility#2, PartyBotAI/UpdateAI | — |
| UpdateVisibilityOf_helper | function | GameObject/IsMoTransport, Object/GetEntry, Object/GetObjectGuid | — | — |
| AddBroadcastListener | function | PlayerBroadcaster/AddListener | — | — |
| RemoveBroadcastListener | function | PlayerBroadcaster/RemoveListener | — | — |
| UpdateVisibilityOf#2 | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetString, Unit.Main/IsVisibleForInState, WorldObject.Object/BuildOutOfRangeUpdateBlock, WorldObject.Object/FindMap, WorldObject.Object/GetDistance#3, WorldObject.Object/IsWithinVisibilityDistanceOf | GridNotifiers/Notify | — |
| LeaveCombatWithFarAwayCreatures | method | Creature.Main/ToCreature, HostileReference/next, HostileRefManager/deleteReference, HostileRefManager/getFirst, ObjectGuid/IsPlayer, ThreatManager/getSourceUnit, ThreatManager/modifyThreatPercent#2, Unit.Main/CanHaveThreatList, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetHostileRefManager, Unit.Main/GetThreatManager, WorldObject.Object/IsWithinVisibilityDistanceOf | — | — |
| SetLongSight | method | Aura/GetEffIndex, Aura/GetSpellProto, Camera/ResetView, Camera/SetView, DynamicObject/Create, DynamicObject/DynamicObject, Map.Main/GenerateLocalLowGuid, Map.Main/GetVisibilityDistance, Object/GetObjectGuid, SpellCaster/AddDynObject, Unit.Main/SetChannelObjectGuid, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | Unit.SpellAuras/HandleFarSight | — |
| UpdateLongSight | method | SpellCaster/GetDynObject#2, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/Relocate | Map.Main/PlayerRelocation | — |
| ScheduleCameraUpdate | method | Object/SetGuidValue, ObjectGuid/IsEmpty, WorldObject.Object/DirectSendPublicValueUpdate#3 | Camera/SetView | — |
| InitPrimaryProfessions | method | World/getConfig#4 | — | — |
| SetComboPoints | method | Object/GetObjectGuid, Object/SetGuidValue, ObjectAccessor/GetUnit, WorldObject.Object/SetByteValue | — | — |
| AddComboPoints | method | Object/GetGUIDLow, Object/GetObjectGuid, ObjectAccessor/GetUnit, ObjectGuid/operator==, Unit.Main/AddComboPointHolder, Unit.Main/RemoveComboPointHolder, Unit.Main/RemoveSpellsCausingAura | Spell.Effects/EffectAddComboPoints, Unit.Main/ProcSkillsAndReactives, Unit.SpellAuras/HandleAuraRetainComboPoints | — |
| ClearComboPoints | method | Object/GetGUIDLow, ObjectAccessor/GetUnit, ObjectGuid/Clear, ObjectGuid/operator!, Unit.Main/RemoveComboPointHolder, Unit.Main/RemoveSpellsCausingAura | Spell.Main/finish, Unit.Main/ClearAllReactives, Unit.Main/ClearComboPointHolders, Unit.Main/UpdateReactives, WorldSession.MiscHandler/HandleSetSelectionOpcode | — |
| SetGroup | method | Errors/PrintStacktraceAndThrow, GroupReference/setSubGroup | game_Group_Group/Disband, game_Group_Group/_addMember#2, game_Group_Group/_removeMember | — |
| SendInitialPacketsBeforeAddToMap | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#9, HonorMgr/Update, MasterPlayer.Main/SendInitialActionButtons, MovementInfo/AddMovementFlag, ReputationMgr/SendInitialReputations, shared_Util/secsToTimeBitFields, Unit.Main/IsTaxiFlying, World/GetGameTime, WorldPacket/Initialize, WorldPacket/WorldPacket#4, WorldSession.Main/GetMasterPlayer, WorldSession.Main/SendPacket, WorldSession.Main/SendTutorialsData | WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| SendInitialPacketsAfterAddToMap | method | SpellCaster/CastSpell#2, Unit.Main/GetAurasByType, Unit.Main/GetRace, Unit.Main/HasAuraType, Unit.Main/SetRooted, Unit.SpellAuras/ApplyModifier, WorldObject.Object/GetZoneAndAreaId | WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| SendUpdateToOutOfRangeGroupMembers | method | game_Group_Group/UpdatePlayerOutOfRange, Pet.Main/ResetAuraUpdateMask, Unit.Main/GetPet | — | — |
| SendTransferAborted | method | ByteBuffer/operator<<#7, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Map.Main/CanEnter#2, MapManager/CanPlayerEnter | — |
| SendInstanceResetWarning | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Map.Main/SendResetWarnings, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| ApplyEquipCooldown | method | ByteBuffer/operator<<#10, game_Objects_Item/GetProto, Object/GetObjectGuid, ObjectGuid/operator<<, SpellMgr/GetSpellEntry, SpellMgr/Instance, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| ResetSpells | method | — | ChatHandler.CharacterCommands/HandleResetSpellsCommand | — |
| LearnDefaultSpells | method | Log.Main/Out, Object/IsInWorld, ObjectMgr/GetPlayerInfo, Unit.Main/GetClass, Unit.Main/GetRace | ChatHandler.CharacterCommands/HandleLearnAllDefaultCommand, ChatHandler.CharacterCommands/HandleUnLearnAllCraftsCommand | — |
| LearnQuestRewardedSpells#2 | method | QuestDef/GetRewSpellCast, SpellCaster/CastSpell#2, SpellMgr/GetFirstSpellInChain, SpellMgr/GetSpellEntry, SpellMgr/GetSpellRank, SpellMgr/Instance, SpellMgr/IsHighRankOfSpell | — | — |
| LearnQuestRewardedSpells | method | ObjectMgr/GetQuestTemplate | ChatHandler.CharacterCommands/HandleLearnAllDefaultCommand | — |
| SetSemaphoreTeleportNear | method | — | WorldSession.MovementHandler/ExecuteTeleportNear | — |
| SetSemaphoreTeleportFar | method | — | MapManager/ExecuteSingleDelayedTeleport, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| GetBattleGround | method | BattleGroundMgr/GetBattleGround | AiBotAI.Combat/UpdateOutOfCombatAI_Druid, AiBotAI.Combat/UpdateOutOfCombatAI_Priest, AiBotAI.Combat/UpdateOutOfCombatAI_Warlock, BattleBotAI.BattleBotWaypoints/StartNewPathFromAnywhere, BattleBotAI.BattleBotWaypoints/StartNewPathFromBeginning, BattleBotAI.BattleBotWaypoints/StartNewPathToObjective, BattleBotAI.Main/DoGraveyardJump, BattleBotAI.Main/DrinkAndEat, BattleBotAI.Main/GetMaxAggroDistanceForMap, BattleBotAI.Main/OnEnterBattleGround, BattleBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateBattleGroundAI, BattleBotAI.Main/UpdateOutOfCombatAI_Druid, BattleBotAI.Main/UpdateOutOfCombatAI_Priest, BattleBotAI.Main/UpdateOutOfCombatAI_Warlock, BattleBotAI.Main/UpdateWaypointMovement, BattleBotAI.Main/UseMount, ChatHandler.MiscCommands/HandleBGCustomCommand, ChatHandler.MiscCommands/HandleBGStartCommand, ChatHandler.MiscCommands/HandleBGStopCommand, ChatHandler.PlayerBotMgr/HandleBattleBotShowAllPathsCommand, GameObject/Update, GameObject/Use, game_Group_Group/RewardGroupAtKill_helper, MovementAnticheat/CheckForbiddenArea, Spell.Effects/EffectDummy, Spell.Effects/EffectOpenLock, Spell.Effects/EffectSpiritHeal, Spell.Effects/EffectSummonObjectWild, Spell.Main/CheckCast, SpellMgr/GetSpellAllowedInLocationError, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/QuestComplete_AV_npc_troops_chief, ThreatListCopier.battleground_alterac/QuestComplete_npc_AVBlood_collector, Unit.Main/Kill, Unit.SpellAuras/HandleAuraModEffectImmunity, WorldSession.BattleGroundHandler/HandleAreaSpiritHealerQueryOpcode, WorldSession.BattleGroundHandler/HandleAreaSpiritHealerQueueOpcode, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode, WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode, WorldSession.BattleGroundHandler/HandleLeaveBattlefieldOpcode, WorldSession.BattleGroundHandler/HandlePVPLogDataOpcode, WorldSession.Main/LogoutPlayer, WorldSession.MiscHandler/HandleAreaTriggerOpcode, WorldSession.MiscHandler/HandleReclaimCorpseOpcode, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| GetBGAccessByLevel | method | BattleGround/GetMaxLevel, BattleGround/GetMinLevel, BattleGroundMgr/GetBattleGroundTemplate, Unit.Main/GetLevel | ChatHandler.MiscCommands/RegisterPlayerToBG, Creature.Main/CanInteractWithBattleMaster, WorldSession.BattleGroundHandler/HandleBattlemasterHelloOpcode | — |
| GetMinLevelForBattleGroundBracketId | method | BattleGround/GetMinLevel, BattleGroundMgr/GetBattleGroundTemplate, Errors/PrintStacktraceAndThrow | BattleGroundMgr/AddGroup, BattleGroundMgr/CheckCreateNewBg | — |
| GetMaxLevelForBattleGroundBracketId | method | — | BattleGroundMgr/AddGroup, BattleGroundMgr/CheckCreateNewBg | — |
| GetBattleGroundBracketIdFromLevel | method | Unit.Main/GetLevel | BattleGroundMgr/BuildBattleGroundListPacket, BattleGroundMgr/PlayerLoggedIn, ChatHandler.PlayerBotMgr/Update, game_Group_Group/CanJoinBattleGroundQueue, World/SendWorldTextToBGAndQueue, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| GetBattleGroundBracketIdFromLevel#2 | method | BattleGround/GetMinLevel, BattleGroundMgr/GetBattleGroundTemplate, Errors/PrintStacktraceAndThrow | World/SendWorldTextToBGAndQueue | — |
| GetReputationPriceDiscount | method | HonorMgr/GetRank, WorldObject.Object/GetFactionId | WorldSession.ItemHandler/SendListInventory, WorldSession.NPCHandler/HandleRepairItemOpcode, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.NPCHandler/SendTrainerList | — |
| IsSpellFitByClassAndRace | method | SpellMgr/GetSkillLineAbilityMapBoundsBySpellId, SpellMgr/GetSkillRaceClassInfoMapBounds, SpellMgr/Instance, Unit.Main/GetClassMask, Unit.Main/GetLevel, Unit.Main/GetRaceMask | AiBotAI.Bridge/BridgeHandleTrain, ChatHandler.CharacterCommands/HandleLearnAllMySpellsCommand, ChatHandler.CharacterCommands/HandleLearnTrainerHelper, WorldSession.NPCHandler/SendTrainerList | — |
| HasQuestForGO | method | Group/isRaidGroup, ObjectMgr/GetQuestTemplate, QuestDef/IsAllowedInRaid | GameObject/ActivateToQuest | — |
| UpdateForQuestWorldObjects | method | GameObject/ActivateToQuest, GameObject/IsTransport, Map.Main/GetGameObject, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/IsGameObject, UpdateData/HasData, UpdateData/Send, UpdateData/UpdateData, WorldObject.Object/BuildValuesUpdateBlockForPlayerWithFlags, WorldObject.Object/FindMap, WorldObject.Object/GetMap | game_Group_Group/AddMember, game_Group_Group/ConvertToRaid, game_Group_Group/Disband, game_Group_Group/RemoveMember | — |
| SendSummonRequest | method | ByteBuffer/operator<<#10, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.CharacterCommands/HandleGroupSummonCommand, Spell.Effects/EffectSummonPlayer | — |
| SummonIfPossible | method | BattleGround/EventPlayerDroppedFlag, MotionMaster/MovementExpired, PlayerTaxi/ClearTaxiDestinations, Unit.Main/GetMotionMaster, Unit.Main/IsTaxiFlying, WorldObject.Object/GetOrientation | WorldSession.MovementHandler/HandleSummonResponseOpcode | — |
| RemoveItemDurations | method | — | — | — |
| AddItemDurations | method | game_Objects_Item/SendTimeUpdate, Object/GetUInt32Value | — | — |
| AutoUnequipWeaponsIfNeed | method | — | — | — |
| AutoUnequipOffhandIfNeed | method | — | AiBotAI.Loot/TryAutoEquip, Spell.Effects/EffectSummonChangeItem, WorldSession.ItemHandler/HandleAutoEquipItemOpcode | — |
| AutoUnequipItemFromSlot | method | Database/BeginTransaction, Database/CommitTransaction, game_Mail_Mail/AddItem, game_Mail_Mail/MailReceiver, game_Mail_Mail/MailSender#4, game_Mail_Mail/SendMailTo, game_Objects_Item/DeleteFromInventoryDB, game_Objects_Item/SaveToDB, MailDraft/MailDraft#2, Object/GetGUIDLow, WorldSession.Main/GetMangosString | ObjectMgr/ApplyPremadeGearTemplateToPlayer, PartyBotAI/CloneFromPlayer | — |
| GetZoneScript | method | WorldObject.Object/GetZoneId, ZoneScriptMgr/GetZoneScriptToZoneId | spell_special/OnAfterApply#4, Unit.Main/Kill, Unit.Main/Mount, Unit.SpellAuras/HandleModStealth, WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| HasItemFitToSpellReqirements | method | game_Objects_Item/IsBroken, game_Objects_Item/IsFitToSpellRequirements, Log.Main/Out | Spell.Main/CheckItems | — |
| RemoveItemDependentAurasAndCasts | method | game_Objects_Item/GetProto, Object/GetObjectGuid, ObjectGuid/operator!=, Spell.Main/getState, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/IsPassive, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell, Unit.Main/GetSpellAuraHolderMap, Unit.Main/RemoveAurasDueToSpell, Unit.SpellAuras/GetId | — | — |
| SelectResurrectionSpellId | method | Aura/GetId, Aura/GetSpellProto, Log.Main/Out, shared_Util/roll_chance_i, SpellCaster/IsSpellReady#2, Unit.Main/GetAurasByType | Unit.Main/Kill | — |
| IsHonorOrXPTarget | method | Creature.Main/GetCreatureInfo, Creature.Main/IsPet, Creature.Main/IsTotem, Formulas/GetGrayLevel, Object/GetTypeId, Unit.Main/GetLevel, Unit.Main/HasUnitState | Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.SpellAuras/HandleChannelDeathItem | — |
| RewardSinglePlayerAtKill | method | Creature.Main/GetCreatureInfo, Formulas/Gain, Object/GetObjectGuid, Object/GetTypeId, Pet.Main/GivePetXP, Unit.Main/GetPet, Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself | Unit.Main/Kill | — |
| RewardPlayerAndGroupAtEvent | method | Group/GetFirstMember, GroupReference/next, Object/GetObjectGuid, Object/GetTypeId, Object/HasFlag, ObjectGuid/ObjectGuid, Unit.Main/IsAlive | Map.ScriptCommands/ScriptCommand_KillCredit | — |
| RewardPlayerAndGroupAtCast | method | Group/GetFirstMember, GroupReference/next, Object/GetEntry, Object/GetObjectGuid, Object/HasFlag, Unit.Main/IsAlive | GameObject/Use, npcs_special/UpdateAI#13, Spell.Main/DoAllEffectOnTarget, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/update | — |
| IsAtGroupRewardDistance | method | Unit.Main/IsAlive, WorldObject.Object/IsWithinLootXPDist | game_Battlegrounds_BattleGround/HandleKillPlayer, game_Group_Group/GetDataForXPAtKill, game_Group_Group/RewardGroupAtKill, LootMgr/FillPlayerDependentLoot, WorldSession.LootHandler/HandleLootMasterGiveOpcode, ZoneScript/HandleKill | — |
| GetBaseWeaponSkillValue | method | game_Objects_Item/GetProficiencySkill, game_Objects_Item/GetProto | — | — |
| ResurrectUsingRequestData | method | MapEntry/IsDungeon, MapPersistentStateMgr/GetInstanceId, ObjectGuid/IsPlayer, ObjectMgr/GetGoBackTrigger, ObjectMgr/GetMapEntranceTrigger, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/SetHealth, Unit.Main/SetPower, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | WorldSession.MiscHandler/HandleResurrectResponseOpcode | — |
| SetClientControl | method | ByteBuffer/operator<<#7, MovementAnticheat/LogMovementPacket, Object/GetPackGUID, ObjectGuid/operator<<#2, PackedGuid/size, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.DebugCommands/HandleDebugControlCommand, game_Battlegrounds_BattleGround/BlockMovement, Spell.Main/SendChannelUpdate, Unit.Main/UpdateControl, Unit.SpellAuras/ModPossess | — |
| GetConfirmedMover | method | Object/GetObjectGuid, ObjectGuid/operator!, ObjectGuid/operator==, Unit.Main/GetCharmerGuid, Unit.Main/GetPossessorGuid, WorldSession.Main/GetClientMoverGuid | WorldSession.MovementHandler/HandleMovementOpcodes | — |
| UpdateZoneDependentAuras | method | SpellCaster/CastSpell#2, SpellMgr/GetSpellAreaForAreaMapBounds, SpellMgr/Instance, SpellMgr/IsFitToRequirements, Unit.Main/HasAura | — | — |
| UpdateAreaDependentAuras | method | SpellAuraHolder/GetSpellProto, SpellCaster/CastSpell#2, SpellMgr/GetSpellAllowedInLocationError#2, SpellMgr/GetSpellAreaForAreaMapBounds, SpellMgr/Instance, SpellMgr/IsFitToRequirements, Unit.Main/HasAura, Unit.Main/RemoveSpellAuraHolder | — | — |
| GetCorpseReclaimDelay | method | World/getConfig | WorldSession.MiscHandler/HandleReclaimCorpseOpcode | — |
| UpdateCorpseReclaimDelay | method | World/getConfig | — | — |
| SendCorpseReclaimDelay | method | ByteBuffer/operator<<#10, Corpse/GetGhostTime, Corpse/GetType, World/getConfig, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetNextRandomRaidMember | method | Group/GetFirstMember, Group/GetMembersCount, GroupReference/next, shared_Util/urand, Unit.Main/HasInvisibilityAura, Unit.Main/IsHostileTo, WorldObject.Object/IsWithinDistInMap | — | — |
| CanUninviteFromGroup | method | Group/GetLeaderGuid, Group/IsAssistant, Group/IsLeader, Object/GetObjectGuid, ObjectGuid/operator== | WorldSession.GroupHandler/HandleGroupUninviteGuidOpcode, WorldSession.GroupHandler/HandleGroupUninviteOpcode | — |
| UpdateGroupLeaderFlag | method | Group/GetLeaderGuid, Object/GetObjectGuid, Object/HasFlag, ObjectGuid/operator!=, ObjectGuid/operator==, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | game_Group_Group/_updateLeaderFlag | — |
| SetBattleGroundRaid | method | GroupReference/setSubGroup | game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup, game_Group_Group/_addMember#2 | — |
| RemoveFromBattleGroundRaid | method | GroupReference/setSubGroup | game_Group_Group/Disband, game_Group_Group/_removeMember | — |
| SetOriginalGroup | method | Errors/PrintStacktraceAndThrow, GroupReference/setSubGroup | game_Group_Group/Disband, game_Group_Group/_addMember#2, game_Group_Group/_removeMember | — |
| UpdateTerainEnvironmentFlags | method | GridMap/getLiquidStatus#2, Map.Main/GetTerrain, SpellCaster/CastSpell#2, TerrainManager/GetLiquidType, Unit.Main/GetCollisionHeight, Unit.Main/GetMinSwimDepth, Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#2 | WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| SetCanParry | method | Player.StatSystem/UpdateParryPercentage | Spell.Effects/EffectParry | — |
| SetCanBlock | method | Player.StatSystem/UpdateBlockPercentage | Spell.Effects/EffectBlock | — |
| isContainedIn | method | — | — | — |
| CanUseBattleGroundObject | method | Unit.Main/GetClass, Unit.Main/HasAura#2, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsMounted, Unit.Main/IsTotalImmune | GameObject/Use, Spell.Effects/EffectDummy, Spell.Main/CheckCast | — |
| AutoStoreLoot | method | LootMgr/AllowedForPlayer, LootMgr/GetLootTarget, LootMgr/GetMaxSlotInLootFor, LootMgr/LootItemInSlot, Object/GetGUIDLow | AiBotAI.Bridge/BridgeHandleUseGameObject, AiBotAI.Loot/DoAutoLoot, WorldSession.LootHandler/DoLootRelease | — |
| CalculateTalentsPoints | method | Unit.Main/GetLevel, World/getConfig#2 | — | — |
| DoPlayerLearnSpell | ctor | — | — | — |
| operator() | method | — | — | — |
| LearnSpellHighRank | method | SpellMgr/Instance | ChatHandler.CharacterCommands/HandleLearnAllMyTalentsCommand, ChatHandler.CharacterCommands/HandleLearnCommand | — |
| GetSpellRank | method | SpellMgr/GetSkillLineAbilityMapBoundsBySpellId, SpellMgr/Instance | — | — |
| _LoadSkills | method | Database/PExecute#2, DBCStores/GetSkillRaceClassInfo, Field/GetUInt16, Log.Main/Out, Object/GetGUIDLow, ObjectMgr/GetSkillRangeType, QueryResult/Fetch, QueryResult/NextRow, SkillStatusData/SkillStatusData, SpellCaster/GetSkillMaxForLevel, Unit.Main/GetClass, Unit.Main/GetRace, WorldObject.Object/SetUInt32Value | — | character_skills |
| _LoadForgottenSkills | method | Field/GetUInt16, Log.Main/Out, Object/GetGUIDLow, QueryResult/Fetch, QueryResult/NextRow | — | — |
| LoadSkillsFromFields | method | Object/GetUInt32Value, SkillStatusData/SkillStatusData | — | — |
| CanEquipUniqueItem | method | — | — | — |
| LearnTalent | method | Log.Main/Out, Unit.Main/GetClassMask | CombatBotBaseAI/LearnRandomTalents, WorldSession.SkillHandler/HandleLearnTalentOpcode | — |
| UpdateFallInformationIfNeed | method | MovementInfo/HasMovementFlag, ObjectGuid/operator!= | WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes, WorldSession.MovementHandler/HandleMovementFlagChangeToggleAck, WorldSession.MovementHandler/HandleMovementOpcodes, WorldSession.MovementHandler/HandleMoveRootAck | — |
| HandleFall | method | Log.Main/Out, MovementInfo/GetFallTime, MovementInfo/GetPos, MovementInfo/GetTransportPos#2, MovementInfo/HasMovementFlag, ObjectGuid/operator!=, Unit.Main/GetMaxHealth, Unit.Main/GetTotalAuraModifier, Unit.Main/GetTotalAuraMultiplierByMiscMask, Unit.Main/HasAuraType, Unit.Main/IsDead, World/getConfig#2, WorldObject.Object/GetPositionZ, WorldObject.Object/UpdateAllowedPositionZ | WorldSession.MovementHandler/HandleMovementOpcodes | — |
| FallGround | method | Map.Main/GetHeight, Unit.Main/GetDeathState, Unit.Main/GetMaxHealth, Unit.Main/SetDeathState, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#2 | — | — |
| UnsummonPetTemporaryIfAny | method | CharmInfo/GetPetNumber, Pet.Main/IsControlled, Pet.Main/IsTemporarySummoned, Pet.Main/Unsummon, Unit.Main/GetCharmInfo, Unit.Main/GetPet | spell_special/OnInit | — |
| ResummonPetTemporaryUnSummonedIfAny | method | Pet.Main/LoadPetFromDB, Pet.Main/Pet, Unit.Main/GetPetGuid | WorldSession.MovementHandler/ExecuteTeleportNear, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| IsPetNeedBeTemporaryUnsummoned | method | Object/IsInWorld, Unit.Main/IsAlive, Unit.Main/IsMounted, Unit.Main/IsTaxiFlying, World/getConfig | Pet.Main/LoadPetFromDB | — |
| _SaveBGData | method | Database/CreateStatement, Object/GetGUIDLow, SqlPreparedStatement/Execute#2, SqlPreparedStatement/operator=, SqlStatement/addFloat, SqlStatement/addUInt32, SqlStatementID/SqlStatementID | — | character_battleground_data |
| SendClearCooldown | method | ByteBuffer/operator<<#10, Object/GetObjectGuid, ObjectGuid/operator<<, WorldPacket/WorldPacket#4 | Spell.Main/finish, WorldSession.PetHandler/HandlePetCastSpellOpcode | — |
| SendClearAllCooldowns | method | Object/GetObjectGuid, ObjectGuid/operator<<, WorldPacket/WorldPacket#4 | Pet.Main/RemoveAllCooldowns | — |
| SendSpellCooldown | method | ByteBuffer/operator<<#10, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Creature.Main/AddCooldown, Map.ScriptCommands/ScriptCommand_AddSpellCooldown | — |
| SendSpellRemoved | method | ByteBuffer/operator<<#13, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendChannelUpdate | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4 | ChatHandler.DebugCommands/HandleDebugSendNextChannelSpellVisualCommand, ChatHandler.DebugCommands/HandleSendSpellChannelVisualCommand, Spell.Main/SendChannelUpdate, Unit.Main/CancelSpellChannelingAnimationInstantly | — |
| UpdateChannelStartPosition | method | Spell.Main/UpdateCastStartPosition | WorldSession.MovementHandler/HandleMoveNotActiveMoverOpcode | — |
| HasMovementFlag | method | MovementInfo/HasMovementFlag | ChatHandler.CharacterCommands/HandleCheatStatusCommand, MovementAnticheat/IsTeleportAllowed3D | — |
| SetHomebindToLocation | method | Database/PExecute#2, Object/GetGUIDLow | Spell.Effects/EffectBind | character_homebind |
| TeleportToHomebind | method | GridDefines/IsValidMapCoord#3, Log.Main/Out, MapEntry/Instanceable, Object/GetGUIDLow, ObjectMgr/GetItemPrototype, ObjectMgr/GetPlayerInfo, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetClass, Unit.Main/GetRace, WorldLocation/WorldLocation#2 | Map.Main/PermBindAllPlayers, Map.Main/TeleportAllPlayersTo, Spell.Effects/EffectTeleportUnits, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.Main/LogoutPlayer, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| GetSelectedUnit | method | Map.Main/GetUnit, WorldObject.Object/GetMap | PetAI/CanAttack | — |
| GetSelectedCreature | method | Map.Main/GetCreature, WorldObject.Object/GetMap | BattleGroundAV/HandleCommand | — |
| GetSelectedPlayer | method | Map.Main/GetPlayer, WorldObject.Object/GetMap | — | — |
| GetObjectByTypeMask | method | Map.Main/GetCreature, Map.Main/GetDynamicObject, Map.Main/GetGameObject, Map.Main/GetPet, Object/GetObjectGuid, Object/IsInWorld, ObjectAccessor/FindPlayer, ObjectGuid/GetHigh, ObjectGuid/operator==, WorldObject.Object/GetMap | ChatHandler.UnitCommands/HandleGetAngleCommand, ChatHandler.UnitCommands/HandleGetDistanceCommand, ChatHandler.UnitCommands/HandleGPSCommand, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, WorldSession.QuestHandler/HandleQuestgiverChooseRewardOpcode, WorldSession.QuestHandler/HandleQuestgiverQueryQuestOpcode, WorldSession.QuestHandler/HandleQuestgiverRequestRewardOpcode, WorldSession.QuestHandler/HandleQuestgiverStatusQueryOpcode | — |
| SetRestType | method | World/IsFFAPvPRealm, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| SendDuelCountdown | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.DuelHandler/HandleDuelAcceptedOpcode | — |
| RemoveAI | method | PlayerAI/Remove | PlayerAI/UpdateAI#2 | — |
| RemoveTemporaryAI | method | WorldSession.Main/GetBot | Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/ModPossess | — |
| SetControlledBy | method | PlayerAI/PlayerControlledAI, WorldSession.Main/GetBot | Unit.SpellAuras/HandleModCharm | — |
| ChangeRace | method | Log.Main/Out, Object/GetObjectGuid, ObjectMgr/GetPlayerInfo, Unit.Main/GetClass, Unit.Main/GetRace, Unit.Main/RemoveSpellsCausingAura, World/InvalidatePlayerDataToAllClient, WorldLocation/WorldLocation#2, WorldObject.Object/SetByteValue, WorldSession.Main/LogoutPlayer | ChatHandler.CharacterCommands/HandleCharacterChangeRaceCommand | — |
| GetPriestSpellForRace | function | — | — | — |
| GetCapitalReputationForRace | function | — | — | — |
| ConvertSpell | method | ActionButton/GetAction, ActionButton/GetType, ActionButton/SetActionAndType, Log.Main/Out, MasterPlayer.Main/GetActionButtons, WorldSession.Main/GetMasterPlayer | — | — |
| ChangeSpellsForRace | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, ObjectMgr/GetPlayerInfo, Unit.Main/GetClass | — | — |
| ChangeItemsForRace | method | game_Objects_Item/ChangeEntry, game_Objects_Item/GetProto, Log.Main/Out, Object/GetEntry, ObjectMgr/GetItemPrototype, ObjectMgr/GetMountDataByEntry, ObjectMgr/GetMountItemEntry, ObjectMgr/GetOppositeRace, ObjectMgr/GetRandomMountForRace | — | — |
| ChangeReputationsForRace | method | FactionEntry/GetIndexFitTo, Log.Main/Out, ObjectMgr/GetFactionEntry, ReputationMgr/GetBaseReputation, ReputationMgr/GetState, ReputationMgr/GetState#2, ReputationMgr/GetStateList, ReputationMgr/SendState, ReputationMgr/SetReputation, Unit.Main/GetClassMask | — | — |
| ChangeQuestsForRace | method | Log.Main/Out, ObjectMgr/GetQuestTemplate, QuestDef/GetQuestId, QuestDef/HasSpecialFlag, QuestDef/IsActive, QuestStatusData/QuestStatusData | — | — |
| IsImmuneToSpellEffect | method | SpellEntry/IsPositiveEffect, Unit.Main/IsImmuneToSpellEffect | — | — |
| AddItem | method | game_Objects_Item/GenerateItemRandomPropertyId | instance_naxxramas.Main/GossipSelect_npc_MasterCraftsmanOmarion | — |
| SendDestroyGroupMembers | method | Group/GetMemberSlots, Map.Main/GetPlayer, Object/GetObjectGuid, ObjectGuid/operator<<, ObjectGuid/operator==, PlayerBroadcaster/RemoveListener, WorldObject.Object/GetMap, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| RefreshBitsForVisibleUnits | method | ByteBuffer/operator<<#7, Object/GetPackGUID, ObjectGuid/operator<<#2, UpdateData/AddUpdateBlockAndGetBuffer, UpdateData/Send, UpdateData/UpdateData, WorldObject.Object/BuildValuesUpdate | — | — |
| SetSession | method | Errors/PrintStacktraceAndThrow, GossipDef/PlayerMenu | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| InterruptSpellsWithCastItem | method | EventProcessor/GetEvents, Spell.Main/cancel, Spell.Main/ClearCastItem, Spell.Main/getState, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell, SpellEvent/GetSpell | game_Objects_Item/RemoveFromWorld, WorldSession.ItemHandler/HandleSellItemOpcode | — |
| GetShortDescription | method | Object/GetGUIDLow, WorldSession.Main/GetAccountId, WorldSession.Main/GetRemoteAddress, WorldSession.Main/GetUsername | game_Group_Group/CountSingleLooterRoll, game_Group_Group/CountTheRoll, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.Main/ProcessAnticheatAction | — |
| LootMoney | method | LootMgr/GetLootTarget, Object/GetGuidStr, Object/GetObjectGuid, Object/HasFlag, ObjectGuid/ObjectGuid | AiBotAI.Bridge/BridgeHandleUseGameObject, AiBotAI.Loot/DoAutoLoot, WorldSession.LootHandler/HandleLootMoneyOpcode | — |
| RewardHonor | method | Creature.Main/IsCivilian, Creature.Main/IsRacialLeader, HonorMgr/Add, HonorMgr/DishonorableKillPoints, Object/GetTypeId, Unit.Main/GetLevel, Unit.Main/HasAuraType, World/getConfig, World/GetWowPatch | ChatHandler.CharacterCommands/HandleHonorAddKillCommand, game_Group_Group/RewardGroupAtKill_helper | — |
| RewardHonorOnDeath | method | Formulas/xp_in_group_rate, Group/GetMemberSlots, HonorMgr/Add, HonorMgr/HonorableKillPoints, Map.Main/GetPlayer, Unit.Main/HasAuraType, Unit.Main/IsAlive, World/getConfig, World/GetWowPatch, WorldObject.Object/GetMap | Unit.Main/Kill | — |
| OnReceivedItem | method | game_Objects_Item/GetProto, World/getConfig#4 | game_Group_Group/CountSingleLooterRoll, game_Group_Group/CountTheRoll, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode | — |
| HasFreeBattleGroundQueueId | method | World/getConfig#4 | game_Group_Group/CanJoinBattleGroundQueue, WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| TaxiStepFinished | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, FlightPathMovementGenerator/GetPath, FlightPathMovementGenerator/SkipCurrentNode, Log.Main/Out, Object/IsInWorld, ObjectMgr/GetTaxiMountDisplayId, ObjectMgr/GetTaxiNodeEntry, ObjectMgr/GetTaxiPath, PlayerTaxi/ClearTaxiDestinations, PlayerTaxi/GetTaxiDestination, PlayerTaxi/GetTaxiSource, PlayerTaxi/NextTaxiDestination, PlayerTaxi/SetTaximaskNode, Unit.Main/GetMotionMaster, WaypointMovementGenerator/Interrupt, WaypointMovementGenerator/SetCurrentNodeAfterTeleport, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket, WorldSession.TaxiHandler/SendDoFlight | WaypointMovementGenerator/Finalize | — |
| HandleStealthedUnitsDetection | method | AnyStealthedCheck/AnyStealthedCheck, Camera/GetBody, Object/GetObjectGuid, Object/ToPlayer, PlayerBroadcaster/AddListener, PlayerBroadcaster/RemoveListener, Unit.Main/IsVisibleForOrDetect, World/getConfig#2, WorldObject.Object/DestroyForPlayer, WorldObject.Object/FindMap, WorldObject.Object/SendCreateUpdateToPlayer | — | — |
| IsInVisibleList | method | Object/GetObjectGuid | Camera/Event_ViewPointVisibilityChanged, game_Group_Group/AddMember, game_Group_Group/UpdatePlayerOutOfRange, Map.Main/SendInitSelf, Unit.Main/IsVisibleForOrDetect | — |
| GetSocial | method | Errors/PrintStacktraceAndThrow, MasterPlayer.Main/GetSocial, WorldSession.Main/GetMasterPlayer | Map.Main/CrashUnload, Spell.Effects/EffectDuel, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode | — |
| FindSocial | method | MasterPlayer.Main/GetSocial, WorldSession.Main/GetMasterPlayer | — | — |
| DeletePacketBroadcaster | method | MovementBroadcaster/RemovePlayer, PlayerBroadcaster/FreeAtLogout, World/GetBroadcaster | WorldSession.Main/Update | — |
| CreatePacketBroadcaster | method | Errors/PrintStacktraceAndThrow, MovementBroadcaster/RegisterPlayer, Object/GetObjectGuid, World/GetBroadcaster, WorldSession.Main/GetSocket | PlayerBotAI/SpawnNewPlayer, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| AddGCD | method | Object/GetFloatValue, Object/GetObjectGuid, SpellCaster/AddGCD, SpellEntry/HasAttribute, World/getConfig#4 | — | — |
| AddCooldown | method | ByteBuffer/operator<<#10, CooldownContainer/AddCooldown, CooldownContainer/end, CooldownContainer/erase, CooldownContainer/FindByCategory, CooldownContainer/FindBySpellId, CooldownData/GetItemId, CooldownData/GetSpellId, CooldownData/IsCatCDExpired, CooldownData/IsPermanent, CooldownData/IsSpellCDExpired, Log.Main/Out, Object/GetFloatValue, Object/GetObjectGuid, ObjectGuid/operator<<, ObjectMgr/GetItemPrototype, SpellEntry/HasAttribute, SpellEntry/HasAttribute#4, World/GetCurrentClockTime, WorldPacket/WorldPacket#4 | ChatHandler.TeleportCommands/HandleUnstuckCommand, GameObject/FinishRitual | — |
| RemoveSpellCooldown | method | CooldownContainer/RemoveBySpellId | AiBotAI.Combat/DrinkAndEat, BattleBotAI.Main/DrinkAndEat, PartyBotAI/DrinkAndEat | — |
| RemoveSpellCategoryCooldown | method | CooldownContainer/end, CooldownContainer/erase, CooldownContainer/FindByCategory, CooldownData/GetSpellId | — | — |
| RemoveAllCooldowns | method | CooldownContainer/begin, CooldownContainer/end, CooldownContainer/erase, CooldownData/GetSpellId, CooldownData/IsPermanent | ChatHandler.UnitCommands/HandleCooldownClearClientSideCommand | — |
| LockOutSpells | method | ByteBuffer/append#3, ByteBuffer/ByteBuffer, ByteBuffer/operator<<#10, Object/GetObjectGuid, ObjectGuid/operator<<, SpellCaster/GetExpireTime, SpellCaster/LockOutSpells, SpellEntry/GetSpellSchoolMask, SpellEntry/HasAttribute, SpellMgr/GetSpellEntry, SpellMgr/Instance, World/GetCurrentClockTime, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| RemoveSpellLockout | method | SpellEntry/GetSpellSchoolMask, SpellEntry/HasAttribute, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| CastHighestStealthRank | method | SpellCaster/CastSpell, SpellCaster/IsSpellReady, SpellEntry/IsFitToFamily, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/IsImmuneToSpell | spell_rogue/OnEffectExecute#2 | — |
| PlayerLogHeaderToConsole | method | Object/GetGUIDLow, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer, WorldSession.Main/GetRemoteAddress | — | — |
| PlayerLogHeaderToFile | method | Object/GetGUIDLow, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer, WorldSession.Main/GetRemoteAddress | — | — |
| IsPlayerLoggingEnabledToDB | function | Log.Main/GetDbLevel, World/getConfig | — | — |
| PlayerLogToDB | function | Database/CreateStatement, Object/GetGUIDLow, SqlPreparedStatement/Execute#2, SqlStatement/addFloat, SqlStatement/addNull, SqlStatement/addString#2, SqlStatement/addString#3, SqlStatement/addUInt32, SqlStatementID/SqlStatementID, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer, WorldSession.Main/GetRemoteAddress | — | logs_player |
| Player | method | Log.Main/OutTimestamp, WorldSession.Main/GetAccountId | ChatHandler.Chat/ExecuteCommand, game_Battlegrounds_BattleGround/EndBattleGround, game_Group_Group/CountSingleLooterRoll, game_Group_Group/CountTheRoll, Map.Main/PermBindAllPlayers, Spell.Effects/EffectEnchantItemPerm, Spell.Effects/EffectEnchantItemTmp, World/LogChat, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMasterGiveOpcode, WorldSession.MovementHandler/ExecuteTeleportNear, WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes, WorldSession.MovementHandler/HandleMoveKnockBackAck, WorldSession.MovementHandler/HandleMovementFlagChangeToggleAck, WorldSession.MovementHandler/HandleMovementOpcodes, WorldSession.MovementHandler/HandleMoveNotActiveMoverOpcode, WorldSession.MovementHandler/HandleMoveRootAck, WorldSession.MovementHandler/HandleMoverRelocation, WorldSession.MovementHandler/HandleMoveTeleportAckOpcode, WorldSession.MovementHandler/HandleMoveWorldportAck, WorldSession.MovementHandler/HandleSetActiveMoverOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/MoveItems | — |
| Player#2 | method | Log.Main/OutTimestamp, WorldSession.Main/GetAccountId | Map.Main/CrashUnload, MovementAnticheat/Finalize, MovementAnticheat/HandleSplineDone, WorldSession.CharacterHandler/HandleCharDeleteOpcode, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.Main/ForcePlayerLogoutDelay, WorldSession.Main/LogoutPlayer, WorldSession.Main/ProcessAnticheatAction | — |
| Player#3 | method | Log.Main/OutTimestamp | AuctionHouseMgr/SendAuctionWonMail, ChatHandler.Chat/ExecuteCommand, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.CharacterHandler/HandleChangePlayerNameOpcodeCallBack, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.MiscHandler/HandleWardenDataOpcode | — |
| Player#4 | method | Log.Main/OutTimestamp | — | — |
| ClearTemporaryWarWithFactions | method | ObjectMgr/GetFactionEntry, ReputationMgr/GetRank, ReputationMgr/SetAtWar#2 | Unit.Main/ClearInCombat | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_action`: guid int(11) unsigned PK, button tinyint(3) unsigned PK, action int(11) unsigned, type tinyint(3) unsigned
- `character_aura`: guid int(11) unsigned PK, caster_guid bigint(20) unsigned PK, item_guid int(11) unsigned PK, spell int(11) unsigned PK, stacks int(11) unsigned, charges int(11) unsigned, base_points0 float, base_points1 float, base_points2 float, periodic_time0 int(11) unsigned, periodic_time1 int(11) unsigned, periodic_time2 int(11) unsigned, max_duration int(11), duration int(11), effect_index_mask tinyint(3) unsigned
- `character_battleground_data`: guid int(11) unsigned PK, instance_id int(11) unsigned, team int(11) unsigned, join_x float, join_y float, join_z float, join_o float, join_map int(11)
- `character_deleted_items`: id int(11) unsigned PK, player_guid int(11) unsigned, item_id mediumint(8) unsigned, stack_count mediumint(8) unsigned
- `character_forgotten_skills`: guid int(11) unsigned PK, skill mediumint(9) unsigned PK, value mediumint(9) unsigned
- `character_gifts`: guid int(20) unsigned, item_guid int(11) unsigned PK, item_id int(20) unsigned, flags int(20) unsigned
- `character_homebind`: guid int(11) unsigned PK, map int(11) unsigned, zone int(11) unsigned, position_x float, position_y float, position_z float
- `character_instance`: guid int(11) unsigned PK, instance int(11) unsigned PK, permanent tinyint(1) unsigned
- `character_inventory`: guid int(11) unsigned, bag int(11) unsigned, slot tinyint(3) unsigned, item_guid int(11) unsigned PK, item_id int(11) unsigned
- `character_pet`: id int(11) unsigned PK, entry int(11) unsigned, owner_guid int(11) unsigned, display_id int(11) unsigned?, created_by_spell int(11) unsigned, pet_type tinyint(3) unsigned, level int(11) unsigned, xp int(11) unsigned, react_state tinyint(1) unsigned, loyalty_points int(11), loyalty int(11) unsigned, training_points int(11), name varchar(100)?, renamed tinyint(1) unsigned, slot int(11) unsigned, current_health int(11) unsigned, current_mana int(11) unsigned, current_happiness int(11) unsigned, save_time bigint(20) unsigned, reset_talents_cost int(11) unsigned, reset_talents_time bigint(20) unsigned, action_bar_data longtext?, teach_spell_data longtext?
- `character_queststatus`: guid int(11) unsigned PK, quest int(11) unsigned PK, status int(11) unsigned, rewarded tinyint(1) unsigned, explored tinyint(1) unsigned, timer bigint(20) unsigned, mob_count1 int(11) unsigned, mob_count2 int(11) unsigned, mob_count3 int(11) unsigned, mob_count4 int(11) unsigned, item_count1 int(11) unsigned, item_count2 int(11) unsigned, item_count3 int(11) unsigned, item_count4 int(11) unsigned, reward_choice int(11) unsigned
- `character_reputation`: guid int(11) unsigned PK, faction int(11) unsigned PK, standing int(11), flags int(11)
- `character_skills`: guid int(11) unsigned PK, skill mediumint(9) unsigned PK, value mediumint(9) unsigned, max mediumint(9) unsigned
- `character_social`: guid int(11) unsigned PK, friend int(11) unsigned PK, flags tinyint(1) unsigned PK
- `character_spell`: guid int(11) unsigned PK, spell int(11) unsigned PK, active tinyint(3) unsigned, disabled tinyint(3) unsigned
- `character_spell_cooldown`: guid int(11) unsigned PK, spell int(11) unsigned PK, spell_expire_time bigint(20) unsigned, category int(11) unsigned, category_expire_time bigint(20) unsigned, item_id int(11) unsigned
- `character_stats`: guid int(11) unsigned PK, max_health int(10) unsigned, max_power1 int(10) unsigned, max_power2 int(10) unsigned, max_power3 int(10) unsigned, max_power4 int(10) unsigned, max_power5 int(10) unsigned, max_power6 int(10) unsigned, max_power7 int(10) unsigned, strength float, agility float, stamina float, intellect float, spirit float, armor int(10), holy_res int(10), fire_res int(10), nature_res int(10), frost_res int(10), shadow_res int(10), arcane_res int(10), block_chance float, dodge_chance float, parry_chance float, crit_chance float, ranged_crit_chance float, spell_crit_chance float, attack_power int(10) unsigned, ranged_attack_power int(10) unsigned, spell_damage int(10) unsigned, spell_healing int(10) unsigned
- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?
- `group_instance`: leader_guid int(11) unsigned PK, instance int(11) unsigned PK, permanent tinyint(1) unsigned
- `guild_eventlog`: guild_id int(11) PK, log_guid int(11) PK, event_type tinyint(1), player_guid1 int(11), player_guid2 int(11), new_rank tinyint(2), timestamp bigint(20)
- `guild_member`: guild_id int(6) unsigned, guid int(11) unsigned PK, rank tinyint(2) unsigned, player_note varchar(255), officer_note varchar(255)
- `item_instance`: guid int(10) unsigned PK, item_id mediumint(8) unsigned, owner_guid int(10) unsigned, creator_guid int(10) unsigned, gift_creator_guid int(10) unsigned, count int(10) unsigned, duration int(10), charges tinytext?, flags mediumint(8) unsigned, enchantments text, random_property_id smallint(5), durability smallint(5) unsigned, text int(10) unsigned, generated_loot tinyint(4)?
- `item_loot`: guid int(11) unsigned PK, owner_guid int(11) unsigned, item_id int(11) unsigned PK, amount int(11) unsigned, property int(11)
- `logs_player`: id int(10) unsigned PK, time timestamp, type enum('Basic','WorldPacket','Chat','BG','Character','Honor','RA','DBError','DBErrorFix','ClientIds','Loot','LevelUp','Performance','MoneyTrade','GM','GMCritical','ChatSpam','Anticheat'), subtype varchar(20)?, account int(10) unsigned, ip varchar(16)?, guid int(11)?, name varchar(20)?, map int(10) unsigned?, pos_x float?, pos_y float?, pos_z float?, text varchar(512)
- `mail`: id int(11) unsigned PK, message_type tinyint(3) unsigned, stationery tinyint(3), mail_template_id mediumint(8) unsigned, sender_guid int(11) unsigned, receiver_guid int(11) unsigned, subject longtext?, item_text_id int(11) unsigned, has_items tinyint(3) unsigned, expire_time bigint(40), deliver_time bigint(40), money int(11) unsigned, cod int(11) unsigned, checked tinyint(3) unsigned
- `mail_items`: mail_id int(11) unsigned PK, item_guid int(11) unsigned PK, item_id int(11) unsigned, receiver_guid int(11) unsigned
- `petition`: owner_guid int(10) unsigned PK, petition_guid int(10) unsigned?, charter_guid int(10) unsigned?, name varchar(255)
- `petition_sign`: owner_guid int(10) unsigned, petition_guid int(11) unsigned PK, player_guid int(11) unsigned PK, player_account int(11) unsigned

*`?` = nullable, `PK` = primary key column.*

