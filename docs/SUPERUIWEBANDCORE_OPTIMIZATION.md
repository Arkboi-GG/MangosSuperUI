# SuperUI Web and VMaNGOS Core Optimization

## Purpose

This document records the scaling investigation, repository changes, live test
results, conclusions, rollback path, and recommended work needed to move the
SuperUI/VMaNGOS bot stack from hundreds of bots toward 2,500 and eventually a
5,000–10,000 population target.

The most important conclusion so far is that the current limit is not raw CPU
capacity in the VMaNGOS world process. At 692 connected bots the host was still
96–98% idle and the bridge remained healthy. The immediate constraints are:

1. ~~SuperUI's .NET Server GC heap policy, allocation rate, and fragmentation.~~
   **Superseded — see "Session 2" below.** The memory behaviour was caused by
   CircuitTrace *shadow mode*, not by GC policy. With shadow off, the same
   692-bot fleet sits at 1.1 GiB RSS instead of ratcheting to 8.4 GiB.
2. The active VMaNGOS process's 1,024 soft file-descriptor limit — **and,
   separately, `PlayerLimit = 1000`**. These are different resources that
   bind within twelve bots of each other; both must move.
3. Linear per-bot work that will matter later: one TCP connection per bot,
   fleet-wide scheduler scans, per-connection timers, packet/visibility work,
   and serial player updates within each map.

Moving the core to a modern C++ standard is worthwhile for maintainability,
profiling, safer ownership, better concurrency primitives, and implementing the
changes below. It will not automatically make the world thread multi-core or
make `Player::Update` safe to execute concurrently.

## Ownership and operational boundary

- Nico performed the live deployment, restarts, and bot-count changes.
- Repository analysis, code preparation, tests, and live observation were done
  without changing databases or worldstate.
- Live monitoring uses read-only process, `/proc`, log, and HTTP diagnostics.
- The active monitor is `watch-superui-bot-scale-test`, scheduled every five
  minutes. Its working baseline is now 692 bots.
- The active VMaNGOS process runs from the vmangos `run/bin` directory. A second
  CMaNGOS process exists on the host and must not be confused with it: select by
  cmdline path *and* RSS, never by process name (see the Session 2 traps — the
  worker is named `mangosd-main`, and a small wrapper matches the path first).

## Target definition

Two targets must remain distinct:

1. **5,000–10,000 registered population**: bots may be hot, warm, cold, or
   offline/persisted. This is achievable with activity tiers and bounded work.
2. **5,000–10,000 fully embodied online players**: every bot participates in
   movement, visibility, packets, combat, AI, persistence, and world updates at
   normal cadence. This is a significantly harder architecture problem and may
   require multiple worldserver instances.

The practical near-term target is a stable staircase through approximately
750, 1,250, and 2,500 connected bots. The 5,000–10,000 goal should use hot,
warm, and cold activity tiers rather than assuming every bot must remain fully
hot at all times.

## Host and architecture findings

- Host CPU: Intel i9-13900K, 32 logical CPUs.
- Host memory: approximately 31 GiB.
- SuperUI: .NET 8, Server GC, running as `MangosSuperUI.dll`.
- SuperUI bot bridge: one TCP connection per bot on localhost port 3444.
- Each bot normally sends a `STATE` update every five seconds.
- The core already updates the two continent maps on separate map update
  threads. Players within one map still pass through a largely serial update
  path.
- Geographic distribution can therefore use multiple map lanes, but clustering
  thousands of bots on one map negates much of that benefit.
- SuperUI's descriptor limit is large, but the active VMaNGOS process has a
  1,024 soft descriptor limit.
- At 692 bots, VMaNGOS had 730 open descriptors. This is about 71% of the soft
  limit and already crosses the plan's 70% promotion gate. The limit must be
  raised before another bot-count increase.

## Repository changes implemented

The working tree contains a scale-foundation patch. It is not yet represented
as a clean committed change set.

### 1. Bounded runtime diagnostics

Added `RuntimeScaleDiagnosticsService` and registered it as both a singleton and
a hosted service.

The service:

- samples every 30 seconds;
- retains at most 240 samples, or two hours;
- exposes a read-only report at `/Bots/ScaleRuntime`;
- accepts a bounded `history` query parameter;
- does not force garbage collection or write a dump;
- treats diagnostic failure as non-fatal to the host.

The report includes:

- process working set, private bytes, virtual bytes, threads, and handles;
- Server/Workstation GC mode and GC latency mode;
- managed-memory estimate and total lifetime allocation;
- calculated allocation rate;
- heap size, committed bytes, fragmentation, memory load, LOH, and POH;
- Gen 0/1/2 collection counts, finalization queue, pinned objects, and GC pause
  percentage;
- thread-pool threads, pending work, and completed work;
- bridge connection count and tracked-state count;
- UI batch settings, backlog age, coalescing, publish failures, and requeues;
- Linux RSS, PSS, anonymous/private memory, process swap, and file descriptors
  from `/proc/self/smaps_rollup` and `/proc/self/fd`.

