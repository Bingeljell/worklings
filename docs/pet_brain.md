# Pet Brain

The Pet Brain is the Workling's life simulation — the **condition layer** from the [progression design](progression.md). It runs entirely locally, works without any integration, and is deterministic: the same state and the same clock always produce the same pet.

Pixel is the current test Workling and can appear as the Wildkin moss-fox, Elemental ember-newt, or Relicborn keyback pangolin. Changing family never resets care progress. Renaming is the same shape of change: `PetState.renamed(to:)` trims the input, rejects anything empty or over `PetState.maximumNameLength` (24 characters) by leaving the pet unchanged, and otherwise preserves every other field exactly like family selection does. The paw menu's "Rename…" opens a system alert; the care card's pencil icon next to the name opens an inline editor — both call the same `PetSession.rename(to:)`.

## Who owns what

`CompanionCore` owns the rules: needs, moods, care outcomes, and time progression. `PetSession` is the live boundary — it ticks the simulation once a minute, performs care actions, switches families, and saves after changes. Windows, menus, and artwork live elsewhere.

## Condition

Four needs, each `0...100`, and **higher is always better**:

| Need | A new Workling starts at |
| --- | ---: |
| Fullness | 85 |
| Energy | 80 |
| Happiness | 70 |
| Trust | 50 |

New Worklings favour Berries and Puzzle play.

> Internals: the code stores `hunger` and the interface derives `Fullness = 100 - hunger`. Only hunger is persisted. Everywhere else — docs, UI, thresholds — we speak Fullness.

## Time passing

Per hour away or idle:

| Need | Change per hour |
| --- | ---: |
| Fullness | -4 |
| Energy | -3 |
| Happiness | -1 |
| Trust | stable |

A starving or exhausted Workling also bleeds Happiness and Trust. Offline progression caps at seven days, so a long trip doesn't come home to a tragedy — but `lastUpdatedAt` always advances, so the same absence is never punished twice.

These rates are alpha tuning. The current Fullness drain can make Pixel feel permanently hungry after long absences; expect rebalancing from real usage.

## Care actions

| Action | Effect |
| --- | --- |
| Favourite food | Fullness +30, Happiness +8, Trust +3 |
| Other food | Fullness +20, Happiness +3, Trust +1 |
| Favourite play | Fullness -8, Energy -14, Happiness +22, Trust +6 |
| Other play | Fullness -7, Energy -12, Happiness +14, Trust +3 |
| Pet | Happiness +8, Trust +4 |
| Sleep | Fullness -6, Energy +35, Happiness +2 |

Everything clamps to `0...100`. Feed is unavailable at Fullness 100, Play below 15 Energy, Sleep at 100 Energy; Pet always works. Favourites earn bigger reactions. Asking an exhausted Workling to play gets a refusal, not a state change.

## Mood

First match wins:

1. **Hungry** — Fullness `<= 25`
2. **Sleepy** — Energy `<= 20`
3. **Wary** — Trust `<= 20`
4. **Sad** — Happiness `<= 30`
5. **Happy** — Happiness `>= 75`, Trust `>= 60`, and Fullness `>= 60`
6. **Content** — otherwise

`PetCareStatus` separately ranks notice/urgent/critical conditions for hover summaries and action availability — see the [interaction model](pet_interaction.md).

## Activity events

Real-world stimulus arrives as normalized, content-free events — kind, timestamp, source id, nothing else. See the [progression design](progression.md) for the vocabulary and sources.

Structural events (`workStarted`, `workEnded`, `awaitingInput`, `userIdle`) shape a short-lived **activity context** that is never persisted and expires to quiet after 30 minutes without events. That context changes how fast needs drain:

- While work is happening, Fullness drains 1.25× faster and Energy 1.3× faster — your Workling works up an appetite and gets tired alongside you.
- While the user is away, Trust drains at a **two-tier rate**: the first hour of an absence costs 2/hour, and anything beyond that tapers to a gentle 0.2/hour. A quick break costs a little; an evening or a weekend away costs very little more than the first hour did. It stops the moment `userReturned` arrives, since that flips the context back to present.

Every event gets a visible reaction, so its effect is never invisible — including the structural ones, which move no needs directly but still say something:

| Event | Effect | Reaction |
| --- | --- | --- |
| `dailyWake` | Happiness +3, Trust +1 | "A new day!" |
| `taskCompleted` | Happiness +4 | "We did it!" |
| `taskFailed` | Fullness -4, Energy -3, Happiness -3 | "We'll get the next one." |
| `milestone` | Happiness +6, Trust +2 | "Shipped!" |
| `userReturned` | none directly — presence can't be farmed | "You're back!" |
| `workStarted` | none | "Let's get to work!" |
| `workEnded` | none | "Taking a breather." |
| `awaitingInput` | none | "Waiting on you…" |
| `userIdle` | none directly — only the drain above | "Oh, you're away…" |
| `workLogged` | Happiness +3, gated by cooldown and daily cap | "Logged!" |

