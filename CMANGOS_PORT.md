# CMaNGOS Port Plan — MangosSuperUI on CMaNGOS-TBC

**Goal:** MangosSuperUI drives *both* emulators — the existing VMaNGOS 1.12 stack and the **CMaNGOS-TBC (2.4.3, build 8606)** stack now installed beside it at `/home/wowvmangos/cmangos` — selected by an emulator profile. The bulk of the mechanical porting work is executed by a local **Qwen3.8-27B** running for days under a Python harness; **Claude (frontier) is the architect of the seams and the standing auditor** of everything the local model produces.

> **Revision 2 (2026-08-18).** The original plan targeted mangos-classic; the actual install is **mangos-tbc**. All schema numbers below are re-measured from the **live** databases on the box (SSH + information_schema, 2026-08-18). The DB/ops port is nearly identical in shape to the classic analysis; the real scope change is the **client**: 2.4.3 vs 1.12.1 touches every client-asset pipeline (§1.4). The classic-DDL research files remain in owner-local `tools/port/data/` — a future classic profile would reuse ~95% of the port machinery. The root `tools/` tree is deliberately ignored and is not part of the public repository.

Install facts (from the install session): DBs `tbcmangos` / `tbccharacters` / `tbcrealmd` / `tbclogs` on the same MariaDB, same `mangos`/`mangos` user; realmd **3725**, world **8086** (vmangos keeps 3724/8085 — all four services run in parallel today); systemd units **`cmangos-realmd`** / **`cmangos-mangosd`** (mangosd wrapped in `screen -DmS cmangos-mangosd`; live console via `screen -r`, non-interactive via `screen -X stuff $'…\r'`); layout `source/` (plain mangos-tbc, no playerbots) + `build/` + `run/{bin,etc,data}` + `database/` (tbc-db) + extracted 2.4.3 client; account `ADMIN`/gmlevel 3, `expansion=1`; **mmaps mandatory** (core asserts with `mmap.enabled=0`) and already generated. RA is **staged**: `Ra.Enable=1`, `Ra.Port=3446` written to `run/etc/mangosd.conf` (backup `mangosd.conf.bak.pre-ra`) — takes effect on next mangosd restart, which needs the sudoers extension in §4 Phase 0.

---

## 1. Ground truth (measured)

### 1.1 The coupling, by the numbers

| Surface | Scale | Where |
|---|---|---|
| SQL literals | 965 statements across 73 `.cs` files | everywhere; Dapper raw SQL |
| `ConnectionFactory` call sites | ~330 across 40 files | `Models/ConnectionFactory.cs` five fixed methods |
| Cross-DB qualified literals (`mangos.`, `vmangos_admin.`, …) | ~25 | Baseline/LootTuner/Lootifier/NpcDevBaseline/Players/Accounts |
| `patch`/`build`/`patch_min`/`patch_max` predicates | ~145 sites, 13+ files | 57× `MAX(patch)` idiom, 32× `ORDER BY patch DESC`, 17× `patch = 0`, 38× `patch_min/max`, `build = 5875` hardcoded in 10+ places, `build = 4222` in `ZoneSafetyMap.cs:522` |
| RA protocol | prompt `mangos>` + login handshake hardcoded | `Services/RaService.cs:82-96,217,246-251` |
| GM command grammar (parse + reverse commands) | ~15 command families | `Services/StateCaptureService.cs:89-252`, `AuditService.cs:257-271`, `wwwroot/data/commands.json` (~128 commands) |
| systemd units + process names | units hardcoded `mangosd`/`realmd` | `Services/ProcessManagerService.cs:28-34`; TBC units are `cmangos-*` and mangosd runs under screen (/proc `comm` detection still finds `mangosd`) |
| Absolute-path fallbacks | ~40 `?? "/home/wowvmangos/..."` | DbcService, PatchBuilder, ServerData, WorldEditor, Wiki, etc. |
| mangosd.conf metadata | 446 keys mapped (vmangos ~601-key conf) | `wwwroot/data/config-metadata.json`; live TBC conf has **360 keys** with different names |
| App-owned admin schema | 45 tables live in `vmangos_admin` (+ `custom_spell_meta` in **world** DB, 9 `superui_*` RTS tables in **characters**) | `sql/vmangos_admin_schema.sql` + self-provisioning services |

