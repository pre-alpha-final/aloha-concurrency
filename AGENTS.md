# AGENTS.md

Guidance for AI agents (and humans) working in this repository.

## What this is

**Aloha Concurrency** is a small .NET 8 / C# proof-of-concept for a lock-free,
coordinator-free concurrency-control scheme inspired by **Slotted ALOHA** (the
medium-access protocol where transmitters avoid collisions by only acting at the
start of fixed, clock-aligned time slots).

The premise, from the README: *"Concurrency model inspired by Slotted Aloha.
Trades execution time for concurrency."* It deliberately swaps a runtime
coordinator (lock server, DB transaction, compare-and-swap endpoint) for two
cheaper assumptions: **synchronized wall clocks** and an **atomic,
read-your-writes store**.

This is an experiment / thought piece, not production infrastructure. `Program.cs`
is an empty `Main`; the whole idea lives in the repo class and is exercised by one
test.

## Layout

- `src/AlohaConcurrency/RepoForRemoteResource.cs` — the entire algorithm. Start here.
- `src/AlohaConcurrency/RemoteResource.cs` — the shared resource: a `List<int>` + a `Guid Version`.
- `src/AlohaConcurrency/Program.cs` — empty entry point.
- `src/AlohaConcurrency.Tests/UnitTest1.cs` — xUnit test that fires N concurrent writers and asserts correctness + timing.
- `src/AlohaConcurrency.sln` — solution.

## Build & test

```powershell
dotnet build src/AlohaConcurrency.sln
dotnet test  src/AlohaConcurrency.sln
```

- Target framework: `net8.0`. Nullable + implicit usings are enabled.
- Only third-party runtime dependency: `Newtonsoft.Json` (used to deep-copy the
  in-memory resource via serialize/deserialize, simulating a network round-trip).
- The test is **slow by design**: under contention the model commits roughly one
  write per slot, so `[InlineData(100)]` with `SlotSizeMs = 1000` can take on the
  order of ~100 seconds. Don't treat a long-running test run as a hang.

## How the algorithm works (`Add`)

Each retry loop iteration is one ALOHA "slot":

1. **Align to the slot boundary** — sleep until the next `SlotSizeMs` wall-clock boundary so all clients wake together.
2. **Skip if late** — if more than `MaxTimeSlip` (100ms) past the boundary, `continue` to the next slot.
3. **Load** the resource, apply an **idempotent** edit (add item if not already present), stamp a **new `Version` GUID**.
4. **Save** the whole object back atomically (full-object replace).
5. **Wait half a slot**, then **reload and verify** our `Version` survived.
   - Survived → our write won, `break`.
   - Clobbered → someone else's slot overwrote us → loop and retry from fresh state.

At most one writer's version survives a slot, so at most one commit per slot and
no double-success. Losers re-read the now-current state and re-apply, so writes are
**not** lost — the model is explicitly **at-least-once**, which is why the action
must be idempotent.

## Invariants — do not break these

- **The action stays idempotent.** Correctness rests on "re-apply onto whatever is
  current." Closer to a CRDT-style merge than a general transaction. Non-idempotent
  ops (e.g. blind counter increment) are unsafe unless reframed idempotently.
- **Save is atomic full-object replace; load is read-your-writes.** The in-memory
  mock gets this from a single reference swap. Any real backend must preserve both.
- **The half-slot verify wait must stay >> the spread of save-completion times.**
  It exists so verify reads a fully settled state. Don't shorten it toward the slip
  window.
- **`MaxTimeSlip` must stay larger than worst-case clock skew** across clients, and
  comfortably smaller than half a slot.

## Intended operating envelope

The design targets a specific, narrow scenario — keep changes consistent with it:

- **Low write volume:** ~10–100 writes/day, each averaging ~1s of work. Contention
  is rare, so the one-commit-per-slot ceiling never binds in practice.
- **Latency is a non-goal.** Avoiding collisions matters; committing instantly does
  not. This is why slots can be made generously large ("over the top") to buy
  near-total collision confidence essentially for free.
- **Primitive systems** with no access to fancy concurrency primitives — but with
  standard servers running NTP, so clock drift (single-digit-to-tens of ms) sits
  well inside the slip budget.

Every write pays ~1.5 slots of latency unconditionally (align + half-slot verify),
even uncontended — that fixed cost is accepted on purpose given the above.

## Known limitations (by design, but be aware)

- Correctness is only as good as clock sync; skew beyond `MaxTimeSlip` breaks it.
- Atomicity/read-after-write must hold on the real store — fine on a local file
  (write-temp-then-rename), murkier on SMB/NFS shares.
- Inherits ALOHA's behavior under sustained high load: throughput is ~1 commit/slot
  and individual clients can be starved (probabilistic stability, not a hard
  guarantee). Out of scope for the intended low-volume envelope.

## Conventions

- C# with nullable reference types on — honor existing `!` / null annotations.
- Match the surrounding style; comments in `RepoForRemoteResource.cs` explain the
  *why* of the timing — keep them accurate if you touch the logic.
