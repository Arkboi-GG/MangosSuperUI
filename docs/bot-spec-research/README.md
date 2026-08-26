# AiBot specialization research baseline

Status: approved implementation contract. This manifest is the canonical source
for the generated core talent catalog and SuperUI's embedded talent read model.
Deployment and live database migration remain separate operator steps.

## Outcome

The implemented baseline contains 27 persistent specialization profiles: three for every Vanilla class. Each profile has:

- an exact one-point-per-level purchase order from level 10 through 60;
- a 51-point level-60 allocation;
- solo and five-player combat priorities;
- low-level fallbacks, resource rules, interrupts, dispels, crowd-control safety, defensives, and utility;
- Vanilla-only spell and mechanic constraints; and
- sources plus explicit disagreements where raid, PvP, respec, and leveling guides differ.

The detailed matrices are split into:

- [Warrior, Paladin, and Shaman](plate_hybrids.md)
- [Hunter, Rogue, and Druid](physical_nature.md)
- [Mage, Warlock, and Priest](casters.md)
- [Normalized TalentID profile manifest](talent_profiles.json)

## Validation result

All 27 purchase orders were replayed against the Linux server's installed build-5875 `Talent.dbc` at `/home/wowvmangos/vmangos/run/data/5875/dbc/Talent.dbc`.

- Profiles passed: 27/27.
- Purchases passed: 1,377/1,377.
- Every profile covers levels 10–60 exactly once and ends at 51 points.
- No purchase exceeds a talent's live maximum rank.
- No purchase violates a live tier gate or prerequisite link.
- Tree membership and class masks match the live `TalentTab.dbc` data.

The normalized manifest received a separate validation pass after generation: its 27 stable profile IDs are unique, every chunk expands to 51 purchases, every TalentID belongs to the declared class, and the computed points in live tree order match the declared distribution.

The live data caught real problems in secondary web data, including an incorrect one-rank declaration for Mage Shatter and a three-rank Rogue Murder recommendation even though build 5875 has only two ranks. The matrices use the server data in both cases.

## Profile summary

Tree distributions use each class's in-game tree order.

| Profile | Level-60 distribution | Operational identity |
|---|---:|---|
| Warrior — Arms | 31 Arms / 20 Fury / 0 Protection | Two-handed DPS; ordinary-dungeon backup tank |
| Warrior — Fury | 20 Arms / 31 Fury / 0 Protection | Two-handed leveling DPS; gear-aware dual wield is a later variant |
| Warrior — Protection | 11 Arms / 4 Fury / 36 Protection | Shield tank and control |
| Paladin — Holy | 35 Holy / 11 Protection / 5 Retribution | Primary healer with conservative melee support |
| Paladin — Protection | 11 Holy / 31 Protection / 9 Retribution | Shield/AoE dungeon tank; no Vanilla taunt |
| Paladin — Retribution | 11 Holy / 8 Protection / 32 Retribution | Slow two-handed support DPS |
| Hunter — Beast Mastery | 31 Beast Mastery / 20 Marksmanship / 0 Survival | Pet-centered ranged DPS and soloing |
| Hunter — Marksmanship | 20 Beast Mastery / 31 Marksmanship / 0 Survival | Party-oriented ranged DPS |
| Hunter — Survival | 0 Beast Mastery / 20 Marksmanship / 31 Survival | Ranged-first control and utility DPS |
| Rogue — Assassination | 31 Assassination / 8 Combat / 12 Subtlety | Dagger, poison, crit, and finisher DPS |
| Rogue — Combat | 19 Assassination / 32 Combat / 0 Subtlety | Durable, weapon-flexible sustained DPS |
| Rogue — Subtlety | 21 Assassination / 0 Combat / 30 Subtlety | Hemorrhage stealth/control hybrid |
| Shaman — Elemental | 31 Elemental / 0 Enhancement / 20 Restoration | Ranged damage, interrupts, and off-healing |
| Shaman — Enhancement | 0 Elemental / 31 Enhancement / 20 Restoration | Two-handed melee support and off-healing |
| Shaman — Restoration | 0 Elemental / 5 Enhancement / 46 Restoration | Primary healer and encounter-specific totem support |
| Mage — Arcane | 31 Arcane / 0 Fire / 20 Frost | Mana/proc-driven ranged DPS and control |
| Mage — Fire | 17 Arcane / 31 Fire / 3 Frost | High-damage ranged DPS with threat restraint |
| Mage — Frost | 18 Arcane / 0 Fire / 33 Frost | Control, kiting, and safe AoE |
| Warlock — Affliction | 31 Affliction / 20 Demonology / 0 Destruction | Efficient DoTs, drains, and pet sustain |
| Warlock — Demonology | 20 Affliction / 31 Demonology / 0 Destruction | Pet durability/control and Soul Link |
| Warlock — Destruction | 17 Affliction / 0 Demonology / 34 Destruction | Direct-damage caster with strict threat/mana gates |
| Priest — Discipline | 31 Discipline / 15 Holy / 5 Shadow | Mitigation, efficient healing, and Power Infusion |
| Priest — Holy | 16 Discipline / 30 Holy / 5 Shadow | Primary throughput healer; intentionally skips Lightwell |
| Priest — Shadow | 20 Discipline / 0 Holy / 31 Shadow | Mana-aware Shadow DPS with emergency form exit |
| Druid — Balance | 31 Balance / 0 Feral Combat / 20 Restoration | Moonkin caster and off-healer |
| Druid — Feral Combat | 14 Balance / 32 Feral Combat / 5 Restoration | One talent profile with explicit Cat-DPS or Bear-tank role |
| Druid — Restoration | 20 Balance / 0 Feral Combat / 31 Restoration | Primary healer and conservative off-DPS |