### 1.2 Schema diff — live vmangos vs live mangos-tbc, app-touched tables only

The app queries **51 emulator tables**. Live-vs-live (owner-local
`tools/port/data/port_relevant_diff_tbc.json`):
**36 COL-DELTAS · 11 TBC-MISSING · 3 IDENTICAL · 1 VM-MISSING** — essentially the same shape as the classic analysis. Highlights:

- **Renames dominate.** cmangos CamelCase vs vmangos snake_case; **lowercase + strip-underscores auto-maps ~90%** of used columns. Curated exceptions carry the rest (`set_id→itemset`, `wander_distance→spawndist`, `trainer_id→TrainerTemplateId`, `type→CreatureType`, `level_min/max→Min/MaxLevel`, `item_id→itemEntry`, `officer_note→offnote`, the reshaped `account_banned`: `bandate→banned_at`, `unbandate→expires_at`, `banreason→reason`, `id→account_id`…).
- **No `patch`/`build` columns anywhere** — ~145 sites route through one predicate seam; PKs collapse `(entry,patch)→(entry)`, `(entry,build)→(Id)`.
- **`spell_template` is live with 186 columns** — the TBC Spell.dbc layout (`Id`, `SchoolMask`, `SpellName1-16` locale spread, `Rank1-16`, `AttributesEx5/6`, `AttributesServerside`, `IsServerSide`). Spell editing ports with a bigger column map; `entry→Id`, `name→SpellName`, `school→SchoolMask` are semantic mappings, not renames.
- **`skill_line_ability` and `faction_template` are DBC-only on TBC** — the Spell Creator trainer flow and `ZoneSafetyMap` reroute through DBC reads (the server-DBC patcher already exists; but see §1.4 — the DBC *layouts* are 2.4.3 now).
- **Spawn model:** single `id` + `creature_spawn_entry`/`spawn_group*`; `spawnMask`/`spawndist`; no `spawn_flags`/`id2..id5`. `creature_movement` is per-guid `Id, Point, PositionX…` — no `path_id`.
- **Landmine cleared:** TBC `item_instance` has **explicit columns** (`itemEntry`, `enchantments`, `charges`, `durability`…) — no serialized data blob. The lootifier reroll pass is a plain `UPDATE`.
- **TBC additions are opportunities:** `item_template` grows sockets/gems (`socketColor_1-3`, `GemProperties`, `RandomSuffix`, `TotemCategory`), `npc_vendor` gains `ExtendedCost`, `quest_template` gains `RewHonorableKills`/`RewMaxRepValue*`/`CharTitleId`, `characters` gains arena/honor-points columns (the vanilla honor-rank columns are gone — Players page honor display is per-profile), `guild` gains bank columns, `creature_template` gains `HeroicEntry`/`Expansion`.
- **Accounts:** both cores SRP `v`/`s`; TBC keeps `gmlevel` **on `account`** (no `account_access`), no `online` column (use `active_realm_id`). `realmlist` has `realmbuilds` like vmangos (minus `flag`/`gamebuild_min/max`/local-address columns).
- **`tbclogs` is 3 tables** (`logs_anticheat`, `logs_spamdetect`, `logs_db_version`) — Server Logs pages stay a vmangos capability.

### 1.3 Upstream/on-box facts