Relevant files:

- `MangosSuperUI/Services/RuntimeScaleDiagnosticsService.cs`
- `MangosSuperUI/Controllers/BotsController.Fleet.cs`
- `MangosSuperUI/Program.cs`
- `MangosSuperUI.Tests/RuntimeScaleDiagnosticsTests.cs`

### 2. Latest-wins UI state batching

The old path could issue one SignalR send for every bot `STATE`. At 10,000 bots
with a five-second state interval, that implies roughly 2,000 fleet-wide hub
sends per second before lifecycle and brain events.

The new `BotStateUpdateBuffer`:

- stores at most the newest immutable UI snapshot per bot GUID;
- coalesces repeated updates for the same bot;
- uses a fair GUID queue so busy bots cannot permanently starve others;
- drains bounded batches;
- requeues a failed batch without overwriting a newer state;
- records queued, coalesced, published, failed, and requeued counters;
- exposes the oldest pending state age for stop-gate monitoring.

`BotBridgeService` now publishes `BotStateBatch` on a periodic publisher instead
of awaiting a hub send inside every bot TCP reader. Browser slowness therefore
cannot linearly back up all bot state readers.

Defaults and bounds:

| Setting | Default | Allowed range |
| --- | ---: | ---: |
| `BotBridge:StateBatchPublishIntervalMs` | 200 ms | 50–2,000 ms |
| `BotBridge:StateBatchMaxSize` | 1,024 | 64–4,096 |
| `BotBridge:UiPublishTimeoutMs` | 2,000 ms | 100–10,000 ms |

Relevant files:

- `MangosSuperUI/Services/BotStateUpdateBuffer.cs`
- `MangosSuperUI/Services/BotBridgeService.cs`
- `MangosSuperUI/Hubs/BotBridgeHub.cs`
- `MangosSuperUI/wwwroot/js/bots.js`

### 3. Smaller detached browser payloads

`BotState.CreateUiSnapshot()` creates a detached projection and removes the
planner-only quest-log wire blob from recurring browser messages and initial
rosters. The browser already obtains quest detail through its dedicated path,
so repeatedly serializing this field was unnecessary allocation and bandwidth.

Mutable live `BotState` instances are not queued directly. This prevents
concurrent mutation from producing internally inconsistent browser payloads.

### 4. Ordered lifecycle and deletion handling

Connect and disconnect transitions now pass through the same latest-wins state
queue. Initial `AllBots` rosters, state batches, and permanent deletion
tombstones share a publication barrier so an old delayed snapshot cannot
resurrect a deleted bot in the browser.

The deletion path now:

- unwinds the database connection before browser notifications;
- evicts only rows that were actually deleted if a fleet delete partially
  fails;
- removes queued state for deleted GUIDs;
- sends one `BotRemoved` event for one bot or one `BotsRemoved` event for a
  fleet, rather than thousands of serialized lifecycle sends.

The browser now:

- handles `BotStateBatch`;
- retains compatibility with legacy `BotConnected`, `BotDisconnected`, and
  `BotStateUpdate` events for server rollback;
- removes all state, brain, inventory, loadout, spellbook, decision, and DOM
  caches when a bot is permanently deleted;
- blocks late AJAX callbacks from recreating deleted UI state.

After deploying this server/UI pair, every open Bot Monitor tab should be
reloaded so the new JavaScript registers the batch handler.

### 5. Tests and verification

Tests cover buffer bounds, newest-wins behavior, ordering/races, requeue
behavior, timeouts/failures, deletion barriers, UI snapshot behavior, and Linux
memory parsing.

Local verification on 2026-08-29:

```text
dotnet test MangosSuperUI.Tests/MangosSuperUI.Tests.csproj --no-restore
Passed: 212
Failed: 0
Skipped: 0
```

Nico subsequently redeployed and restarted SuperUI and VMaNGOS. The live
`ScaleRuntime` endpoint and `BotStateBatch` counters confirmed that the new code
was active.

## Backup and rollback status

A reversible backup exists at:

- `backups/scale-foundation-20260829-01/RESTORE.md`
- `backups/scale-foundation-20260829-01/original/`
- `backups/scale-foundation-20260829-01/patched/`

The baseline commit recorded by the backup is:

```text
94bdabbc25a608592253acf97b2be8f2428e0d11
```

The restore document maps every original file to its repository destination and
records SHA-256 checksums. The `patched` directory is a reconstruction snapshot
of the tested experiment. No database, worldstate, live service, or server
binary is included in this rollback bundle.

## Live test timeline

### Initial 342-bot baseline

The first post-redeploy baseline was approximately:

- 342 connected and tracked bots;
- SuperUI RSS about 6.4 GiB;
- GC committed about 6.2 GiB;
- managed estimate oscillating as high as about 5.2 GiB;
- allocation rate about 100–109 MiB/s after reconnect;
- zero durable pending UI states;
- zero batch publish failures and requeues;
- active VMaNGOS RSS approximately 1.7–1.9 GiB;
- no repeated world updates over 100 ms;
- no reconnect storm, OOM, or BotStateBatch failure.