These values are alpha tuning. In debug builds only, three environment variables make manual testing practical without waiting on real clocks: `WORKLINGS_IDLE_THRESHOLD_SECONDS` shortens how long counts as "away," `WORKLINGS_PRESENCE_POLL_SECONDS` shortens how often presence is checked, and `WORKLINGS_DEBUG_RATE_SCALE` multiplies every per-hour need rate so a few real seconds can stand in for hours. The paw menu's **Simulate Activity** submenu fires any event by hand and shows the live context. All of this is compiled out of release builds.

**Run a Full Day, Sped Up** is a scripted rehearsal at the top of that same submenu: `dailyWake` → `workStarted` → (11 simulated minutes later) `workEnded` → `workLogged` → `taskCompleted` → `milestone`, paced about 1.5 real seconds apart so reactions and XP are visible one at a time instead of all at once. Every step's timestamp is anchored backward from the moment the script starts, not forward from it, so the pet's `lastUpdatedAt` never lands in the future — a forward-anchored script would leave real-time condition decay frozen until wall-clock time caught up to the simulated end point. The `workStarted`→`workEnded` gap is deliberately just past Focus Session's minimum qualifying duration so that XP grant actually fires. Open the care card's Stats tab before running it to watch XP and stats move live.

The live presence source keeps a genuine absence alive by quietly re-touching the context roughly every 15 seconds, without repeating the "Oh, you're away…" reaction — so the two-tier rate above sees the absence's real duration rather than losing track of it. The 30-minute expiry is a fallback for abnormal termination only (a crash, a `workStarted` whose `workEnded` never arrives), not the everyday path.

## Focus Session

A paw-menu item and a care-card button that toggle between "Start Focus Session" and "End Focus Session," firing real `workStarted` and `workEnded` events for a work block with an actual beginning and end — unlike Log Work's point-in-time nature. It's tagged with the `manual` source id, the same as Log Work: starting or ending the block is still something the user asserts by clicking, not something Worklings can verify on its own. A future agent adapter emitting the same event kinds automatically would carry a different source id, so the two can be treated differently once that distinction matters.