- **RA console — live and empirically verified on 3446 (2026-08-18):** banner `Welcome to the Continued Massive Network Game Object Server.\r\n` → `Username: ` → `Password: ` → **`+Logged in.\r\nmangos>`** — the same success token vmangos's RaService matches, so only the prompt-driven handshake differs. `Ra.Restricted=1` does *not* block gmlevel 3 (`ADMIN`/`admin` works — password was re-set to the documented value via the screen console, which is itself a proven RA-less fallback: `screen -S cmangos-mangosd -X stuff $'…\r'`). Commands work with and without the leading dot. **`.server info` labels differ** (T5): line 1 `CMaNGOS/0.18 (<date> - <git hash>)`, no `Core revision:` prefix; `Online players: N (max: M)` (vmangos: `Players online:`); `Server uptime: …`; plus a `Using World DB: TBC-DB 1.11.0 …` content-version line worth surfacing on the dashboard. Conf keys: `Ra.MinLevel`/`Ra.Secure`/`Ra.Restricted` vs vmangos `Ra.MinAccountLevel`. SOAP (`urn:MaNGOS`, 7878) remains a fallback transport.
- **Map formats:** mangos-tbc `.map` version magic is **`s1.4`** (vmangos/classic: `z1.4`) — same `MAPS` magic, same lineage; the header/V9/V8 layout is expected to match but **must be golden-file verified** in Phase 0 (parse one live TBC tile, cross-check interpolated Z against in-game `.gps`). vmaps same family; **mmaps v8 vs vmangos v6** — regenerated per core (already done: 72 map headers on the box).
- **GM commands:** `.goname`/`.appear` and `.namego`/`.summon` are aliases on cmangos; `.account create/set gmlevel/set password`, `.ban/.unban`, `.mute`, `.send mail/items/money (+mass)`, `.gobject add/move/turn/delete/near`, `.npc add/delete/move` (35 subs), `.tele name`, `.server info/shutdown/restart`, `.saveall`, `.reload` (91 subcommands) all present. Authoritative per-core source: the world-DB `command` table.
- **Conf:** same `"host;port;user;pass;db"` value format; key names differ (`WorldDatabaseInfo` vs `WorldDatabase.Info`); live TBC conf = 360 keys.
- **Process model:** `systemctl` via sudo — **sudoers currently covers only vmangos units** (`mangosd`/`realmd`); Phase 0 adds the `cmangos-*` units. mangosd-under-screen is invisible to the app's `/proc` scan (it matches `comm == mangosd`), but stop/start semantics go through the unit names.

### 1.4 The client delta — 2.4.3 is the real new scope

The v1 plan (classic target) inherited the 1.12.1 client wholesale. TBC does not. Everything below is 1.12-anchored today and needs a **client profile** (build 8606) for the TBC side:

| Pipeline | 1.12 anchor today | TBC impact |
|---|---|---|
| `DbcService` | Spell.dbc **173 fields / 692 B** hardcoded; CharSections/HelmetGeoset layouts "verified against 5875" | 2.4.3 DBC layouts differ (Spell.dbc alone is the 186-col shape seen in `spell_template`); needs per-build field maps — the same data-driven layout tables the live DB now hands us for free |
| Managed MPQ reader/writer | **MPQ v1 only** ("WoW 1.12 only uses v1") | 2.4.3 archives may use the extended v2 header — **verify against the extracted client on the box first**; if v2, the managed layer grows a v2 read path (write path for patch MPQs can likely stay v1 — TBC clients accept v1 patches) |
| M2 particle patching (Spell Creator visuals, SpellDNA) | vanilla M2 (v256-257) offsets | TBC M2 is v260-263 — emitter/texture block offsets shift; needs a TBC M2 map + golden-file forensics (the `fireball_forensic.py` method, rerun on 2.4.3) |
| Icons / 3D models / minimaps served from client MPQs | 1.12 client at `/home/wowvmangos/wowclient` | TBC client already extracted at `/home/wowvmangos/cmangos/World of Warcraft 2.4.3/` — per-profile ClientDataPath + the MPQ/BLP/M2 questions above |
| World Editor (ADT/WMO/MCVT, patch-Z) | 1.12 ADT v18 | TBC ADT is structurally close (still v18) but must be golden-verified before enabling |
| Client patch conventions | patch-3/4/Z.MPQ, WDB clear, spell id < 65535 | Same mechanisms exist on 2.4.3; verify the patch-letter loading order and the SpellVisual ID ceiling empirically |

**Consequence:** the TBC profile launches with client-asset features **capability-flagged off** (icon/model serving can be pointed at the 1.12 client as a cosmetic stopgap for browse pages, since icons are largely shared). They come back in Phase 4b as the client profile lands. DB-side content editing (items, spells, loot, quests, spawns) works without any of this.

---

## 2. Architecture: the `IEmulatorProfile` seam

