# SuperUI / VMaNGOS 5k–10k bot scaling plan

## Target and constraint

Treat these as two different targets:

1. **5k–10k population**: registered bots that may be hot, warm, cold, or offline.
2. **5k–10k fully embodied bots**: every bot is an online `Player`, participating in
   visibility, packets, movement, combat, and persistence at full cadence.

The first target is realistic through activity tiers. The second is a much harder
world-server architecture project and may ultimately require multiple worldserver
instances. Moving to a newer C++ standard is useful for implementation quality,
but does not make the per-map player update path parallel by itself.

## Baseline observed on 2026-08-29

- Host: i9-13900K, 32 logical CPUs, 31 GiB RAM.
- Fleet: 342 connected bots; 201 on map 0 and 141 on map 1.
- `mangosd`: about 2.3 GiB RSS, 56 threads, no observed >100 ms slow-update
  messages during the inspection window.
- SuperUI: 9.8–12.5 GiB RSS/HWM, about 98% private anonymous memory.
- SuperUI bridge: 342 bot sockets and 735 open file descriptors.
- SuperUI has a 524,288 file-descriptor limit, but `mangosd` has a **1,024 soft
  limit**. The core limit must be raised by Nico before approaching 1,000 bridge
  sockets.
- The C++ core already runs the two continent maps on separate map update
  threads. A single map still updates its players serially.
- A bot sends STATE every five seconds. At 10k bots, the old path meant roughly
  2,000 fleet-wide SignalR sends per second.

## Phase 1 — bounded observability and UI backpressure

This repository change implements both items:

1. Sample process, GC, Linux anonymous memory, allocation rate, thread-pool,
   file-descriptor, bridge, and UI-batch counters every 30 seconds. Retain 240
   samples (two hours) and expose them read-only at `/Bots/ScaleRuntime`.
2. Coalesce browser state by bot GUID and publish `BotStateBatch` every 200 ms in
   fair FIFO chunks of at most 1,024. Both values are bounded configuration
   settings. Never queue a mutable `BotState`, and omit the planner's quest-log
   blob from recurring browser updates and initial browser rosters.

The optional configuration keys are `BotBridge:StateBatchPublishIntervalMs`
(50–2,000 ms), `BotBridge:StateBatchMaxSize` (64–4,096), and
`BotBridge:UiPublishTimeoutMs` (100–10,000 ms; default 2,000). Connect/disconnect
are state projections in the same queue, and mass deletion uses one fleet
tombstone, so a browser outage cannot hold bot TCP readers or a database
connection behind thousands of lifecycle sends.

The patch does not force GC, take a dump, change bot decisions, deploy files, or
control a live process.

The server-to-browser state contract changes to `BotStateBatch`. During Nico's
owner-operated rollout, reload every open Bot Monitor tab after the new server
build is running so the versioned `bots.js` registers the batch handler. The new
UI still accepts the old single-state events if the server is rolled back.

### Memory decision after the first owner-operated rollout

Use at least 30 minutes of telemetry at the current fleet size:

- `managedAllocatedBytesEstimate` trending near anonymous RSS makes managed
  allocation the main suspect, but it can include garbage not yet collected.
- `gcCommittedBytes` much larger than `managedAllocatedBytesEstimate`, with high
  `gcFragmentedBytes`, points to allocation churn/fragmentation and retained GC
  segments.
- A persistent anonymous-RSS trend not explained by the GC measures points toward
  native allocations or another non-GC owner. Do not subtract these as exact
  values: anonymous RSS is resident memory while GC committed memory need not be.

Only after that split should we take a controlled heap dump or change logging.
Current logging and Shadow circuit tracing are high-rate allocation amplifiers,
but their retained rings are bounded and do not explain 11 GiB by themselves.

## Phase 2 — bridge and brain scale work

Implement as separately revertible changes, in this order:

1. Replace the per-connection one-second sensory watchdog timer with one central
   sweep, preserving pre-HELLO timeout and exact-session race protection.
2. Add timings and eligible/due queues to the brain scheduler. Stop scanning and
   locking every bot at 4 Hz when most bots have a 10–30 second decision cadence.
3. Limit group coordination locks to actual group members plus previously owned
   members; do not acquire every fresh bot context for every group pass.
4. If the one-socket-per-bot bridge remains a constraint, move to a multiplexed
   core-to-SuperUI connection. This removes a linear file-descriptor and socket
   scheduler cost on both processes.

## Phase 3 — core work that determines the real ceiling

Profile before changing the C++ standard, then attack measured multipliers:

1. Add hot/warm/cold AI cadence. Combat, player-party, and nearby-player bots stay
   hot; distant idle bots sense and decide less often; cold population is
   persisted without full-rate embodiment.
2. Suppress or summarize client-only packet construction for socketless bots when
   AI does not consume the packet. Audit visibility fan-out in dense bot areas.
3. Spread population geographically. The two continent map threads give only two
   large parallel lanes; clustering thousands on one map defeats that benefit.
4. Parallelize only isolated, measured subsystems first (path queries, immutable
   sensing, batch preparation). Splitting `Player::Update` across workers requires
   an ownership/message-passing design, not merely `std::thread` or C++20.
5. If a single process cannot meet the 10k fully-online target safely, shard by
   world/map/population role across multiple worldserver instances.

## Load staircase and promotion gates

Nico controls every live rollout and load step. Do not skip a gate:

| Step | Soak | Promote only when |
| --- | ---: | --- |
| 342 baseline | 30 min | telemetry is stable and complete |
| 750 | 30 min | no growing UI backlog, no sensory-feed storm |
| 1,250 | 60 min | core FD limit is raised; world updates remain inside budget |
| 2,500 | 2 hr | RSS plateaus, swap-in/out is negligible, DB saves keep up |
| 5,000 | 4 hr | hot/warm/cold policy and brain scheduling are proven |
| 10,000 | 12 hr | no monotonic memory growth, tick overruns, or reconnect churn |

Stop and roll back a step if any of these occurs:

- repeated world updates exceed the configured 100 ms map interval;
- available RAM falls below 6 GiB or the host begins sustained swap I/O;
- SuperUI RSS grows more than 10% over the final hour without leveling off;
- pending UI states do not drain within ten seconds after a synchronized burst;
- file descriptors exceed 70% of the active soft limit;
- stale/recycled sensory sockets or reconnects rise continuously;
- character-save/database latency grows across successive save intervals.

## Rollback

The pre-change originals, SHA-256 checksums, baseline commit, and path-by-path
restore map live in `backups/scale-foundation-20260829-01/RESTORE.md`.