The host had substantial swap already occupied by older pages, but repeated
`vmstat` samples showed no sustained swap-in or swap-out activity. Neither the
SuperUI process nor active VMaNGOS process reported process swap in the later
checks. Available memory, rather than historical swap occupancy alone, is the
useful safety signal.

### Long 342-bot GC cycle

The 342-bot run exposed a very large Server GC sawtooth:

| Approx. UTC | Bots | SuperUI RSS | GC committed | Fragmented | Allocation | Available RAM | Interpretation |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 20:50 | 342 | 6.46 GiB | 6.22 GiB | 0.44 GiB | 106 MiB/s | 15.6 GiB | Initial monitored point |
| 21:06 | 342 | 4.11 GiB | 3.85 GiB | 0.22 GiB | 100 MiB/s | 17.8 GiB | GC contraction |
| 21:18 | 342 | 6.77 GiB | 6.50 GiB | 0.71 GiB | 85 MiB/s | 15.1 GiB | Monotonic regrowth |
| 21:24 | 342 | 8.99 GiB | 8.72 GiB | 0.73 GiB | 82 MiB/s | 12.9 GiB | Growth accelerated |
| 21:30 | 342 | 9.07 GiB | 8.79 GiB | 0.18 GiB | 78 MiB/s | 12.7 GiB | Contracted after ~9.74 GiB peak |
| 21:48 | 342 | 9.26 GiB | 8.98 GiB | 0.81 GiB | 79 MiB/s | 12.6 GiB | Managed estimate reached ~6.53 GiB |
| 22:00 | 342 | 9.29 GiB | 8.84 GiB | 1.53 GiB | 78 MiB/s | 12.1 GiB | High fragmentation |
| 22:17 | 342 | 10.15 GiB | 9.85 GiB | — | ~74 MiB/s | — | Immediate pre-ramp sample |

RSS repeatedly tracked GC committed memory while bridge queues, VMaNGOS, and
system CPU remained healthy. GC sometimes returned hundreds of MiB, proving the
growth was not strictly monotonic forever, but the retained/committed plateau
kept ratcheting upward during the observation window. This is stronger evidence
for Server GC heap sizing/fragmentation and sustained allocation churn than for
a VMaNGOS world-thread limit.

### Ramp from 342 to 692 bots

Nico added 350 bots at approximately 22:17 UTC. All connected successfully, and
the endpoint reported 692 bridge connections and 692 tracked states.

At 22:20:44 UTC:

| Metric | 692-bot observation |
| --- | ---: |
| SuperUI RSS | 10.98 GiB |
| SuperUI private bytes | 11.46 GiB |
| GC committed | 10.64 GiB |
| GC heap after last collection | 4.65 GiB |
| GC fragmented | 3.43 GiB |
| Managed-memory estimate | 4.08 GiB |
| Allocation rate | 123.9 MiB/s |
| GC pause percentage | 0.14% |
| SuperUI threads / descriptors | 90 / 1,038 |
| Pending UI states / oldest age | 0 / none |
| Publish failures / requeues | 0 / 0 |
| Active VMaNGOS RSS | 2.19 GiB |
| Active VMaNGOS threads / descriptors | 56 / 730 |
| Available host RAM | 10.56 GiB |
| Sustained swap I/O | None observed |
| Host CPU idle | 96–98% |

The only transient UI queue in the earlier run was 134 states approximately
0.04 seconds old; it drained by the next sample. This is healthy behavior and
well inside the ten-second gate.

No recent log evidence showed:

- repeated world-update overruns above 100 ms;
- stale/recycled socket growth;
- reconnect storms;
- OOMs;
- `BotStateBatch` publish failures;
- increasing requeues.

Normal gameplay logs did show movement/path refusal, death/recovery, and quest
fallback activity. Those are bot-behavior issues, not present evidence of a
scale transport failure.

## What the measurements mean

### The UI batching change worked

The 350-bot synchronized increase did not create a lasting UI backlog. Zero
publish failures and zero requeues at 692 bots indicate that the latest-wins
buffer removed SignalR fan-out from the critical TCP ingestion path as intended.

This does not make browser rendering free, but it changes backpressure from
"one queued message per heartbeat" to "at most one current state per bot."

### The C++ core is not yet CPU-bound

At 692 bots:

- VMaNGOS remained near 2.19 GiB RSS;
- its thread count remained 56;
- the host remained 96–98% idle;
- there were no repeated slow world updates.

This indicates ample CPU headroom at the current population. It does not prove
that a single map can scale to 5,000 fully active players; it only shows that the
present test has not reached that wall.

### The VMaNGOS descriptor limit is an immediate hard ceiling

The process had 730 descriptors against a 1,024 soft limit. The increase from
342 to 692 bots added approximately one descriptor per bot, as expected from the
current bridge design. Without raising the limit, failures will appear near
1,000 connections regardless of available CPU or RAM.