One interface, injected everywhere the inventory found coupling. **Claude writes this frame by hand (T0); the local model never touches it.**

```csharp
public interface IEmulatorProfile
{
    string Id { get; }                     // "vmangos" | "cmangos-tbc"
    EmulatorCapabilities Caps { get; }     // feature flags, see §2.3
    DatabaseMap Databases { get; }         // role -> conn string + physical name; QualifiedName(role, table)
    WorldSchemaDialect Schema { get; }     // LatestPatchPredicate(), SpellKey(), column-map lookups
    ClientProfile Client { get; }          // build, DBC layout maps, MPQ/M2 capabilities, client data path
    ConsoleDialect Console { get; }        // handshake steps, prompt tokens, framing, timeouts
    CommandGrammar Commands { get; }       // command templates + reverse-command table + categorizer
    ProcessModel Processes { get; }        // systemd units, /proc keywords, start order, systemctl template
    EmulatorPaths Paths { get; }           // bin/etc/data/dbc/maps/vmaps/mmaps/src/sql/log dir + conf paths
    ServerConfigModel Config { get; }      // conf file set, key-metadata json, DB-info key names, reload cmd
    BackupModel Backup { get; }            // db roles per group, artifact naming
    LogFileMap LogFiles { get; }
    DiagnosticsModel Diagnostics { get; }  // probe list + per-emulator fix texts
}
```

Selection: `"Emulator": "cmangos-tbc"` in `server-config.json`; profile-keyed sections (`"Emulators": { "vmangos": {...}, "cmangos-tbc": {...} }`) with the legacy `"Vmangos"` section read as the vmangos profile.

**Decisions — now partly facts from the install:**

1. **Code adapter, not compat views** (unchanged rationale: write paths, real schema in the explorer, native-dialect future).
2. **One app instance per emulator:** vmangos on :5000, TBC on :5001 (`mangossuperui-cmangos` unit). Singletons make multi-profile-per-process a later refactor.
3. **Admin DB per profile:** `tbc_admin` beside `vmangos_admin` (matches the `tbc*` naming; `og_*` baselines, audit log, lootifier registries are per-world).
4. ~~Ports/DB names/units~~ — **installed facts:** `tbc*` DBs, 3725/8086, `cmangos-realmd`/`cmangos-mangosd`, shared `mangos` MySQL user, RA staged on 3446.
5. **`.map`/vmap parsers stay shared**, gaining a per-profile version-magic (`z1.4` vs `s1.4`) after the Phase-0 golden-file check; MoveMapGen invocation (name + `MoveMapGen.sh` orchestration) moves into the profile.

### 2.3 Capability flags on stock mangos-tbc

**Off:** bot fleet/bridge/chat/rotations (SuperUiBots C++ absent), Quest & Crafting Lootifier award hooks, `.bot addai`, `.npc reloadspawn`, MSUI_DualSpec, RTS world mode, Server Logs pages, wareffort/antispam/pbcast console verbs, **and initially all client-asset pipelines** (§1.4): Spell Creator visual/patch stages, Retexture Engine, World Editor commit, 3D model/minimap serving (or 1.12-client cosmetic stopgap).
**On:** dashboard/diagnose, console, players/accounts/realm, config editor, audit + activity, backup/restore + non-RTS worlds, DB explorer (after whitelist regen), items/spells/gobjects/quests browse+edit, loot tuner, instance loot, baselines, **ARPG Lootifier** (pure table writes — now with TBC socket/suffix columns available), profession tuning (DB side), world map (after map golden-check), wiki, source map, downloads, Placer addon (stock `.gobject`/`.gps`).

---

## 3. The Rosetta pack (deterministic fact base)

Same doctrine as SourceMapper/wiki: *the model never produces the map, it only consumes proven facts.* Status after the live pass:

| Script | Output | Status |
|---|---|---|
| `schema_dump` | `data/live_schemas.json` — all 9 live DBs from the box's information_schema | **done (live, 2026-08-18)** |
| `schema_diff` / `tbc_diff.py` | `data/port_relevant_diff_tbc.json` — the worklist | **done (live vs live)** |
| `column_map.yaml` generator | normalized-match + curated exceptions + missing-with-strategy, per app-touched table | next (mechanical from the diff) |
| `sql_surface.py` | every SQL literal with file:line, tables+columns, op type (regenerates the agent-2 inventory; feeds verifier V2) | Phase 0 |
| `conf_diff.py` | vmangos↔tbc conf key map + `config-metadata.cmangos-tbc.json` skeleton (360 keys) | Phase 0 |
| `command_diff.py` | command matrix from both world DBs' `command` tables + `.help` crawl over RA | Phase 0 (needs RA up) |
| `coupling_lint.py` | grep-class invariants (verifier V3) | Phase 0 |
| `probe_endpoints.py` | HTTP smoke suite, dual-profile golden compare | Phase 0-1 |
| `dbc_layout_probe.py` | per-build DBC field maps for the client profile (2.4.3 vs 1.12 record sizes/offsets, MPQ header version scan of the extracted TBC client) | Phase 0 stretch / 4b gate |

Plus the two grounding files injected into every model prompt: `PORT_CONVENTIONS.md` (living lessons, Claude-curated) and `superui-capabilities.json`.

---

## 4. Phases

| Phase | Scope | Exit criteria (scripted) |
|---|---|---|
| **0. Foundation** (human + Claude) | ~~sudoers for `cmangos-*` units, RA on 3446~~ **done + handshake verified 2026-08-18.** Remaining: `tbc_admin` provisioned; map golden-file check (`s1.4` tile parse vs in-game `.gps`); MPQ-version scan of the 2.4.3 client; `dotnet-sdk-8.0` on box; column_map.yaml + remaining Rosetta scripts; `IEmulatorProfile` frame + vmangos profile extraction (T0); harness skeleton end-to-end. Watch item: mangosd hit a DynamicObject shutdown assert on the SIGINT restart (server recovered; recurs → investigate before trusting restart flows) | app boots with `Emulator=vmangos` byte-identical; RA round-trip on both cores ✓ (TBC side); worklist + column map exist |
| **1. Mechanical bulk** (qwen) | T1 factory roles (~330), T2 cross-DB literals (~25), T3 patch/build predicate seam (~145), T7 paths/process/config plumbing | build green; coupling linter zero; vmangos probe suite green |
| **2. Read paths on TBC** (qwen) | T4a per-query dialect for browse endpoints; `discover_relationships.py` run against `tbc*` + explorer whitelist regeneration | probe suite green against live TBC; vmangos regression green |
| **3. Writes + ops** (qwen + Claude review tier) | T4b writes (items/spells editors vs 186-col `spell_template`, loot tuner, baselines, ARPG lootifier incl. TBC `itemEntry` reroll, NpcDev SQL), T5 RA dialect + command grammar + console catalog, T6 conf metadata (360 keys), backup groups (`tbc*` + `tbc_admin`) | write-tests on a scratch TBC world; `.reload` round-trips; backup/restore cycle passes |
| **4a. Content pipelines, DB side** (qwen) | Spell Creator SQL stage against Spell.dbc-layout `spell_template`; trainer registration via `npc_trainer` (+`ReqAbility`) with `SkillLineAbility.dbc` server patch; profession tuning reagent logic on the TBC layout | create + train a custom spell on TBC (server side); recipes tune |
| **4b. Client profile** (Claude + qwen with golden-file verifiers) | 2.4.3 DBC layout maps, MPQ v2 read path (if the scan says so), TBC M2 offsets, ADT verification, per-profile client data path; re-enable Spell Creator visuals / Retexture / World Editor / 3D serving for TBC | custom spell with visuals + retextured item + placed WMO visible in the 2.4.3 client |
| **5. Core capabilities** (separate project) | Bridge/AiBotAI, loot hooks, `.spec`, RTS on mangos-tbc — after a SourceMapper AST run over the TBC core; bot strategy decision (rebase on active `cmangos/playerbots` vs raw port) | out of scope for the first multi-day run |

---

## 5. The Qwen3.8 harness

### 5.1 Model + topology