## Approved implementation decisions

### 1. Persist specialization identity

Recommended: store a stable class-local profile ID on each bot and keep active group role separate. Do not infer identity only from current talent totals: low-level bots have too few points, several optimal profiles are hybrids, and Feral needs a separate Cat/Bear role.

### 2. Repair existing bots without destructive resets

Recommended migration policy:

1. Assign a persisted profile to a zero-talent bot, then idempotently buy every legal missing point through its current level.
2. Continue a partially built bot only when its learned ranks are compatible with the selected profile.
3. Preserve and flag a conflicting existing build instead of silently resetting it.

For the current organically leveled zero-talent population, the proposed default is deterministic round-robin assignment across the three class profiles so restarts do not change a bot and every spec is represented. A weighted distribution can replace this if the realm should favor tanks, healers, or stronger leveling specs.

### 3. Accept bot-oriented builds rather than raid-respec templates

The baseline optimizes autonomous leveling plus five-player usefulness without scheduled respecs. That intentionally differs from a raid-only optimum. Notable choices are ranged-first Survival, weapon-flexible Combat Rogue, two-handed leveling Fury, 21/0/30 Subtlety, and 16/30/5 Holy Priest. Warrior Arms uses axe specialization, and its damage role locks automated gearing to two-handed axes while retaining a safe degraded combat policy until one is equipped. When explicitly assigned the backup-tank role, it switches the equipment policy to a one-handed weapon and shield so its tank actions remain usable.

### 4. Replace the fixed rotation-list model

The copied combat base cannot become reliable by adding a longer ordered spell list. The new decision layer needs typed state for known spell ranks, cast/channel lock, resource reserve, target time-to-live, party role, threat, crowd control, target count, range/position, weapon and stance/form, combo points, pet state, totems, auras/procs, and dispellable effects.

Recommended structure:

- one shared class mechanics module per class;
- three spec policy modules per class;
- a common action scorer/executor with explicit suppress reasons; and
- telemetry before balance tuning.

### 5. Make SuperUI display authoritative state

The UI should show the persisted profile, active role, points by tree, every current/max talent rank, the next planned purchase and level, and any migration/validation conflict. It should read actual talent state from the core/database API and never infer talents from known spells.

## Proposed implementation order after approval

1. Add the persisted profile and migration-safe schema/API contract.
2. Add the DBC-driven, idempotent talent planner/spender and repair existing zero-talent bots.
3. Add the typed combat context, action executor, suppress-reason logging, and telemetry.
4. Implement and test the nine class mechanics modules and 27 spec policies in class-sized vertical slices.
5. Expose actual and planned talents through SuperUI.

The release gate should include unit tests for every profile at levels 9–60, live-core integration tests for login/level-up repair, and scenario tests for interrupts, threat, crowd control, resource starvation, forms/stances, pets, and healing triage.
