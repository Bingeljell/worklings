# Worklings Progression Design

## Status

This is the agreed design direction for activity awareness, experience, levels, and stats. None of it is implemented. It exists so that implementation slices, and eventually external contributors, build toward one coherent game rather than a collection of features.

The care loop described in [Pet Brain](pet_brain.md) is implemented and remains the foundation this design builds on.

## The two-layer model

Worklings separates the **living pet** from the **character sheet**.

| Layer | Owns | Nature |
| --- | --- | --- |
| Condition | Hunger, Energy, Happiness, Trust | Short-horizon wellbeing. Rises and falls daily with care and activity. The tamagotchi layer, deliberately. |
| Progression | XP, level, stats | Long-horizon growth. Only ever accumulates. The MMO layer. |

Condition is not the stat block. Its job is presence and attachment: a creature that gets hungry, curls up, and misses you. The progression layer is the character sheet: how the Workling grows from companion to contender, and later, how it fights in PVE and PVP.

The layers couple in one direction: **condition gates progression.** A well-cared-for Workling earns XP at full rate. A neglected one learns slowly, and later fights below its sheet or refuses to fight. This keeps the care loop mechanically load-bearing without letting neglect destroy accumulated progress, which preserves the existing reversible-neglect principle.

## Activity events

All real-world stimulus enters through the provider-neutral boundary already defined in [Architecture](architecture.md):

```text
Activity source -> normalized event -> activity context -> Pet Brain intent -> presentation
```

A normalized event carries an event kind, a timestamp, and a source identifier. It never carries prompts, source code, diffs, commit messages, file paths, window contents, or keystrokes.

### Event vocabulary

| Event | Meaning |
| --- | --- |
| `dailyWake` | The app was opened or first used on a new calendar day |
| `workStarted` / `workEnded` | A sustained work or focus block began or ended |
| `taskCompleted` / `taskFailed` | An agent run, build, or comparable unit of work finished |
| `awaitingInput` | A connected agent is blocked on the human |
| `milestone` | A commit was made, a PR was opened, or a PR was merged |
| `userIdle` / `userReturned` | Presence changed, based on system input idle time |

### Planned sources

Ordered roughly by implementation cost:

1. **Daily wake.** The app itself is a source: the first launch or interaction of each calendar day emits `dailyWake`. This is the login-reward hook and requires no permissions or integrations.
2. **Simulated source.** A debug-only control that emits arbitrary events, used to tune pet reactions and XP rules before any real adapter exists, and to keep behavioral checks deterministic.
3. **Presence.** System input idle time (no content, no per-app visibility) drives `userIdle` and `userReturned`, and bounds work blocks.
4. **Local git.** FSEvents watching of explicitly connected repositories emits `milestone` on commit. Opt-in per repository.
5. **GitHub connect.** See below.
6. **Agent adapters.** Codex first, per the architecture doc: session lifecycle events from locally written session files map to `workStarted`, `awaitingInput`, `taskCompleted`, and `taskFailed`.

### GitHub connect

An explicit, opt-in integration that reads the user's own recent GitHub activity — commit counts, PRs opened, PRs merged — and converts it into `milestone` events.

- Authentication uses the OAuth device flow with read-only scopes; the token is stored in the user's Keychain.
- Worklings stores only event kinds, counts, and timestamps derived from the API response. Repository names, commit messages, and diffs are discarded at the adapter boundary, consistent with the content-free event contract.
- The integration is off by default, clearly disconnectable, and its absence never harms the pet.

GitHub activity has a property no local source has: it is a public, timestamped, third-party record. Merged PRs in particular are expensive to fake at scale. This makes GitHub-sourced XP the most verifiable progression input and the natural anchor for later multiplayer normalization.

## Experience

XP is earned from normalized events and from care quality, so progression is provider-neutral by construction.

