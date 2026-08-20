# Database Divergence Report — SuperUI stack vs. stock VMaNGOS

**Scope:** the four core game databases only — `mangos` (world), `characters`, `realmd`, `logs`.
The application's own admin database, **`vmangos_admin`, is excluded by request** (it is documented in
[`MangosSuperUI/sql/vmangos_admin_schema.sql`](MangosSuperUI/sql/vmangos_admin_schema.sql)).

**Method / sources**
- **Live schema:** `tools/port/data/live_schemas.json` — an `information_schema` dump of all live DBs taken over SSH on **2026-08-18**.
- **Stock reference:** upstream VMaNGOS `sql/` on the `development` branch (`characters.sql`, `logon.sql`, `logs.sql`) fetched 2026-08-20.
- **Code scan:** every `CREATE TABLE` / `ALTER TABLE` in `MangosSuperUI/**.cs`, with each statement's target DB traced through `Models/ConnectionFactory.cs` (`Mangos()`→world, `Characters()`, `Realmd()`, `Logs()`, `Admin()`→`vmangos_admin`).

> ⚠️ **Two caveats before the tables.**
> 1. The stock reference is VMaNGOS **development-branch** SQL. Your SuperUI-Core is a *fork* of some earlier VMaNGOS commit, so a few tables flagged "version drift" below are almost certainly just *your fork's base snapshot differing from today's upstream* — not something the SuperUI stack added. Those are marked ⬜ and are not actionable.
> 2. The live dump is from **2026-08-18**. Tables a feature creates only on first use won't appear unless that feature had been exercised on that box. Where the code creates a core-DB table that isn't in the dump, it's noted.

### Legend

| Mark | Origin | Actionable for you? |
|------|--------|---------------------|
| 🟩 | **MangosSuperUI web app** creates it | Yes — this repo owns it |
| 🟦 | **SuperUI-Core (C++) contract** — app writes it, core reads it (or vice-versa) | Yes — ship the schema |
| 🟨 | **Manual / experimental** — hand-made, not produced by app or core | Housekeeping (drop when done) |
| ⬜ | **Version drift** — fork base ≠ upstream dev; not a SuperUI feature | No |

---

## Headline

Against stock VMaNGOS, the **SuperUI stack adds surprisingly little to the core DBs.** Almost everything custom lives in `vmangos_admin` (excluded). Within the four core databases, only these are genuinely attributable to the SuperUI stack:

- **`characters.playerbot`** — the one and only **modified stock table** (+8 columns). This is the boot-blocker from your install notes.
- **`mangos.custom_spell_meta`** — Spell Creator sidecar (written via a live connection).
- **`characters.superui_*` (×9)** and **`mangos.superui_rts_spell_original[_state]` (×2)** — RTS World Editor, injected into the core DBs *via restored dump artifacts*, not a live connection.

Everything else that diverges is manual experiments or fork-base version drift — **not** the web app or a SuperUI feature.

---

## `mangos` (world) — 174 live tables vs ~171 stock

| Table | Mark | Origin & notes |
|-------|------|----------------|
| `custom_spell_meta` | 🟩 | **Spell Creator.** Created lazily on first use via `_db.Mangos()` (`Services/SpellServices/SpellConfigService.cs:44-59`). Columns: `entry` (PK), `source_entry`, `spell_name`, `name_subtext`, `description`, `tooltip`, `color_preset`, `phase_params`, `icon_source`, `icon_path`, `created_at`, `updated_at`. Present in the live dump. |
| `superui_rts_spell_original` | 🟩 | **RTS World Editor.** `CREATE TABLE ... LIKE spell_template` baked into the *WorldMangos* dump artifact (`Services/RtsHeroSpellWorldStore.cs:29-30`, attached at `Services/RtsWorldCreationService.cs:55-58`). Reaches the world DB at dump-restore time. **Not in the 2026-08-18 dump** (world-side RTS restore not applied on that box). |
| `superui_rts_spell_original_state` | 🟩 | **RTS World Editor.** Same artifact postlude (`RtsHeroSpellWorldStore.cs:31-32`). Columns: `id` TINYINT UNSIGNED (PK), `captured_at` TIMESTAMP. Not in the dump. |
| `custom_texts` | 🟦 | Present in live. **The app does not create it** (no `CREATE`) — it only *populates* it (Spell Creator / Spell Completer / PatchBuilder write custom text rows). Native to the SuperUI-Core world DB. Listed here only so you know the SuperUI toolchain depends on it existing. |
| `tmp_spell` | 🟨 | Manual leftover — zero references anywhere in the app. Looks like a scratch copy from a spell import/dump session. Safe to drop. |