- **Model:** `qwen3.8` on Ollama = **Qwen3.8-27B** (Aug 2026; 256K native context; `reasoning_effort` low/medium/xhigh; Apache 2.0). Default tag `27b` q4_K_M ≈ 18 GB; benchmark **`27b-mtp-q4_K_M`** (multi-token prediction) for the bulk phases. Successor of the qwen 3.6-27B that generated the wiki, same hardware class.
- **Topology:** harness on the server box (`/home/wowvmangos/msui-port/` + repo clone — SSH key access exists); Ollama on the homeai GPU box (`192.168.0.201:11434`) or on-box. `dotnet-sdk-8.0` on the box is a Phase-0 verify.
- **Sampling for code transforms:** `temperature 0.2, top_p 0.9, presence/frequency 0, fixed seed, reasoning_effort=low` (T1-T3/T7), `medium` (T4), `xhigh` on flagged retries only. Card chat defaults (temp 0.7/presence 1.5) apply only to T6 prose — presence penalty actively harms exact-code reproduction.
- **Output contract:** aider-style SEARCH/REPLACE blocks, exact-match apply, one task per call, `<done>`/`<blocked reason="">` terminator. No unified diffs.

### 5.2 The loop

```
tasks.sqlite (id, class, unit, files, status, attempts, verifier_log, audit_state)
   ▲ generated from the Rosetta pack + sql_surface index
   │
driver.py ──► context_packer: task spec + exact file slice ± callee signatures
   │            + column_map.yaml slice + PORT_CONVENTIONS.md + few-shot exemplars  (≤ 24K tokens)
   ├──► ollama /api/chat ──► SEARCH/REPLACE blocks
   ├──► apply (exact match; reject on drift)
   └──► verifier ladder:
        V0 patch applies cleanly
        V1 dotnet build (incremental)
        V2 SQL identifier validation: every table.column in changed SQL exists in the
           TARGET profile's live schema dump (live_schemas.json)  ← kills hallucinated columns
        V3 coupling_lint: no literal db names / `mangos>` / patch predicates /
           absolute paths outside profile classes; no edits outside task.files
        V4 worldstate-clinical-check (fast, in-process unit gate)
        V5 batched dual-stack runtime smoke: probe_endpoints.py against BOTH stacks
           (TBC progress + vmangos regression)
   green → commit on port/cmangos branch → next task
   red   → retry with verifier transcript (max 3, escalating reasoning_effort)
   still red → audit_state = needs_claude, park, continue
```

Pause conditions: `needs_claude > 25`, unsampled-green > 100, V5 vmangos regression, dirty tree. Throughput: ~600-800 tasks for Phases 1-3 at 1-3 min/task → **2-4 days continuous**.

### 5.3 Claude auditor protocol

- **Claude-authored, never qwen:** T0 frame, RaService dialect refactor, dynamic-SQL whitelists, `StateCaptureService` reverse-command correctness, all missing-with-strategy schema arbitration, MPQ v2/M2 format work in 4b (qwen assists with layout-table generation under golden-file verifiers).
- **Audit queue:** parked tasks land as bundles (spec + diff + verifier transcript); verdicts approve / fix / reject+lesson; every lesson appends to `PORT_CONVENTIONS.md`. Same doctrine as the wiki generator and VoiceLibraryBuilder: failures get closed in the harness (or its standing rules), not in one-off prompt tweaks.
- **Drift sampling:** 10% of green tasks re-reviewed daily — verifiers prove identifier truth, not semantics.
- **Meta-rule:** first-pass rate < ~60% on a task class means the template/context pack is wrong; fix the harness, don't grind the model.

### 5.4 Git policy (needs your sign-off)

Harness commits per green task on `port/cmangos` in the box's clone; you pull/review/merge; nothing is pushed by the harness. (Claude itself never commits, per standing preference — this is the harness's own checkpointing machinery.)

---

## 6. Task classes (the seed catalog)

