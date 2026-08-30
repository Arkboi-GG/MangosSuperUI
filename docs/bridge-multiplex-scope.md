# Multiplexed core↔SuperUI bridge — scope

Replaces one TCP connection per bot with one (or a few) connections carrying
every bot, so descriptors, tasks, and timers stop scaling with fleet size.

Scoped 2026-08-29 against the live 692-bot fleet.

> ## Status: DEFERRED — do not build this yet
>
> This was scoped believing per-bot TCP was a structural scaling limit. Measuring
> it showed otherwise:
>
> ```
> TCP: inuse 1475  alloc 1478  mem 2190 pages   ← 8.6 MiB total, ~5.9 KB/socket
> tcp_mem pressure threshold: 378,726 pages     ← 1.44 GiB
> ```
>
> At 10,000 bots that is 20,000 sockets ≈ **117 MiB of kernel memory**, about 8%
> of where the kernel begins applying TCP memory pressure. The 1024 descriptor
> ceiling was an unconfigured systemd default, not a property of the design; one
> drop-in removes it through 10k.
>
> **The real bottleneck is elsewhere.** mangosd's load sits on two cores — the
> continent map threads — at 19% and 16% at 692 bots. That trend reaches
> saturation somewhere around 2,000–3,600 bots, and no amount of bridge work
> moves it.
>
> **What to build instead:** plan item §F1 on its own — replace the ~10,000
> per-connection watchdog timers with a single sweep. That is the only per-socket
> cost that still looks questionable at scale, and it is about a day of work
> against one-to-two weeks for the full multiplex.
>
> Revisit this document if descriptor count becomes genuinely inconvenient, or if
> a future phase needs to prove the C# side at 10,000 connections. The analysis
> below stands; only its priority changed.

## Why

Measured on the running system, not estimated:

```
mangosd:   708 socket fds of 728 total
  of which peer = :3444:  692        ← exactly one per bot
SuperUI:  1032 fds                   ← ~692 bridge + ~340 baseline
```

Each bot costs **two file descriptors** (one per end), and mangosd's soft
`RLIMIT_NOFILE` is 1024. Baseline is ~36, so the wall is **~988 bots** — the
`accept`/`connect` fails with `EMFILE` and the bot exists in-world but is
invisible to SuperUI.

Raising `LimitNOFILE` to 65535 unblocks the ramp today. It does not change the
fact that the design makes a kernel resource scale linearly with population.

Descriptors are only the visible part. Per bot, today:

| Cost | Where | At 692 | At 10,000 |
| --- | --- | ---: | ---: |
| Socket fd (core) | `m_bridgeSocket` | 692 | 10,000 |
| Socket fd (SuperUI) | `BotConnection.Client` | 692 | 10,000 |
| Fixed recv buffer | `m_bridgeRecvBuf[4096]` | 2.7 MiB | 39 MiB |
| Send buffer | `m_bridgeSendBuf` (cap 64 KiB) | variable | variable |
| Reader task | one per `BotConnection` | 692 | 10,000 |
| **1 Hz watchdog timer + task** | `WatchSensoryFeedAsync` | 692 | 10,000 |

At the target that is ~20,000 descriptors, ~20,000 .NET tasks, and ~10,000
one-second `PeriodicTimer`s. Multiplexing removes all of it, and it subsumes
plan item §F1 (replace the per-connection watchdog with one central sweep) —
once there is one connection, the per-connection watchdog has nothing to watch.

## What actually blocks it

Identity is currently carried by the socket, not the message.

```jsonc
// every frame today, both directions — NDJSON, one object per line
{ "type": "STATE", "payload": { ... }, "cbt": 12345 }
```

There is no `guid`. C# learns which bot a socket belongs to from `HELLO`
(`TryClaimHelloIdentity`) and thereafter *is* the identity. Every send path
resolves a `BotConnection` and writes to its stream; every receive path knows
the bot because of which reader loop it is in.

Moving identity into the frame is the whole change. Everything else follows.

## Threading — the constraint that shapes the design

The C++ bridge is pumped from `AiBotAI::UpdateBridgeTick()`, called by
`UpdateAI(diff)`, which runs **on the map update thread that owns that bot**.
Two continents means two map threads touching the bridge concurrently.

Today that is safe by construction: each bot only touches its own socket and its
own buffers, from its own map thread.

The invariant worth preserving through this change:

> **Inbound commands are applied to a bot on that bot's own map thread.**

The design below keeps that exactly. Only socket I/O moves off-thread — which is
a strict improvement, because `BridgeFlush()` currently calls `send()` on the map
update thread.

## Design

### Protocol

Add `guid` to the envelope, both directions:

```jsonc
{ "type": "STATE", "guid": 4711, "payload": { ... }, "cbt": 12345 }
```

Absent `guid` means "use socket identity" — that is what makes both transports
coexist during migration.

One new frame type for connection setup:

- `HELLO_LINK` — sent once per connection by the core, announcing multiplex mode
  and a `linkEpoch`. Per-bot `HELLO` still follows, one per bot, carrying `guid`.

### Core side

A `BotBridgeLink` singleton owning:

- one non-blocking socket and a **dedicated bridge thread** doing all `send`/`recv`;
- a per-bot bounded outbound queue (preserving today's per-bot drop-oldest
  policy, rather than letting one bot's backlog become everyone's);
- a per-bot inbound queue, drained by that bot in `UpdateBridgeTick()` on its own
  map thread;
- a round-robin drain from per-bot queues into the shared wire buffer, so a busy
  bot cannot starve the rest.

`AiBotAI` keeps `BridgeSend`/`BridgeSendState`/`BridgeProcessLine` unchanged in
shape; they enqueue to the link instead of touching a socket. Map threads only
ever append to and pop from bounded queues under a short lock.

The round-robin fairness requirement is the same one the C# `BotStateUpdateBuffer`
already solves for the UI, and for the same reason.

### SuperUI side

`BotConnection` splits into two concepts:

- **Link** — the physical socket: reader loop, write gate, reconnect handling.
- **Bot session** — the per-guid state that everything else already uses
  (`BotState`, `SessionId`, sensory-feed stamps, circuit epoch).

Today they are one object; per-bot sessions become virtual, hosted on a link.
`Connections` keeps its `guid → session` shape, so most call sites are unchanged.

The per-connection watchdog collapses into **one sweep** over all sessions on a
timer, checking the same `LastStateReceivedUtcTicks` stamps.

## The hard parts

These are the parts to plan around; the plumbing is not the risk.

**1. Session-replacement races.** The current code is careful here —
`TryClaimHelloIdentity`, `SessionId`, and the `ReferenceEquals(active,
expectedConnection)` recheck *after* acquiring the send gate all exist to stop a
command landing on a superseded socket. There is a test pinning it
(`ReplacedSession_IsRejectedInsideMutationGateBeforeWaitResolution`).

With no per-bot socket, "superseded" must become an explicit
`(linkEpoch, guid, sessionId)` generation check. **Treat the existing tests as
the specification** — they encode races that were found the hard way.

**2. Failure isolation, the real regression.** Today one bot's socket dying
affects one bot. Multiplexed, the link dying affects the entire fleet.

A naive implementation turns one dropped connection into 10,000 disconnect
events followed by 10,000 reconnects — which trips two of the plan's own stop
gates (reconnect storm, UI backlog). The link must reconnect and **resync**:
one `HELLO_LINK` plus a bulk roster, treated by C# as "these sessions continue",
not as a mass disconnect. The brain's existing `EVICT_DISCONNECT_SEC` grace
window is the right place to absorb it.

**3. Head-of-line blocking.** One shared send buffer means a full kernel buffer
stalls every bot. Bounded per-bot queues in front of the shared buffer keep the
drop policy per-bot, as it is today.

**4. Ordering.** Per-bot ordering must survive; cross-bot ordering never
mattered and must not be accidentally relied upon.

## Phasing

Each phase ships and is revertible on its own.

| Phase | Change | Risk |
| --- | --- | --- |
| 0 | Add `guid` to the envelope both sides; ignored where redundant | none — no behaviour change |
| 1 | C# accepts a multiplexed link (virtual sessions, central watchdog sweep) | low — nothing produces one yet |
| 2 | C++ `BotBridgeLink` behind `AiBot.Bridge.Multiplex`, default **0** | low — off by default |
| 3 | Enable on a small fleet, then ramp the staircase | the real test |
| 4 | Retire the per-bot path once proven | cleanup |

Phase 1 is testable **before any core work exists** using the bridge load
generator (see the scaling plan): a harness that opens one connection and drives
N synthetic bots is exactly a multiplexed client. That makes the load generator a
prerequisite rather than a nice-to-have, and it means phase 1 can be proven at
10,000 bots without touching mangosd.

Rollback at any point after phase 2 is a config flip plus a mangosd restart — no
SuperUI redeploy, because C# keeps accepting both transports.

## Effort

Roughly 1–2 weeks of focused work: C# virtual sessions ~2–3 days, C++ link
~2–3 days, integration and soak ~2 days. The estimate is dominated by the
session-race semantics in the hard-parts section, not by the socket code.

## What this does not solve

- **`PlayerLimit`** (currently 1000, and bots *are* real sessions — `.server
  info` reports all 692 as players online). Different axis, still needs raising.
- Serial `Player::Update` within a map.
- Visibility and packet fan-out.
- Per-bot AI cadence (the hot/warm/cold tiering work).

It removes the descriptor ceiling permanently, deletes ~30,000 OS and runtime
objects at the 10k target, takes `send()` off the map update threads, and folds
in the per-connection-watchdog cleanup. It does not make the world loop parallel.