All other 169 world tables are stock VMaNGOS content/definition tables. **No stock world table is altered** — Item Forge and the Lootifier only `INSERT` rows into stock tables (`item_template`, `*_loot_template`, etc.); they never change their columns.

---

## `characters` — 69 live tables vs 55 stock

| Table | Mark | Origin & notes |
|-------|------|----------------|
| `playerbot` **(stock table, +8 cols)** | 🟦 | **The boot-blocker.** See dedicated section below. |
| `superui_worldstate` | 🟩 | RTS. `RtsWorldCreationService.cs:194`. Cols: `key` (PK), `value`. |
| `superui_rules_zone` | 🟩 | RTS. `:195`. Cols: `zone_id` (PK), `ore`, `skins`, `herbs`. |
| `superui_rules_hub` | 🟩 | RTS. `:196`. Cols: `hub_id` (PK), `zone_id`, `name`, `banner_go_guid`, `event_alliance`, `event_horde`, `capture_ms`, `initial_controller`. |
| `superui_rules_hero` | 🟩 | RTS. `:197`. Cols: `hero_level` (PK), `declare_cost`, `revive_fee`, `spell_id`, `scale_percent`, `damage_percent`. |
| `superui_rules_dungeon` | 🟩 | RTS. `:198`. Cols: `map_id` (PK), `final_boss_entry`, `buff_spell_id`, `loot_items`. |
| `superui_faction` | 🟩 | RTS. `:199`. Cols: `team` (PK), `honor_pool`. |
| `superui_heroes` | 🟩 | RTS. `:200`. Cols: `guid` (PK), `team`, `hero_level`, `dead`, `declared_at`. |
| `superui_zone_control` | 🟩 | RTS. `:201`. Cols: `zone_id` (PK), `controller`. |
| `superui_dungeon_control` | 🟩 | RTS. `:202`. Cols: `map_id` (PK), `controller`. |
| `money_bak_20g` | 🟨 | Manual. A "backup 20g" money table — hand-made admin experiment. Not app, not core. |
| `character_spec_test` | 🟨 | Manual. Dual-spec experiment (`_test` suffix). Not app, not core. |
| `character_spec_action_test` | 🟨 | Manual. Same. |
| `account_data` | ⬜ | Native VMaNGOS account-scoped UI/SavedVariables data. Its storage location varies by VMaNGOS version/build flag; absent from today's dev `characters.sql` but a real core feature. Version drift. |
| `character_account_data` | ⬜ | Same family — per-character account-data cache. Version drift. |

> The 9 `superui_*` tables **are** present in the 2026-08-18 dump — so the RTS world-creation ceremony *has* run against this box's `characters` DB (even though its world-DB counterparts above hadn't been restored yet).

---

## `realmd` — 15 live tables vs 11 stock

| Table | Mark | Origin & notes |
|-------|------|----------------|
| `rbac_permissions` | ⬜ | **Not created by the web app** (no references in `MangosSuperUI/**.cs`). RBAC is not a traditional VMaNGOS concept; introduced by the SuperUI-Core fork or a manual import. Origin is outside this repo. |
| `rbac_account_permissions` | ⬜ | Same. |
| `rbac_command_permissions` | ⬜ | Same. |
| `allowed_clients` | ⬜ | Same — not stock VMaNGOS, not app. Likely a fork-level client-build allowlist. |