Neither `workStarted` nor `workEnded` grants Happiness or Trust directly — there is nothing to game, because the only effect is the existing working multiplier (see [Activity events](#activity-events) above): Fullness and Energy drain faster for the duration of the block, exactly as they already do for the simulated version of these events. This is the first real trigger for that multiplier; the [progression design](progression.md) lists sustained work blocks as a planned XP source, which will read from this same event pair once it exists.

A session's XP duration is measured between the events' own timestamps, never delivery time, and **idle time inside a block doesn't count**: returning from an absence shifts the block's effective start forward by the time away, and ending a block while still away stops counting at the moment of departure. A block worked 10 minutes, idled 30, worked 5 reads as 15 focus minutes.

## Log Work

The first self-reported source that grants a reward: a paw-menu item and a care-card button that let you tell Pixel about work with no natural start or end — a meeting, a decision, helping someone. It fires `workLogged`, tagged with the `manual` source id so it's always distinguishable from externally verifiable sources like a future GitHub milestone.

There is no user-chosen point value — every credited log grants the same fixed Happiness gain, because a self-adjustable reward is exactly the loophole that makes idle-game economies gameable. Fairness instead comes from two caps, mirroring the "caps, not cryptography" principle in the [progression design](progression.md):

- A cooldown between credited logs.
- A hard daily cap on how many logs are ever credited.

Both are checked before the action is even available — Log Work is disabled with an explanation exactly like Feed at zero hunger, never silently clicked and rejected. `PetBrain.workLogAvailability` is the single source of truth both the menu and the care card read.

The daily cap is tracked on the save (`lastWorkLogAt` plus the `workLog` daily tally) but never proactively reset: a stale count from a previous day is simply ignored once the tally's stored date no longer matches today, so there is no day-rollover code path to get wrong. `workLog` and the per-source `dailyXP` ledger share one `DailyTally` type — the single place that "valid only today" bookkeeping lives.

## Presentation

`PetPresentation` turns mood and the latest reaction into a face, palette, label, and short thought; `WorklingPetView` maps that to the selected family's sprite frames. Care reactions take over for about three seconds, then normal state resumes.

## The save

Versioned JSON at `~/Library/Application Support/Worklings/pet-state.json`, written atomically. Version 1 holds: schema version, name, family, the four needs (as hunger internally), favourites, the last progression timestamp, Log Work's cooldown/daily-cap bookkeeping, and — additively, per the [progression design](progression.md) — total XP, class, stats, and daily XP-accrual bookkeeping. Level is never itself stored; it is always derived from total XP.

An unreadable save is never overwritten — it's preserved, persistence pauses for the session, and a fresh in-memory pet takes over. First launch after the rebrand copies a legacy Build Companion save forward without deleting it.

## Checks

`swift run CompanionCoreChecks` covers clamping, defaults, mood priority, deterministic simulation, offline caps, care tradeoffs and refusals, persistence round trips, corrupt-save preservation, family switching, renaming validity, urgency, presentation, placement, Log Work's cooldown/daily cap/day rollover, and the [XP/level/class/stat system](progression.md#tuning-reference)'s curve, condition multiplier, per-source and overall daily caps, day rollover, and class-weighted stat growth.

## Tuning reference

Every number on this page is alpha tuning, but they live in different places depending on how they're changed. This table is the index — if a value below drifts from the source, trust the source.

| Knob | Default | Where |
| --- | --- | --- |
| Fullness / Energy / Happiness decay per hour | 4 / 3 / 1 | `PetSimulationRates` in `Sources/CompanionCore/PetBrain.swift` |
| Maximum offline catch-up | 7 days | `PetSimulationRates.maximumOfflineHours` |
| Working Fullness / Energy multiplier | 1.25× / 1.3× | `PetSimulationRates.workingHungerMultiplier` / `.workingEnergyMultiplier` |
| Away Trust rate (first hour / beyond) | 2/hour / 0.2/hour | `PetSimulationRates.awayTrustPerHour` / `.longAwayTrustPerHour` |
| Away grace period | 1 hour | `PetSimulationRates.awayGracePeriodHours` |
| Log Work cooldown / daily cap / gain | 30 min / 6 per day / +3 Happiness | `PetSimulationRates.workLogCooldownMinutes` / `.workLogDailyCap` / `.workLogHappinessGain` |
| Feed (favourite / other) | Fullness +30/+20, Happiness +8/+3, Trust +3/+1 | `PetBrain.perform`, `.feed` case (inline, not in `PetSimulationRates`) |
| Play (favourite / other) | Fullness -8/-7, Energy -14/-12, Happiness +22/+14, Trust +6/+3 | `PetBrain.perform`, `.play` case (inline) |
| Pet / Sleep | Happiness +8/Trust +4 · Fullness -6/Energy +35/Happiness +2 | `PetBrain.perform`, `.pet`/`.sleep` cases (inline) |
| Play requires Energy ≥ | 15 | `PetBrain.perform`, `.play` case (inline) |
| `dailyWake` / `taskCompleted` / `taskFailed` / `milestone` deltas | see the [event table](#activity-events) above | `PetBrain.observe` (inline) |
| Mood thresholds (Hungry/Sleepy/Wary/Sad/Happy) | Fullness ≤25, Energy ≤20, Trust ≤20, Happiness ≤30, Happy needs all three healthy | `PetState.mood` in `Sources/CompanionCore/PetState.swift` (inline) |
| Notice / Urgent / Critical thresholds | Fullness 45/25/10, Energy 45/20/10, Happiness 45/30/15, Trust 35/20/10 | `PetCareStatus` condition functions in `Sources/CompanionCore/PetCareStatus.swift` (inline) |
| Activity context expiry | 30 minutes | `ActivityContext.defaultExpiryInterval` in `Sources/CompanionCore/ActivityEvent.swift` |
| Presence idle threshold / poll interval | 5 min / 15 sec | `PresenceEvaluator.defaultIdleThreshold` (CompanionCore) / `PresenceMonitor`'s `pollInterval` default (`Sources/Worklings/PresenceMonitor.swift`) |
| Maximum pet name length | 24 characters | `PetState.maximumNameLength` in `Sources/CompanionCore/PetState.swift` |
| Debug-only overrides | env vars, compiled out of release | `WORKLINGS_IDLE_THRESHOLD_SECONDS`, `WORKLINGS_PRESENCE_POLL_SECONDS`, `WORKLINGS_DEBUG_RATE_SCALE` in `Sources/Worklings/AppDelegate.swift` |

Everything in `PetSimulationRates` is a named, constructor-injected constant — the easy case, already the right shape for tuning. Everything marked "inline" is a magic number sitting directly in a `switch` case, which works but means tuning it means editing source and rebuilding rather than adjusting one obvious place. Consolidating the inline constants into `PetSimulationRates` (or a sibling struct) so every knob lives in one discoverable, named location is worth doing — deliberately not done now, to avoid restructuring numbers that are still actively being tuned turn by turn.

## Next Pet Brain work

- Consolidate the "inline" tuning constants above into named, injectable structs, once the numbers themselves have settled down.
- Tune need rates and XP/stat-growth rates from real usage.
- Personality beyond two favourites.
- Reversible neglect and runaway recovery.
- Mood- and need-driven movement, attention-seeking, and sleep intents on top of idle roaming.
- Adoption and initial creature setup without resetting relationship state.