| Source | Notes |
| --- | --- |
| `dailyWake` | The login reward. Modest, reliable, streak-friendly. |
| Completed work blocks | The workhorse source: sustained real activity. |
| `taskCompleted` | Agent and build completions. |
| `milestone` | Commits small, merged PRs largest. |
| Care actions | A trickle, so tending the pet always means something. |

**Condition multiplier.** XP accrual scales with current wellbeing. A Workling at strong Fullness, Energy, Happiness, and Trust earns at full rate; a neglected one at a fraction. This is one multiplier inside the Pet Brain and the primary coupling between the two layers.

**Caps and diminishing returns.** Because inputs reflect real activity, per-source and per-day caps are the fairness mechanism (see below). Exact values are deferred alpha tuning; the accrual design must support them from the start rather than retrofitting.

**Curve.** Levels 1–20 use a hand-authored XP table during alpha. Tables are easier to tune than formulas and can be replaced by a formula once the shape is validated.

## Levels

Each level grants **stat points that bank until spent.** That is the entire contract. Skill trees, abilities, gear, and cosmetics are later systems that consume banked points or unlock at level thresholds; a data-driven grant table means adding them never changes the leveling core.

## Stats

The character sheet is battle-facing, sized for eventual PVE and PVP:

| Stat | Battle meaning | Ambient meaning before battles exist |
| --- | --- | --- |
| **Vitality** | Hit points | Slower hunger and energy decay |
| **Power** | Offense | Bolder reactions and celebrations |
| **Guard** | Defense | Steadier under neglect penalties |
| **Agility** | Speed, turn order | Faster, fancier roaming |
| **Wit** | Skill and special effectiveness | Small XP bonus, stronger puzzle-play results |

Every stat has an ambient effect from day one so allocation is meaningful long before combat ships.

Points come from levels. A light grows-by-use drift may be layered on later so behavior shapes the sheet, but allocation is the primary mechanism.

**Trust and Bond.** Trust stays a condition need. If a long-horizon relationship stat proves necessary, sustained high Trust can graduate into a separate **Bond** stat; that decision is deferred.

**Families stay cosmetic for now.** Stat affinities per family (Wildkin, Elemental, Relicborn) would give mechanical identity, but they conflict with the shipped promise that switching family preserves all progress. Revisit only alongside a real adoption flow.

## Fairness

The save is a local JSON file and Worklings is open source; anyone determined can edit their pet. Fairness is therefore designed as **caps, not cryptography**:

- **Daily XP caps and per-source diminishing returns.** A perfect cheat can only compress time; it cannot produce a pet a diligent legitimate player could never have. The ceiling is the fairness mechanism.
- **Wall-clock gating.** XP accrual is bounded by real elapsed time, reusing the same discipline as the existing capped offline progression.
- **Shallow power curves.** Levels are primarily prestige and identity, so an edited save is unimpressive rather than oppressive.
- **No local save obfuscation.** It contradicts the open-source posture and has never worked anywhere.
- **Multiplayer normalization.** When multiplayer arrives, levels and stats are normalized or bracketed server-side, and claimed progression can be sanity-checked against plausible event rates — with GitHub-verifiable milestones as the strongest anchor.

Exact cap values and rates are deliberately deferred; realism-derived inputs make them straightforward to add when tuning begins.

## Persistence

Progression fields (level, XP, banked points, allocated stats, daily accrual bookkeeping) extend the existing versioned save additively, following the pattern established by the family field: older saves load unchanged with defaults, and no migration destroys care state.

## Implementation order

1. Event vocabulary, activity context, and the simulated source in `CompanionCore`, with behavioral checks.
2. `dailyWake` and presence sources — the cheapest real stimuli, shipping the "it reacts to me" moment.
3. XP, levels, and stat allocation on the event stream, as an additive save-schema revision.
4. GitHub connect and the first agent adapter, once reactions feel right on simulated and presence input.
5. Battle systems, skill trees, and multiplayer normalization, far later, on top of the sheet this document defines.
