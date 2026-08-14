# World State

World State manages one set of canonical VMaNGOS databases and a shelf of immutable snapshot bundles. Parked worlds are not simultaneously mounted database schemas.

## Lifecycle semantics

- **Suspend** stops `mangosd` and `realmd`, captures every managed world-state artifact, and leaves the captured world's bytes in the canonical databases. The registry records that those bytes are still materialized, so a normal same-world resume may skip importing them.
- **Force full restore** validates and imports the selected snapshot even when it is already materialized. This is the recovery/test path for proving that a parked snapshot is actually restorable.
- **Resume** validates again, stops the processes if necessary, restores every required group as one whole-world operation, restores that world's `mangosd.conf`, applies any RTS launch configuration, and then starts `realmd` and `mangosd` from the owner-confirmed UI action.
- **Fork** is an exact logical branch of an existing snapshot. It keeps the same characters, bots, progress, and configuration until the fork is run and suspended separately.
- **Create New RTS World** is different from Fork. It builds a new parked snapshot entirely from snapshot files and never mounts a database or starts a service.

There is only one physical `mangos`, `vmangos_admin`, `characters`, and `realmd` set at a time. `LiveWorldId` means a world is serving; `MaterializedWorldId` means whose bytes currently remain in those schemas. A suspended world can therefore be materialized but not live.

## Snapshot formats

Legacy v1 snapshots contain full gzip SQL dumps and one combined core tar. They can be structurally checked, but they have no historical hashes. Their root `mangosd.conf` is restored explicitly to the currently configured `Vmangos:MangosdConfPath` rather than being left beside the source tree.

V2 snapshots add SHA-256 and length metadata for required artifacts, an exact-path `core_mangosd.conf`, campaign-scoped rotation assignments, and two character-template artifacts:

- `players_characters_schema.sql.gz`: deployed character schema with no data;
- `players_characters_system.sql.gz`: migration/version rows only.

Those clean-template artifacts are why a v2 snapshot can safely seed a zero-roster campaign without parsing or editing an arbitrary full character dump.

Snapshots capture the configured source tree, SQL tree, and `mangosd.conf`; they do **not** capture the installed `mangosd` or `realmd` binaries. Those owner-deployed binaries are shared runtime capability and must already include the RTS implementation before an RTS world is resumed.

## Clean RTS campaign boundary

An RTS genesis preserves:

- `mangos` world content;
- human accounts, access, bans, and realm configuration in `realmd`;
- reusable/global `vmangos_admin` configuration and content;
- core source, SQL, server configuration, rotation profiles, and the global WoW-era name list.

It resets:

- all characters and character-owned rows, including `playerbot`;
- every `realmcharacters.numchars` count, matching the newly empty character database;
- GUID/name-bound bot personality, registry, inventory, wallet, grouping, chat, and trace state;
- per-name rotation assignments;
- RTS honor, hero, territory, and dungeon runtime state.

The new roster starts at zero. Later bot creation reuses `wwwroot/data/wow_era_5000_names.txt`; no bots are spawned during world creation.

## RTS launch configuration

The `rts-r1-v1` profile is editable each time an RTS world is resumed. It currently includes:

- `PlayerLimit`, `PlayerHardLimit`, and `LoginPerTick` in `mangosd.conf`;
- Alliance and Horde bot admission caps;
- the RTS state flush interval;
- every R1 XP and loot rate consumed by the core.

`RealmID` is not editable campaign tuning. Create inherits it from the selected snapshot's captured `mangosd.conf`; Resume verifies the same captured value again after restore and never rewrites it. Combined Alliance and Horde caps may not exceed `PlayerLimit` or the current eligible unique-name count. `PlayerLimit - combined bot caps` is the explicit session headroom left for humans and other logins; zero is valid but leaves no headroom. The default profile reserves 2,500 bot slots (1,250 per faction) inside a 2,600-session soft/hard ceiling, leaving 100 session slots. Bots and human players share the VMaNGOS session limit. The exact lowercase `characters.superui_worldstate.mode=rts` row activates the C++ RTS gate at boot; a UI flavor badge alone does not.

## Owner validation sequence

1. Deploy the updated MangosSuperUI artifact using the owner's normal procedure.
2. On the parked legacy MMO world, select **Force full restore** and review the preflight. The owner confirms Resume.
3. Verify the MMO is healthy, then the owner suspends it. That creates the first checksummed v2 clean-template snapshot.
4. Choose **New RTS World**, select that v2 source, review/edit the launch profile, and create the parked zero-roster world.
5. Resume the RTS world, verify the preflight/profile, and perform the R1 gameplay checks.

Codex may edit and build this project, but does not deploy it, invoke these lifecycle actions, mutate the live databases, or control either server process.

## Current recovery boundary

Preflight verifies archive structure and v2 hashes before destructive work, and the materialized marker is cleared before imports. Database groups are still restored sequentially with drop/create/import rather than through a cross-schema transactional staging swap. A failed restore remains recoverable from the immutable snapshot, but it must be retried before any world is treated as live.