Raising the limit is necessary but does not remove the linear socket scheduler
cost. Long-term, a multiplexed core-to-SuperUI bridge should carry many bots over
one or a small number of connections.

### SuperUI GC is the current memory wall — WRONG, see Session 2

> This section is kept as written because the reasoning was sound given the
> measurements available at the time, and because the way it was wrong is
> instructive. Every observation below is real. The conclusion drawn from them
> is not: the cause was CircuitTrace shadow mode, and no GC setting was involved.


The important pattern is:

- RSS follows GC committed memory closely;
- Linux memory is overwhelmingly anonymous/private;
- fragmentation has ranged from tens of MiB to more than 3 GiB;
- allocation has remained approximately 75–124 MiB/s;
- bridge backlog and failure counters stay at zero;
- the C++ core grows much more slowly than SuperUI.

This does not yet prove a retained-object leak. It demonstrates an expensive
allocation workload combined with Server GC committing and retaining very large
segments. The process can contract after full collections, but its peaks and
plateaus are too large to extrapolate safely to thousands of bots.

### Logging is an allocation amplifier

`FleetReport.MaxRows` is 60, and a full fleet report is logged roughly every 30
seconds. At scale, each Info message includes detailed state for 60 bots and an
"N more bots" suffix. Additional per-bot planner, path, vendor, and brain logs
are also frequent.

This logging probably does not explain the entire heap by itself, but it creates
avoidable string, formatting, JSON/log-provider, and journal work. The Info
fleet report should contain aggregate counts only. Detailed rows should move to
Debug, tracing, or an on-demand endpoint.

## Immediate operating guidance

> **Status after Session 2:** conditions 2–5 are met and have held for hours.
> Condition 1 (descriptors) is not, and is now the sole blocker. Condition 3 is
> met decisively — RSS is flat at 1.1 GiB, not merely plateaued.
>
> Judge condition 4 on the **peak** UI queue metrics, not the instantaneous ones:
> the "0 pending states" readings in this document were an artifact of sampling a
> 200 ms drain every 30 s. Real value is ~150 bots at 0.20 s peak age.

Do not increase beyond 692 until all of the following are true:

1. VMaNGOS's soft descriptor limit has been raised and verified on the actual
   running process.
2. The 692-bot run has completed at least one 30–60 minute observation window
   without available RAM approaching 6 GiB or sustained swap I/O.
3. SuperUI RSS and GC committed memory have plateaued or contracted after a full
   GC cycle.
4. UI pending states continue draining in under ten seconds with no rising
   failures or requeues.
5. Recent logs remain free of repeated world updates above 100 ms and continuous
   reconnect/recycle growth.

If available RAM falls below 6 GiB, swap-in/out remains nonzero across successive
samples, or SuperUI continues monotonic growth, Nico should reduce the
owner-operated bot load or restart the experiment with the next GC configuration.

## Session 2 — 2026-08-29 evening

The plan above called for a DATAS A/B as the next experiment. That was not run,
because a cheaper measurement was added first and it changed the diagnosis.

### The measurement that changed it

`POST /Bots/ScaleLiveHeap` forces one blocking compacting gen2 collection and
reports **live** managed bytes. The periodic sampler could not answer this: its
`ManagedAllocatedBytesEstimate` includes uncollected garbage, and `HeapSizeBytes`
reports the last *ephemeral* collection, not live data. The 4.65 GiB figure this
document treats as evidence of retention was an ephemeral-GC artifact.

| Probe | Bots | Live | Per bot | RSS before → after | Pause |
| --- | ---: | ---: | ---: | ---: | ---: |
| 22:58:28Z | 692 | 996.6 MiB | 1.44 MiB | 6,589 → 1,248 MiB | 0.76 s |
| 23:05:34Z (rings full) | 692 | 1,538.9 MiB | 2.22 MiB | 6,349 → 1,790 MiB | 0.70 s |

Roughly 5.2 GiB of the 6.4 GiB was collectable garbage. Live data per bot was
never the problem, and the projection that made 5,000 bots look impossible
(~33 GiB) was wrong by an order of magnitude.

### The actual cause: CircuitTrace shadow mode

Shadow mode was **on**, persisted in `bot_settings` and silently restored on
every restart. Per bot it retains up to `SegmentRingCap` (1,024) tick segments
plus `DecisionHistoryCap` (2,048) decision runs.

The mechanism that made it expensive is `DecisionRun.Representative`: a decision
holds a reference to its `TickSegment`, so the decision history pins segments the
ring has **already evicted**. Real retention per bot is therefore up to 3,072
segments, not the 1,024 the ring cap implies.

Measured between the two probes: decision runs grew by 539,373 and live bytes
grew by 542.3 MiB — **~1,006 bytes per decision run**, which puts circuit-trace
retention at roughly 1.3–1.4 GiB of the 1.54 GiB live heap.

Turning it off, at a constant 692 bots:

| | Shadow on | Shadow off, rings still resident | After restart |
| --- | ---: | ---: | ---: |
| RSS | ratcheting to 8.40 GiB | flat 2.75 GiB | flat **1.08–1.10 GiB** |
| GC committed | 8.17 GiB | 2.50 GiB | 0.90 GiB |
| Natural gen2 | stalled at 3–4 | keeping up | keeping up |
| Allocation | ~122 MB/s | ~103 MB/s | ~120 MB/s |
| Brain tick p95 | ~36 ms | ~30 ms | **~26 ms** |

Note the last column: allocation returned to ~120 MB/s while RSS stayed flat at
1.1 GiB. Shadow's problem was never allocation *volume* — its allocations were
long-lived, so they survived gen0 and promoted. Ordinary bot churn dies young.

Rings are only released on bot eviction or restart, so `mode=off` stops the bleed
but does not reclaim what is already held; the drop from 2.75 to 1.1 GiB required
the restart.

### Descriptors and PlayerLimit are different axes

`ss` on the live process settles what the 728 descriptors actually are:

```
mangosd: 708 socket fds of 728 total
  of which peer = :3444 (SuperUI bridge): 692    ← one per bot
.server info: Players online: 692 (0 queued)
```

The descriptors are consumed by the **bridge**, not by player sockets — the bots
are socketless as clients. So:

- `RLIMIT_NOFILE` (1024, systemd's default; the unit sets nothing) binds at
  ~988 bots. Failure is `EMFILE` on the bot's outbound `connect()`, leaving a bot
  that exists in-world but is invisible to SuperUI.
- `PlayerLimit` (1000) caps world sessions, and bots *are* real sessions.
  Failure is `AddQueuedSession` — the bot connects its bridge fine and never
  enters the world.

Raising either alone just moves the wall. The core never calls `setrlimit`, and
`/etc/security/limits.conf` does not apply to systemd services.

### Sockets themselves are cheap

The descriptor ceiling looked like a design flaw. Measuring what a socket
actually costs shows it is not:

```
TCP: inuse 1475  alloc 1478  mem 2190 pages    ← 8.6 MiB total, ~5.9 KB/socket
tcp_mem pressure threshold: 378,726 pages      ← 1.44 GiB
```

At 10,000 bots, 20,000 bridge sockets come to roughly **117 MiB of kernel
memory** — about 8% of the level where the kernel starts applying TCP memory
pressure. Descriptors above that are table entries, and 65,535 against 20,000 is
not close.

So one TCP connection per bot is fine, and the 1024 limit was an unconfigured
systemd default rather than a property of the bridge design. The per-bot costs
that remain, at 10,000 bots:

| Cost | At 10,000 bots | Verdict |
| --- | ---: | --- |
| Kernel socket memory | ~117 MiB | irrelevant |
| C++ fixed recv buffers (4 KiB each) | ~40 MiB | irrelevant |
| `send()` syscalls on the map thread | ~2,000/sec | ~1% of a core |
| **C# per-connection watchdog timers** | **10,000 × 1 Hz** | the only real one |

That last row is §F1, and it is worth doing on its own. Multiplexing the bridge
(§F4) is deferred — it would solve a problem the measurements say does not exist.

### Instrumentation added this session

- `POST /Bots/ScaleLiveHeap` — rate-limited forced live-heap measurement.
- High-water marks on the UI buffer (`PeakPendingBotCount`, `PeakPendingAgeSeconds`,
  `PeakBatchSize`, `DrainCycles`), recorded at drain cadence and reset only by the
  sampler.
- `PerBotCost` on every snapshot — the only figures that extrapolate.
- `BrainLoopMetrics` — median/p95/max/peak for roster-sync, brain-ticks,
  fleet-report and whole-iteration.
- `CircuitTrace.GetRetentionSnapshot()`, including segments pinned only by the
  decision history.
- `CircuitTraceHost.ResolveStartupMode()` — **shadow is never restored from
  settings**; startup always resolves to Off and heals the stored value.
- FleetReport split into summary (logged) and detail (on demand), an `IsEnabled`
  guard on the eagerly-evaluated log argument, bounded top-K row selection, and
  removal of its own probes.
- Dashboard: per-process CPU/RAM cards plus a per-core breakdown
  (`ProcessResourceSampler`, `ProcessCoreSampler`).

### Traps worth recording

- **The world process is named `mangosd-main`.** `pgrep -x mangosd` matches the
  unrelated CMaNGOS process; `pgrep -f` on the vmangos path returns a 5-descriptor
  screen wrapper first. Select by RSS. `ProcessManagerService` already resolves
  this correctly; ad-hoc monitoring scripts do not.
- **Instantaneous queue sampling lied.** Every "0 pending states" observation in
  this document was sampling luck: 30-second samples of a queue the publisher
  drains every 200 ms. The real backlog is a steady ~150 bots at 0.20 s peak age.
- **The host's 8 GB of swap belongs to the idle CMaNGOS process** (2.69 GB of it),
  not to either test process. Neither mangosd nor SuperUI has a single swapped page.
- **Per-core CPU is the only useful CPU view here.** mangosd's work sits on two
  cores (the continent map threads) at 19% and 16%. Any figure averaged across
  32 cores hides both the concentration and the headroom.

## Prioritized next changes

Reordered after Session 2. Items A and B are demoted; the descriptor/session work
and the load generator are promoted.

### A. First isolated configuration test: enable DATAS

> **Status: demoted, not cancelled.** This was aimed at a heap that ratcheted to
> 10 GiB. That heap now sits flat at 1.1 GiB with natural gen2 collections keeping
> up, so DATAS would be tuning a problem that no longer dominates. Still worth
> running before 2,500+, but it is no longer the critical path.

The leading test is .NET 8 dynamic adaptation to application sizes (DATAS):

```text
DOTNET_GCDynamicAdaptationMode=1
```

Equivalent runtime configuration:

```json
{
  "runtimeOptions": {
    "configProperties": {
      "System.GC.DynamicAdaptationMode": 1
    }
  }
}
```

DATAS is designed to make heap size more proportional to long-lived data. It is
available in .NET 8 and enabled by default only starting in .NET 9. GC settings
are read when the process starts, so this requires an owner-operated SuperUI
restart. Reference: <https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector>.

Test it alone first. Keep the same bot count and workload so the comparison is
meaningful. Record peak RSS, post-GC RSS, committed bytes, fragmentation,
allocation rate, CPU, and UI backlog for at least 30–60 minutes.

Success criteria:

- materially smaller RSS/committed peaks;
- lower retained plateau after full GC;
- no unacceptable increase in GC pause or CPU;
- unchanged bridge reliability.

### B. Second isolated GC test: cap Server GC heap count

> **Status: demoted.** Same reason as A, and it must not be combined with DATAS —
> DATAS tunes heap count itself, so setting both makes the result uninterpretable.

If DATAS is insufficient, test a lower Server GC heap count separately. The host
exposes 32 logical CPUs, so default Server GC can create many heaps and reserve a
large amount of memory for a workload that is not CPU-bound.

Start with an eight-heap experiment:

```json
{
  "runtimeOptions": {
    "configProperties": {
      "System.GC.HeapCount": 8
    }
  }
}
```

The environment variable is `DOTNET_GCHeapCount`; numeric environment-variable
values are hexadecimal, while `runtimeconfig.json` values are decimal. Do not
combine this with the first DATAS experiment until each has an independent
baseline.

### C. Make fleet logging summary-only at Info

> **Status: done (Session 2), and it was bigger than described here.** The report
> string was being built every 30 s *even when Info was filtered out*, because the
> argument is evaluated before the logging call — an `IsEnabled` guard fixed that.
> Row selection also sorted the entire roster with a three-key `OrderBy` to print
> 60 lines; it is now a bounded top-K. And the renderer carried ~10 `CircuitTrace`
> probes per bot per render, firing into every bot's shadow ring — inflating the
> exact retention the report is used to investigate. Those are gone.

Change the periodic Info report to include only:

- total connected bots;
- goal/state counts;
- level range/average;
- stalled and feed-stale counts;
- perhaps the top few anomaly counts.

Move the 60 per-bot detail rows to Debug or an on-demand diagnostic endpoint.
Then compare allocation rate at the same bot count. This is the clearest
low-risk code change for allocation churn.

### D. Capture an allocation profile if allocation remains high

If allocation remains above the desired budget after DATAS and logging changes,
capture a short controlled allocation trace or managed heap dump at a fixed bot
count. Rank allocation stacks and retained types before rewriting broad areas.

Likely areas to measure include:

- periodic fleet report string construction;
- per-bot planner and circuit-trace messages;
- JSON deserialization and detached UI snapshots;
- brain scheduler scans and temporary collections;
- per-request controller projections;
- SignalR serialization.

### E. Raise and verify VMaNGOS file-descriptor limits

> **Status: still the blocker, and it is two limits, not one.** See Session 2.

Before increasing the fleet, Nico should raise the active service/process soft
limit to a deliberate value such as 65,535 and verify `/proc/<pid>/limits` after
restart. A configuration file change is not sufficient unless the running
process inherits it.

The 1024 comes from systemd's default (`#DefaultLimitNOFILE=1024:524288` is
commented in `/etc/systemd/system.conf`, and `mangosd.service` sets no
`LimitNOFILE=`). A drop-in is preferred over editing the unit, so it survives a
unit rewrite and does not conflict with the INSTALL.md template:

```bash
sudo mkdir -p /etc/systemd/system/mangosd.service.d && sudo tee /etc/systemd/system/mangosd.service.d/10-nofile.conf > /dev/null << 'EOF'
[Service]
LimitNOFILE=65535
EOF
```

Verify on the running process, not the config — and note the process name:

```bash
grep "Max open files" /proc/$(pgrep -x mangosd-main)/limits
```

Raise `PlayerLimit` in the same maintenance window. Safe to raise: the socket
layer is epoll (`IoContext_linux.cpp`), not `select()`, so descriptors above 1023
are fine. The only `select()` calls are on `STDIN_FILENO` in `CliRunnable.cpp`.