| Class | What | Count est. | Executor | Hard gate |
|---|---|---|---|---|
| T0 | Profile frame, vmangos profile extraction, config plumbing, `WorldEditorController.cs:6128` hardcoded conn fix, GM-level accessor unification | ~15 units | **Claude** | clinical-check + vmangos probe suite |
| T1 | ConnectionFactory call-site migration to role API | ~330 sites / ~80 tasks | qwen, effort=low | V1+V3 |
| T2 | Cross-DB literals → `QualifiedName()` | ~25 | qwen | V1+V2+V3 |
| T3 | patch/build predicates → `Schema.LatestPatchPredicate()` / `SpellKey()`; PK-shape call sites | ~145 / ~40 tasks | qwen | V1+V2 + Claude sample |
| T4a | Read-query dialect per table cluster (column map + per-emulator SQL for spawns, bans, honor columns, quests) | ~150 clusters | qwen, effort=medium | V2 live + V5 |
| T4b | Write-path dialect (editors vs `spell_template` 186-col, loot tuner, baselines, lootifier reroll, NpcDev SQL) | ~100 clusters | qwen + Claude review | V5 write-tests on scratch world |
| T5 | Console dialect variants, command grammar tables, `commands.cmangos-tbc.json`, ParseServerInfo | ~30 | qwen (tables) + Claude (RaService) | live RA round-trip both cores |
| T6 | `config-metadata.cmangos-tbc.json` — 360 keys, mined key set + model prose + existence verifier | ~35 tasks | qwen, card sampling | conf_diff validator |
| T7 | Paths/process/log/backup/diagnose profile values + fix texts | ~60 | qwen, effort=low | V1+V3 |
| T8 | Capability gating (nav, endpoints per §2.3) | ~40 | qwen | V5 nav probe |
| T9 | Client-profile layout tables (2.4.3 DBC field maps, M2 offset maps) generated under golden-file verifiers | Phase 4b | qwen + Claude | golden-file parity |

Pre-flagged Claude-tier landmines: `spell_template` engine/transactionality on TBC (vmangos's is MyISAM); `ALTER TABLE … ADD COLUMN IF NOT EXISTS` is MariaDB-only (`WorldEditorController.cs:1196`); `NpcDevApplyService.cs:193` unwhitelisted table-name interpolation (fix during port); explorer whitelist is generated — rerun `discover_relationships.py` against `tbc*` before Phase 2; `ServerLogsController` table list already drifts from live vmangos logs (pre-existing); the 2.4.3 MPQ header version is unverified (Phase 0 scan decides 4b's MPQ scope).

---

## 7. Open decisions

| # | Question | Default if you say nothing |
|---|---|---|
| 1 | Harness on the server box vs Windows | server box |
| 2 | Ollama host + tag | homeai .201; benchmark `27b-mtp-q4_K_M` day one |
| 3 | Harness commits on `port/cmangos` (box clone, never pushed) | yes — needs sign-off |
| 4 | Admin DB name for the TBC profile | `tbc_admin` |
| 5 | Second app instance on :5001 for TBC | yes |
| 6 | Client-asset stopgap: serve icons/models from the 1.12 client for TBC browse pages until 4b | yes (cosmetic only) |
| 7 | A future mangos-classic profile (the original target) | revisit after Phase 3 — machinery reuses ~95% |
| 8 | Phase-5 bot strategy (rebase on `cmangos/playerbots` vs raw port) | decide after Phase 3; pre-work = SourceMapper over mangos-tbc |

---

## 8. Research artifacts

The supporting material lives in owner-local `tools/port/`. It is deliberately ignored and
must not be published; this section records its inventory without creating public links:

- `data/live_schemas.json` — **all 9 live DBs** (box information_schema, 2026-08-18)
- `data/port_relevant_diff_tbc.json` — **the worklist** (live vmangos vs live mangos-tbc, app-touched tables)
- `data/schema_diff.json`, `data/port_relevant_diff.json`, `data/cmangos_schema.json` — the earlier mangos-classic analysis (kept for the future classic profile)
- `research/agent1_coupling_inventory.md` — every vmangos coupling with file:line + the 10 adapter seams
- `research/agent2_sql_surface.md` — 965-statement SQL surface, dynamic-SQL sites, semantics
- `research/agent3_core_client_map.md` — bridge vocabulary, custom core hooks, client pipeline, reusable harness tooling

Box access for tooling: SSH as `wowvmangos@192.168.0.2` (key `id_ed25519_msui_vmangos_travel_20260731`); MariaDB is localhost-bound — all DB work runs on-box.