**Zero web-app footprint in `realmd`.** RTS only issues an `UPDATE realmcharacters SET numchars=0` into the realmd dump (`RtsWorldCreationService.cs:82-86`) — no DDL, no new tables.

---

## `logs` — 11 live tables vs 15 stock (dev)

| Table | Mark | Origin & notes |
|-------|------|----------------|
| `logs_player` | ⬜ | Present in live, not in dev `logs.sql`. Fork/version. |
| `system_fingerprint_usage` | ⬜ | Present in live, not stock. Fork/version. |
| *(absent from live: `logs_behavior`, `logs_characters`, `logs_chat`, `logs_movement`, `logs_spamdetect`, `logs_warden`)* | ⬜ | These stock dev-branch log tables **don't exist** on your box → your fork predates them. Pure version drift. |

**Zero web-app footprint in `logs`.** No `CREATE`/`ALTER` in the codebase targets the logs DB.

---

## The one real stock-table modification: `characters.playerbot`

This is the single stock VMaNGOS table the SuperUI stack changes, and the cause of the mangosd boot failure on a bare SuperUI-Core install.

**Stock VMaNGOS** (`characters.sql`) and the SQL bundled with SuperUI-Core both create only:

```
char_guid, chance, comment, ai
```

**But the compiled SuperUI-Core `PlayerBotMgr` selects 12 columns at startup:**

```
SELECT char_guid, chance, ai, race, class, level, map,
       position_x, position_y, position_z, name FROM playerbot
```

The MangosSuperUI **web app** owns the migration that reconciles the two: `BotBrainService.EnsurePlayerbotColumnsAsync()` adds the missing 8 columns **eagerly at startup** via `_db.Characters()` (`Services/BotBrainService.cs:998-1020`), guarded idempotently by `information_schema` checks:

| Added column | Type |
|--------------|------|
| `name` | `VARCHAR(12)` |
| `race` | `TINYINT UNSIGNED` |
| `class` | `TINYINT UNSIGNED` |
| `level` | `TINYINT UNSIGNED` |
| `map` | `SMALLINT UNSIGNED` |
| `position_x` | `FLOAT` |
| `position_y` | `FLOAT` |
| `position_z` | `FLOAT` |

**Why a bare core fails:** on a fresh install, mangosd boots during **Part 1** — *before the web app has ever run* — so nothing has added those columns yet, and `PlayerBotMgr`'s query fails, terminating mangosd. Once the MangosSuperUI web app has started at least once, it self-heals the schema and the problem disappears. Until then, apply the manual fix (now documented in **INSTALL.md → Step 0a**).

> **Recommendation:** commit the 12-column `playerbot` DDL and the `vmangos_admin.lootifier_generated_items` DDL to the **SuperUI-Core** repository's SQL so a bare-core install boots without the web app's help. That closes the code-vs-bundled-SQL gap at its source. (`INSTALL.md → Step 0` is the interim workaround.)

---

## Attribution summary

| Database | 🟩 Web app | 🟦 Core contract | 🟨 Manual | ⬜ Version drift |
|----------|-----------|------------------|-----------|------------------|
| `mangos` | `custom_spell_meta`, `superui_rts_spell_original`, `superui_rts_spell_original_state` | `custom_texts` (populated, not created) | `tmp_spell` | — |
| `characters` | `superui_*` ×9 | `playerbot` (+8 cols) | `money_bak_20g`, `character_spec_test`, `character_spec_action_test` | `account_data`, `character_account_data` |
| `realmd` | — | — | — | `rbac_*` ×3, `allowed_clients` |
| `logs` | — | — | — | `logs_player`, `system_fingerprint_usage` (+6 stock tables absent) |

**Bottom line:** the web app's true additions to the core DBs are `custom_spell_meta` + the RTS `superui_*`/`superui_rts_*` set + the `playerbot` column migration. Everything else is either your own manual scratch tables or a difference between your fork's VMaNGOS base and today's upstream — neither of which the SuperUI stack introduced.