| Setting | Now | 2,500 bots | 10,000 bots |
| --- | ---: | ---: | ---: |
| `LimitNOFILE` | 1024 | 65535 | 65535 |
| `PlayerLimit` | 1000 | 3000 | 12000 |
| `Network.Threads` | 1 | 2–4 | 8+ |

**To ship this to all users**, two places: add `LimitNOFILE=` to the unit
templates in `INSTALL.md` (mangosd and realmd), and have the core raise its own
soft limit to the hard limit at startup via `setrlimit` in `src/mangosd/Main.cpp`.
The second matters because it needs no root, works regardless of init system, and
covers everyone who launches from screen, tmux, or by hand.

### F. Bridge and brain scale work

> **Status: item 1 promoted, item 4 deferred.**
>
> Measuring the actual cost of a socket settled this. At 10,000 bots, 20,000
> bridge sockets cost ~117 MiB of kernel memory — 8% of the TCP pressure
> threshold — and descriptors are just table entries. Per-bot TCP is not a
> scaling limit; the 1024 ceiling was an unconfigured default.
>
> So **item 4 (multiplexing) is deferred** — see the status banner in
> [docs/bridge-multiplex-scope.md](bridge-multiplex-scope.md) — and **item 1
> is now worth building on its own**, which reverses the earlier note here. Around
> 10,000 one-second timers is the only per-socket cost that still looks
> questionable at scale, and a central sweep is roughly a day of work versus one
> to two weeks for the multiplex.
>
> Baseline for item 2, measured this session at 692 bots: roster-sync p95 15 ms,
> brain-ticks p95 26–36 ms, whole loop p95 67 ms, worst iteration 445 ms against
> a 250 ms cadence.

Implement separately revertible changes in this order:

1. Replace each connection's one-second sensory watchdog timer with one central
   sweep while preserving pre-HELLO timeouts and exact-session race safety.
   **Build this one.**
2. Give the brain scheduler eligible/due queues and timings. Avoid scanning and
   locking every bot at 4 Hz when normal decisions happen every 10–30 seconds.
3. Restrict group coordination locks to actual and previously owned group
   members instead of acquiring every fresh bot context.
4. ~~Multiplex core-to-SuperUI traffic so thousands of bots do not require
   thousands of sockets and descriptors.~~ **Deferred** — thousands of sockets
   turn out to be cheap (~117 MiB of kernel memory at 10,000 bots). Scope kept
   in [docs/bridge-multiplex-scope.md](bridge-multiplex-scope.md) for if
   that ever stops being true.

### G. Bridge load generator — now a prerequisite

There is no harness today (no `TcpClient` anywhere in `MangosSuperUI.Tests`).
Every experiment costs a deploy, a mangosd restart, and a manual bot ramp, and
the workload drifts as bots level, so a "same-load A/B" is observational rather
than controlled.

A harness that opens N loopback connections to 3444 and replays HELLO + STATE at
a fixed cadence is deterministic, needs no mangosd, and reaches 10,000
connections today. It exercises the sockets, the per-connection watchdog, the
latest-wins buffer and SignalR fan-out.

It is now a prerequisite rather than a convenience: **a harness driving N bots
over one connection *is* a multiplexed client**, so it is what lets multiplex
phase 1 be proven at 10,000 bots before any core work exists.

### H. Monitoring gaps still open

Built this session: live-heap probe, high-water marks, per-bot cost derivations,
brain-loop timing, circuit retention. Still missing:

1. **Persist samples to disk.** The in-process ring holds two hours; the 10k gate
   asks for a twelve-hour soak. One JSONL line per sample to a daily file.
2. **Evaluate the stop gates in-process** and expose `status: green|amber|red`
   with the failing list, so the cron watcher is a one-line check and an
   unattended soak can alert instead of swapping the host.
3. **Sample the core from the same endpoint** — `/proc/<pid>/status`, `limits`,
   and fd count for the configured world process, including the fd/soft-limit
   ratio. One endpoint should answer the whole gate table. Must select the
   process by RSS, not by name (see Session 2 traps).
4. **A memory ceiling.** SuperUI shares a host with the world server and has no
   bound; a regression can take mangosd down with it. `MemoryMax=` on the unit
   (or `GCHeapHardLimit`) converts that into a contained single-process failure.
   This is a safety change, not an optimisation — it belongs before any long
   unattended soak.

### I. Core work for 2,500–10,000 bots

1. Introduce hot/warm/cold AI and sensory cadence. Combat, grouped, and nearby
   bots stay hot; distant idle bots update less frequently; cold population is
   persisted without full-rate embodiment.
2. Suppress or summarize client-only packet construction for socketless bots
   when the AI does not consume those packets.
3. Measure and reduce visibility fan-out, especially in dense bot clusters.
4. Spread bots geographically to use existing map update threads.
5. Parallelize isolated measured subsystems first: path queries, immutable
   sensing, and batch preparation.
6. Treat parallel `Player::Update` as an ownership/message-passing redesign,
   not a loop with `std::thread` added around it.
7. If one process cannot meet the fully embodied target, shard by map,
   population role, or worldserver instance.

## Where modern C++ helps—and where it does not

Modernizing the core can help with:

- explicit ownership through RAII and smart pointers;
- safer views and spans instead of unchecked pointer/length pairs;
- stronger type modeling and fewer sentinel values;
- `std::jthread`, stop tokens, atomics, latches, and other structured
  concurrency primitives;
- allocators and `std::pmr` for measured high-churn subsystems;
- better profiling, sanitizers, static analysis, and compiler optimization;
- clearer message-passing boundaries needed for safe parallel work.

It does not automatically:

- parallelize the world loop;
- make shared game objects thread-safe;
- eliminate visibility and packet fan-out;
- remove database contention;
- reduce AI cadence;
- make 10,000 embodied players behave like 10,000 cheap records.

The recommended approach is incremental modernization around measured hot paths,
not a broad language-standard rewrite before profiling.

## Promotion and stop gates

| Fleet step | Minimum soak | Promotion requirements |
| --- | ---: | --- |
| 692 current | 30–60 min | ✅ soaked hours, heap flat at 1.1 GiB, no stop gate — ❌ FD limit still 71% |
| 750 | 30 min | No UI backlog, sensory storm, or memory regression |
| 1,250 | 60 min | Raised FD limit verified; world updates within budget |
| 2,500 | 2 hours | RSS plateaus, swap I/O negligible, saves keep up |
| 5,000 | 4 hours | Activity tiers and due-queue scheduling proven |
| 10,000 | 12 hours | No monotonic memory growth, tick overruns, or reconnect churn |

Stop or roll back a load step when any of these occurs:

- available RAM below 6 GiB;
- sustained swap-in/out activity;
- UI pending states older than ten seconds;
- publish failures or requeues that continue rising;
- continuous stale, recycled, or reconnect growth;
- repeated world updates above the configured 100 ms map interval;
- monotonic SuperUI RSS/GC committed growth without a leveling GC cycle;
- descriptors above 70% of the active soft limit;
- increasing character-save or database latency.

## Current decision

*Superseded by Session 2. Kept for the record:*

> The move from 342 to 692 bots proved that the state batching foundation works
> and that VMaNGOS has substantial CPU headroom at this scale. It also exposed
> two blockers before the next ramp: the descriptor ceiling, and SuperUI's GC
> memory behavior. The next useful experiment is a same-load A/B run with DATAS.

### Decision after Session 2

**Memory is no longer a blocker.** At 692 bots SuperUI holds 1.1 GiB RSS with GC
committed at 0.9 GiB and natural gen2 collections keeping up — down from a heap
that ratcheted to 8.4 GiB. Brain tick p95 is 26 ms. The UI queue drains every
200 ms with zero failures and zero requeues. The 692 step has soaked for hours
and passes every promotion gate **except descriptors**.

The DATAS A/B is therefore not the next experiment. The order is:

1. **Raise `LimitNOFILE` to 65535 and `PlayerLimit` to 3000** (§E), verified on
   the running process. Two different resources; both bind. This is the only
   thing blocking the staircase.
2. **Add the `MemoryMax=` ceiling** (§H.4) before any long unattended soak.
3. **Resume the staircase at 750**, then 1,250, per the existing gates.
4. **Build the load generator** (§G) — it unblocks controlled A/B testing and is
   a prerequisite for multiplex phase 1.
5. **Ship the descriptor fix to all users** — `LimitNOFILE=` in the INSTALL.md
   unit templates, plus `setrlimit` at core startup.
6. **Replace the per-connection watchdog timers with one sweep** (§F1) — about a
   day, and it removes the only per-socket cost that still looks questionable at
   10,000 bots.

DATAS (§A) and heap-count capping (§B) move behind all of the above, to be
revisited if memory becomes interesting again above 2,500 bots.

**Multiplexing the bridge is deferred outright.** Measured socket cost is ~117 MiB
of kernel memory at 10,000 bots against a 1.44 GiB pressure threshold, so the
transport is not a scaling limit — the 1024 descriptor ceiling was an
unconfigured systemd default, not a design property. The
[scope](bridge-multiplex-scope.md) is kept for if that changes.

The bottleneck now worth tracking is the **map thread**: mangosd's load sits on
two cores at 19% and 16% at 692 bots, which extrapolates to saturation somewhere
around 2,000–3,600 bots. That is the wall the staircase is walking toward, it is
in the core rather than SuperUI, and the per-core dashboard added this session is
how it will be seen coming.

### Standing rule from this session

CircuitTrace shadow mode costs roughly 2 MiB per bot retained, ~20% allocation,
and ~10 ms of brain tick p95. It no longer survives a restart by design. Turn it
on deliberately for an investigation, and expect to turn it off again — and note
that `mode=off` stops recording but does not release rings already held, so
reclaiming that memory needs a restart.
